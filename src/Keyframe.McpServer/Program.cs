namespace Keyframe.McpServer
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.McpServer.Settings;
    using Keyframe.Sdk.Serialization;
    using SyslogLogging;

    /// <summary>
    /// Entry point for the Keyframe MCP server.
    /// </summary>
    public static class Program
    {
        #region Public-Methods

        /// <summary>
        /// Main.
        /// </summary>
        /// <param name="args">Arguments: --config=&lt;file&gt;, --install[=targets], --uninstall[=targets], --endpoint=&lt;url&gt;, --help.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string configFile = Environment.GetEnvironmentVariable("KEYFRAME_MCP_SETTINGS_FILE") ?? "keyframe-mcp.json";
            bool install = false;
            bool uninstall = false;
            string targets = "all";
            string endpoint = String.Empty;

            foreach (string arg in args)
            {
                if (arg == "--help" || arg == "-h")
                {
                    ShowHelp();
                    return 0;
                }

                if (arg.StartsWith("--config=", StringComparison.Ordinal)) configFile = arg.Substring(9);
                else if (arg == "--install") install = true;
                else if (arg.StartsWith("--install=", StringComparison.Ordinal)) { install = true; targets = arg.Substring(10); }
                else if (arg == "--uninstall") uninstall = true;
                else if (arg.StartsWith("--uninstall=", StringComparison.Ordinal)) { uninstall = true; targets = arg.Substring(12); }
                else if (arg.StartsWith("--endpoint=", StringComparison.Ordinal)) endpoint = arg.Substring(11);
            }

            McpSettings settings = LoadSettings(configFile);

            if (install || uninstall)
            {
                string? token = settings.RequireAuthentication ? settings.Keyframe.AccessKey : null;
                return McpClientInstaller.Run(uninstall, targets, endpoint, token);
            }

            Console.WriteLine("Keyframe MCP server " + McpServerHost.Version + " (ALPHA)");
            Console.WriteLine("Settings file: " + Path.GetFullPath(configFile));

            LoggingModule logging = BuildLogging(settings.Logging);
            using (McpServerHost host = new McpServerHost(settings, logging))
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };

                using (PosixSignalRegistration term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    stop.Cancel();
                }))
                {
                    try
                    {
                        await host.StartAsync(stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine("Startup failed: " + ex.Message);
                        return 1;
                    }

                    try
                    {
                        await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    logging.Info("[KeyframeMcp] stopping");
                    host.Stop();
                }
            }

            return 0;
        }

        #endregion

        #region Private-Methods

        private static McpSettings LoadSettings(string file)
        {
            McpSettings settings;
            if (File.Exists(file))
            {
                settings = KeyframeSerializer.Deserialize<McpSettings>(File.ReadAllText(file)) ?? new McpSettings();
            }
            else
            {
                settings = new McpSettings();
                try
                {
                    File.WriteAllText(file, KeyframeSerializer.Serialize(settings, true), new UTF8Encoding(false));
                }
                catch (Exception)
                {
                    // A read-only working directory (containers) runs on defaults plus environment overrides.
                }
            }

            string? url = Environment.GetEnvironmentVariable("KEYFRAME_URL");
            if (!String.IsNullOrWhiteSpace(url)) settings.Keyframe.Endpoint = url;
            string? key = Environment.GetEnvironmentVariable("KEYFRAME_ACCESS_KEY");
            if (!String.IsNullOrWhiteSpace(key)) settings.Keyframe.AccessKey = key;
            string? host = Environment.GetEnvironmentVariable("KEYFRAME_MCP_HOSTNAME");
            if (!String.IsNullOrWhiteSpace(host))
            {
                settings.Http.Hostname = host;
                settings.WebSocket.Hostname = host;
                settings.Tcp.Hostname = host == "*" ? "0.0.0.0" : host;
            }

            string? requireAuth = Environment.GetEnvironmentVariable("KEYFRAME_MCP_REQUIRE_AUTHENTICATION");
            if (String.Equals(requireAuth, "false", StringComparison.OrdinalIgnoreCase) || requireAuth == "0") settings.RequireAuthentication = false;
            return settings;
        }

        private static LoggingModule BuildLogging(McpLoggingSettings settings)
        {
            LoggingModule logging = new LoggingModule();
            logging.Settings.EnableConsole = settings.ConsoleLogging;
            logging.Settings.EnableColors = settings.EnableColors;
            logging.Settings.MinimumSeverity = (Severity)settings.MinimumSeverity;
            if (!String.IsNullOrWhiteSpace(settings.LogDirectory) && !String.IsNullOrWhiteSpace(settings.LogFilename))
            {
                try
                {
                    Directory.CreateDirectory(settings.LogDirectory);
                    logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                    logging.Settings.LogFilename = Path.Combine(settings.LogDirectory, settings.LogFilename);
                }
                catch (Exception)
                {
                }
            }

            return logging;
        }

        private static void ShowHelp()
        {
            Console.WriteLine("Keyframe MCP server " + McpServerHost.Version);
            Console.WriteLine();
            Console.WriteLine("Usage: Keyframe.McpServer [options]");
            Console.WriteLine("  --config=<file>        Settings file (default keyframe-mcp.json or KEYFRAME_MCP_SETTINGS_FILE)");
            Console.WriteLine("  --install[=targets]    Register with claude, codex, gemini, cursor, mux (default all), then exit");
            Console.WriteLine("  --uninstall[=targets]  Remove the registration, then exit");
            Console.WriteLine("  --endpoint=<url>       Endpoint to register (default " + McpClientInstaller.DefaultEndpoint + ")");
            Console.WriteLine("  --help, -h             Show this help");
            Console.WriteLine();
            Console.WriteLine("Environment: KEYFRAME_URL, KEYFRAME_ACCESS_KEY, KEYFRAME_MCP_HOSTNAME, KEYFRAME_MCP_REQUIRE_AUTHENTICATION.");
        }

        #endregion
    }
}
