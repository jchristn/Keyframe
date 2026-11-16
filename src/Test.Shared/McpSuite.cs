namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.McpServer;
    using Keyframe.McpServer.Settings;
    using Keyframe.McpServer.Tools;
    using SyslogLogging;
    using Touchstone.Core;

    /// <summary>
    /// Every MCP tool over Streamable HTTP against an in-process REST server and MCP host.
    /// </summary>
    public static class McpSuite
    {
        private const string Id = "Mcp";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "MCP server", new[]
            {
                new TestCaseDescriptor(Id, "AuthAndList", "Bearer key required; nine tools listed", async ct =>
                {
                    using (McpHarness h = await McpHarness.StartAsync(ct).ConfigureAwait(false))
                    {
                        using (HttpResponseMessage denied = await h.PostRawAsync(null, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.Unauthorized, denied.StatusCode, "no bearer token");
                        }

                        await h.InitializeAsync(ct).ConfigureAwait(false);
                        JsonElement list = await h.RequestAsync("tools/list", null, ct).ConfigureAwait(false);
                        List<string> names = list.GetProperty("tools").EnumerateArray().Select(t => t.GetProperty("name").GetString() ?? String.Empty).ToList();
                        foreach (string name in KeyframeTools.ToolNames) Check.True(names.Contains(name), "tool listed: " + name);
                        Check.Equal(KeyframeTools.ToolNames.Length, names.Count, "no extra tools");
                    }
                }, new[] { "mcp" }),

                new TestCaseDescriptor(Id, "Tools", "Health, probe, image, video job, list, cancel, endpoints, and error results", async ct =>
                {
                    using (McpHarness h = await McpHarness.StartAsync(ct).ConfigureAwait(false))
                    {
                        await h.InitializeAsync(ct).ConfigureAwait(false);
                        string image = await TestEnvironment.Base64Async(await TestEnvironment.JpegAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        string video = await TestEnvironment.Base64Async(await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

                        Check.Equal("Healthy", (await h.ToolAsync("keyframe_health", "{}", false, ct).ConfigureAwait(false)).GetProperty("Status").GetString(), "health");
                        Check.Equal("Video", (await h.ToolAsync("keyframe_probe", "{\"Data\":\"" + video + "\"}", false, ct).ConfigureAwait(false)).GetProperty("MediaKind").GetString(), "probe");

                        JsonElement analyzed = await h.ToolAsync("keyframe_image_analyze",
                            "{\"Image\":{\"Data\":\"" + image + "\"},\"Context\":[\"ctx\",{\"Label\":\"L\",\"Text\":\"t\"}],\"Fields\":[{\"Key\":\"hasCats\",\"Type\":\"Boolean\",\"Prompt\":\"Cats?\"}]}", false, ct).ConfigureAwait(false);
                        Check.True(analyzed.GetProperty("Results").TryGetProperty("hasCats", out JsonElement _), "field result");
                        JsonElement summary = await h.ToolAsync("keyframe_image_analyze", "{\"Image\":{\"Data\":\"" + image + "\"}}", false, ct).ConfigureAwait(false);
                        Check.True(summary.GetProperty("Results").TryGetProperty("Summary", out JsonElement _), "default summary");

                        JsonElement job = await h.ToolAsync("keyframe_video_analyze", "{\"Video\":{\"Data\":\"" + video + "\"},\"Scenes\":{\"Enable\":true}}", false, ct).ConfigureAwait(false);
                        string jobId = job.GetProperty("Id").GetString() ?? String.Empty;
                        string status = "Queued";
                        for (int i = 0; i < 120 && (status == "Queued" || status == "Running"); i++)
                        {
                            await Task.Delay(250, ct).ConfigureAwait(false);
                            status = (await h.ToolAsync("keyframe_job_get", "{\"JobId\":\"" + jobId + "\"}", false, ct).ConfigureAwait(false)).GetProperty("Status").GetString() ?? String.Empty;
                        }

                        Check.Equal("Succeeded", status, "job succeeded");
                        JsonElement result = await h.ToolAsync("keyframe_job_result", "{\"JobId\":\"" + jobId + "\"}", false, ct).ConfigureAwait(false);
                        Check.True(result.GetProperty("Scenes").GetArrayLength() >= 1, "scenes");
                        JsonElement waited = await h.ToolAsync("keyframe_video_analyze", "{\"Video\":{\"Data\":\"" + video + "\"},\"Wait\":true,\"WaitTimeoutMs\":60000}", false, ct).ConfigureAwait(false);
                        Check.True(waited.TryGetProperty("Telemetry", out JsonElement _), "wait returns the result");
                        Check.True((await h.ToolAsync("keyframe_job_list", "{\"Status\":\"Succeeded\"}", false, ct).ConfigureAwait(false)).GetProperty("TotalCount").GetInt64() >= 2, "job list");
                        Check.True((await h.ToolAsync("keyframe_endpoints_list", "{}", false, ct).ConfigureAwait(false)).GetArrayLength() >= 2, "endpoints");

                        string conflict = await h.ToolErrorAsync("keyframe_job_cancel", "{\"JobId\":\"" + jobId + "\"}", ct).ConfigureAwait(false);
                        Check.True(conflict.Contains("409"), "cancel finished job is a tool error: " + conflict);
                        string missing = await h.ToolErrorAsync("keyframe_job_get", "{}", ct).ConfigureAwait(false);
                        Check.True(missing.Contains("JobId"), "missing argument is a tool error: " + missing);
                        string bad = await h.ToolErrorAsync("keyframe_image_analyze", "{\"Image\":{\"Data\":\"aGVsbG8=\"}}", ct).ConfigureAwait(false);
                        Check.True(bad.Contains("422"), "undecodable media is a tool error: " + bad);
                    }
                }, new[] { "mcp", "ffmpeg" }, skip)
            });
        }

        private sealed class McpHarness : IDisposable
        {
            private readonly ServerFixture _Rest;
            private readonly McpServerHost _Host;
            private readonly CancellationTokenSource _Stop = new CancellationTokenSource();
            private readonly HttpClient _Http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            private readonly string _Url;
            private string? _Session = null;
            private int _NextId = 1;

            private McpHarness(ServerFixture rest, McpServerHost host, string url)
            {
                _Rest = rest;
                _Host = host;
                _Url = url;
            }

            public static async Task<McpHarness> StartAsync(CancellationToken token)
            {
                ServerFixture rest = await ServerFixture.StartAsync(null, token).ConfigureAwait(false);
                McpSettings settings = new McpSettings();
                settings.Keyframe.Endpoint = rest.Server.BaseUrl;
                settings.Keyframe.AccessKey = ServerFixture.AccessKey;
                settings.Http.Port = FreePort();
                settings.Tcp.Enable = false;
                settings.WebSocket.Enable = false;
                settings.Telemetry.Enable = false;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                McpServerHost host = new McpServerHost(settings, logging);
                McpHarness harness = new McpHarness(rest, host, "http://127.0.0.1:" + settings.Http.Port + "/mcp");
                await host.StartAsync(harness._Stop.Token).ConfigureAwait(false);
                await Task.Delay(300, token).ConfigureAwait(false);
                return harness;
            }

            public async Task<HttpResponseMessage> PostRawAsync(string? bearer, string json, CancellationToken token)
            {
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, _Url);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
                request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
                if (bearer != null) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + bearer);
                if (_Session != null) request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _Session);
                return await _Http.SendAsync(request, token).ConfigureAwait(false);
            }

            public async Task InitializeAsync(CancellationToken token)
            {
                await RequestAsync("initialize", "{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"keyframe-tests\",\"version\":\"1\"}}", token).ConfigureAwait(false);
                using (HttpResponseMessage r = await PostRawAsync(ServerFixture.AccessKey, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", token).ConfigureAwait(false))
                {
                }
            }

            public async Task<JsonElement> RequestAsync(string method, string? paramsJson, CancellationToken token)
            {
                int id = _NextId++;
                string body = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\"" + (paramsJson != null ? ",\"params\":" + paramsJson : String.Empty) + "}";
                using (HttpResponseMessage response = await PostRawAsync(ServerFixture.AccessKey, body, token).ConfigureAwait(false))
                {
                    IEnumerable<string>? values;
                    if (response.Headers.TryGetValues("Mcp-Session-Id", out values)) _Session = values.First();
                    string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                    if (text.StartsWith("event:", StringComparison.Ordinal) || text.StartsWith("data:", StringComparison.Ordinal))
                        text = text.Split('\n').Where(l => l.StartsWith("data:", StringComparison.Ordinal)).Last().Substring(5);
                    using (JsonDocument doc = JsonDocument.Parse(text))
                    {
                        if (doc.RootElement.TryGetProperty("error", out JsonElement error)) throw new InvalidOperationException(method + " failed: " + error.GetRawText());
                        return doc.RootElement.GetProperty("result").Clone();
                    }
                }
            }

            public async Task<JsonElement> ToolAsync(string name, string argumentsJson, bool expectError, CancellationToken token)
            {
                JsonElement result = await RequestAsync("tools/call", "{\"name\":\"" + name + "\",\"arguments\":" + argumentsJson + "}", token).ConfigureAwait(false);
                bool isError = result.TryGetProperty("isError", out JsonElement e) && e.GetBoolean();
                string text = result.GetProperty("content")[0].GetProperty("text").GetString() ?? String.Empty;
                if (isError != expectError) throw new InvalidOperationException(name + ": isError " + isError + ": " + text);
                if (expectError) return JsonDocument.Parse(JsonSerializer.Serialize(text)).RootElement.Clone();
                using (JsonDocument doc = JsonDocument.Parse(text))
                {
                    return doc.RootElement.Clone();
                }
            }

            public async Task<string> ToolErrorAsync(string name, string argumentsJson, CancellationToken token)
            {
                JsonElement text = await ToolAsync(name, argumentsJson, true, token).ConfigureAwait(false);
                return text.GetString() ?? String.Empty;
            }

            public void Dispose()
            {
                _Stop.Cancel();
                _Host.Dispose();
                _Rest.Dispose();
                _Http.Dispose();
                _Stop.Dispose();
            }

            private static int FreePort()
            {
                TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();
                return port;
            }
        }
    }
}
