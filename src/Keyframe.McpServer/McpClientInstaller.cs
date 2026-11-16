namespace Keyframe.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Nodes;

    /// <summary>
    /// Registers or removes the Keyframe MCP server in the per-user configuration of Claude Code, Codex, Gemini CLI,
    /// Cursor, and Mux. Client configuration files are arbitrary user documents, so they are edited as JSON (or TOML)
    /// trees and every unrelated entry is preserved.
    /// </summary>
    public static class McpClientInstaller
    {
        #region Public-Members

        /// <summary>
        /// Name the server is registered under.
        /// </summary>
        public const string ServerName = "keyframe";

        /// <summary>
        /// Default Streamable HTTP endpoint.
        /// </summary>
        public const string DefaultEndpoint = "http://127.0.0.1:8702/mcp";

        /// <summary>
        /// Supported targets.
        /// </summary>
        public static readonly string[] SupportedTargets = new string[] { "claude", "codex", "gemini", "cursor", "mux" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Install or uninstall.
        /// </summary>
        /// <param name="uninstall">True to remove.</param>
        /// <param name="targetsCsv">Comma separated targets, or "all".</param>
        /// <param name="endpoint">Endpoint URL; empty uses the default.</param>
        /// <param name="bearerToken">Bearer token written as an Authorization header, or null for none.</param>
        /// <returns>Process exit code.</returns>
        public static int Run(bool uninstall, string targetsCsv, string endpoint, string? bearerToken)
        {
            if (String.IsNullOrWhiteSpace(endpoint)) endpoint = DefaultEndpoint;
            List<string> targets = ResolveTargets(targetsCsv);
            if (targets.Count == 0)
            {
                Console.WriteLine("No valid targets. Valid targets: " + String.Join(", ", SupportedTargets) + ", all");
                return 1;
            }

            Console.WriteLine((uninstall ? "Uninstalling" : "Installing") + " the Keyframe MCP server");
            Console.WriteLine("| Server name : " + ServerName);
            if (!uninstall)
            {
                Console.WriteLine("| Endpoint    : " + endpoint);
                Console.WriteLine("| Auth header : " + (String.IsNullOrEmpty(bearerToken) ? "none" : "Authorization: Bearer ****"));
            }

            Console.WriteLine("| Targets     : " + String.Join(", ", targets));
            Console.WriteLine();

            int failures = 0;
            foreach (string target in targets)
            {
                try
                {
                    switch (target)
                    {
                        case "claude":
                            UpsertMcpServers("claude", Path.Combine(Home(), ".claude.json"), Entry("claude", endpoint, bearerToken), uninstall);
                            break;
                        case "gemini":
                            UpsertMcpServers("gemini", Path.Combine(Home(), ".gemini", "settings.json"), Entry("gemini", endpoint, bearerToken), uninstall);
                            break;
                        case "cursor":
                            UpsertMcpServers("cursor", Path.Combine(Home(), ".cursor", "mcp.json"), Entry("cursor", endpoint, bearerToken), uninstall);
                            break;
                        case "codex":
                            InstallCodex(uninstall, endpoint, bearerToken);
                            break;
                        case "mux":
                            InstallMux(uninstall, endpoint, bearerToken);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.WriteLine("[" + target + "] ERROR: " + ex.Message);
                }
            }

            Console.WriteLine();
            Console.WriteLine(failures == 0 ? "Done." : "Completed with " + failures + " error(s).");
            return failures == 0 ? 0 : 1;
        }

        #endregion

        #region Private-Methods

        private static List<string> ResolveTargets(string targetsCsv)
        {
            List<string> resolved = new List<string>();
            if (String.IsNullOrWhiteSpace(targetsCsv) || targetsCsv.Trim().Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                resolved.AddRange(SupportedTargets);
                return resolved;
            }

            foreach (string part in targetsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = part.ToLowerInvariant();
                if (candidate == "all")
                {
                    resolved.Clear();
                    resolved.AddRange(SupportedTargets);
                    return resolved;
                }

                if (Array.IndexOf(SupportedTargets, candidate) >= 0)
                {
                    if (!resolved.Contains(candidate)) resolved.Add(candidate);
                }
                else
                {
                    Console.WriteLine("Ignoring unknown target '" + candidate + "'");
                }
            }

            return resolved;
        }

        private static JsonObject Entry(string target, string endpoint, string? bearerToken)
        {
            JsonObject entry = new JsonObject();
            if (target == "claude")
            {
                entry["type"] = "http";
                entry["url"] = endpoint;
            }
            else if (target == "gemini")
            {
                entry["httpUrl"] = endpoint;
            }
            else
            {
                entry["url"] = endpoint;
            }

            if (!String.IsNullOrEmpty(bearerToken))
            {
                entry["headers"] = new JsonObject { ["Authorization"] = "Bearer " + bearerToken };
            }

            return entry;
        }

        private static void UpsertMcpServers(string label, string path, JsonObject value, bool uninstall)
        {
            JsonObject root;
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path);
                JsonNode? parsed = String.IsNullOrWhiteSpace(existing) ? null : JsonNode.Parse(existing, null, ParseOptions());
                root = parsed as JsonObject ?? new JsonObject();
            }
            else
            {
                if (uninstall)
                {
                    Console.WriteLine("[" + label + "] not configured (no file at " + path + ")");
                    return;
                }

                root = new JsonObject();
            }

            JsonObject? servers = root["mcpServers"] as JsonObject;
            if (uninstall)
            {
                if (servers == null || !servers.ContainsKey(ServerName))
                {
                    Console.WriteLine("[" + label + "] not configured (" + path + ")");
                    return;
                }

                servers.Remove(ServerName);
                WriteJson(path, root);
                Console.WriteLine("[" + label + "] removed '" + ServerName + "' from " + path);
                return;
            }

            if (servers == null)
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }

            servers[ServerName] = value;
            EnsureDirectory(path);
            WriteJson(path, root);
            Console.WriteLine("[" + label + "] configured '" + ServerName + "' (" + path + ")");
        }

        private static void InstallMux(bool uninstall, string endpoint, string? bearerToken)
        {
            string? configDir = Environment.GetEnvironmentVariable("MUX_CONFIG_DIR");
            if (String.IsNullOrWhiteSpace(configDir)) configDir = Path.Combine(Home(), ".mux");
            string path = Path.Combine(configDir, "mcp-servers.json");

            JsonObject root;
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path);
                JsonNode? parsed = String.IsNullOrWhiteSpace(existing) ? null : JsonNode.Parse(existing, null, ParseOptions());
                root = parsed as JsonObject ?? new JsonObject();
            }
            else
            {
                if (uninstall)
                {
                    Console.WriteLine("[mux] not configured (no file at " + path + ")");
                    return;
                }

                root = new JsonObject();
            }

            JsonArray servers = root["servers"] as JsonArray ?? new JsonArray();
            JsonArray filtered = new JsonArray();
            bool found = false;
            foreach (JsonNode? item in servers)
            {
                if (item is JsonObject obj && String.Equals(obj["name"]?.ToString(), ServerName, StringComparison.OrdinalIgnoreCase))
                {
                    found = true;
                    continue;
                }

                filtered.Add(item?.DeepClone());
            }

            if (uninstall)
            {
                if (!found)
                {
                    Console.WriteLine("[mux] not configured (" + path + ")");
                    return;
                }

                root["servers"] = filtered;
                WriteJson(path, root);
                Console.WriteLine("[mux] removed '" + ServerName + "' from " + path);
                return;
            }

            Uri uri = new Uri(endpoint);
            JsonObject entry = new JsonObject
            {
                ["name"] = ServerName,
                ["transport"] = "http",
                ["url"] = uri.GetLeftPart(UriPartial.Authority),
                ["mcpPath"] = String.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/" ? "/mcp" : uri.AbsolutePath
            };

            if (!String.IsNullOrEmpty(bearerToken)) entry["headers"] = new JsonObject { ["Authorization"] = "Bearer " + bearerToken };
            filtered.Add(entry);
            root["servers"] = filtered;
            EnsureDirectory(path);
            WriteJson(path, root);
            Console.WriteLine("[mux] configured '" + ServerName + "' (" + path + ")");
        }

        private static void InstallCodex(bool uninstall, string endpoint, string? bearerToken)
        {
            string path = Path.Combine(Home(), ".codex", "config.toml");
            string header = "[mcp_servers." + ServerName + "]";
            string content = File.Exists(path) ? File.ReadAllText(path) : String.Empty;
            string without = RemoveTomlSection(content, header);
            bool existed = !String.Equals(without, content, StringComparison.Ordinal);

            if (uninstall)
            {
                if (!existed)
                {
                    Console.WriteLine("[codex] not configured (" + path + ")");
                    return;
                }

                File.WriteAllText(path, without.TrimEnd() + Environment.NewLine);
                Console.WriteLine("[codex] removed '" + ServerName + "' from " + path);
                return;
            }

            StringBuilder sb = new StringBuilder();
            string trimmed = without.TrimEnd();
            if (trimmed.Length > 0)
            {
                sb.Append(trimmed);
                sb.Append(Environment.NewLine);
                sb.Append(Environment.NewLine);
            }

            sb.Append(header).Append(Environment.NewLine);
            sb.Append("url = \"").Append(endpoint).Append('"').Append(Environment.NewLine);
            if (!String.IsNullOrEmpty(bearerToken))
                sb.Append("http_headers = { Authorization = \"Bearer ").Append(bearerToken).Append("\" }").Append(Environment.NewLine);

            EnsureDirectory(path);
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("[codex] configured '" + ServerName + "' (" + path + ")");
        }

        private static string RemoveTomlSection(string content, string header)
        {
            if (String.IsNullOrEmpty(content)) return content;
            StringBuilder sb = new StringBuilder();
            bool skipping = false;
            foreach (string line in content.Replace("\r\n", "\n").Split('\n'))
            {
                string t = line.Trim();
                if (!skipping && t == header)
                {
                    skipping = true;
                    continue;
                }

                if (skipping)
                {
                    if (t.StartsWith("[", StringComparison.Ordinal)) skipping = false;
                    else continue;
                }

                sb.Append(line).Append('\n');
            }

            return sb.ToString();
        }

        private static JsonDocumentOptions ParseOptions()
        {
            return new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
        }

        private static void WriteJson(string path, JsonObject root)
        {
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        private static void EnsureDirectory(string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        }

        private static string Home()
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        #endregion
    }
}
