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
                new TestCaseDescriptor(Id, "AuthAndList", "Bearer key required; thirteen tools listed", async ct =>
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
                        Check.Equal(13, names.Count, "tool count");
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
                }, new[] { "mcp", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "AudioAndSegments", "Audio job and Wait, audio job result, and segments as base64 JSON", async ct =>
                {
                    using (McpHarness h = await McpHarness.StartAsync(ServerFixture.AccessKey, ct).ConfigureAwait(false))
                    {
                        await h.InitializeAsync(ct).ConfigureAwait(false);
                        string audio = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        string video = await TestEnvironment.Base64Async(await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

                        JsonElement waited = await h.ToolAsync("keyframe_audio_analyze",
                            "{\"Audio\":{\"Data\":\"" + audio + "\"},\"Fields\":[{\"Key\":\"speakers\",\"Type\":\"Integer\",\"Prompt\":\"How many speakers?\"}],\"Wait\":true,\"WaitTimeoutMs\":60000}", false, ct).ConfigureAwait(false);
                        Check.Equal("Audio", waited.GetProperty("MediaKind").GetString(), "audio MediaKind");
                        Check.True(waited.GetProperty("Transcript").GetProperty("Segments").GetArrayLength() >= 1, "transcript segments");
                        Check.True(waited.GetProperty("Results").TryGetProperty("Summary", out JsonElement _), "default audio summary");
                        Check.True(waited.GetProperty("Results").TryGetProperty("speakers", out JsonElement _), "audio field");
                        Check.True(waited.GetProperty("Telemetry").TryGetProperty("QueueWaitMs", out JsonElement _), "QueueWaitMs");

                        JsonElement job = await h.ToolAsync("keyframe_audio_analyze", "{\"Audio\":{\"Data\":\"" + audio + "\"}}", false, ct).ConfigureAwait(false);
                        Check.Equal("Audio", job.GetProperty("MediaKind").GetString(), "job MediaKind");
                        string jobId = job.GetProperty("Id").GetString() ?? String.Empty;
                        string status = "Queued";
                        for (int i = 0; i < 120 && (status == "Queued" || status == "Running"); i++)
                        {
                            await Task.Delay(250, ct).ConfigureAwait(false);
                            status = (await h.ToolAsync("keyframe_job_get", "{\"JobId\":\"" + jobId + "\"}", false, ct).ConfigureAwait(false)).GetProperty("Status").GetString() ?? String.Empty;
                        }

                        Check.Equal("Succeeded", status, "audio job succeeded");
                        JsonElement result = await h.ToolAsync("keyframe_job_result", "{\"JobId\":\"" + jobId + "\"}", false, ct).ConfigureAwait(false);
                        Check.Equal("Audio", result.GetProperty("MediaKind").GetString(), "result MediaKind");
                        Check.True(!result.TryGetProperty("Scenes", out JsonElement _), "audio result has no Scenes");

                        JsonElement clip = await h.ToolAsync("keyframe_segment", "{\"Source\":{\"Data\":\"" + audio + "\"},\"StartMs\":1000,\"EndMs\":3000,\"Output\":\"Audio\",\"Format\":\"wav\"}", false, ct).ConfigureAwait(false);
                        Check.Equal("segment-1000-3000.wav", clip.GetProperty("Filename").GetString(), "clip filename");
                        Check.Equal(2000L, clip.GetProperty("DurationMs").GetInt64(), "clip duration");
                        byte[] bytes = Convert.FromBase64String(clip.GetProperty("Data").GetString() ?? String.Empty);
                        Check.Equal(clip.GetProperty("SizeBytes").GetInt64(), (long)bytes.Length, "clip bytes");

                        JsonElement frame = await h.ToolAsync("keyframe_segment", "{\"Source\":{\"Data\":\"" + video + "\"},\"StartMs\":1000,\"Output\":\"Frame\",\"MaxEdge\":64}", false, ct).ConfigureAwait(false);
                        Check.Equal("image/jpeg", frame.GetProperty("MediaType").GetString(), "frame media type");

                        string backwards = await h.ToolErrorAsync("keyframe_segment", "{\"Source\":{\"Data\":\"" + audio + "\"},\"StartMs\":3000,\"EndMs\":1000,\"Output\":\"Audio\"}", ct).ConfigureAwait(false);
                        Check.True(backwards.Contains("400"), "backwards range is a tool error: " + backwards);
                        string noStart = await h.ToolErrorAsync("keyframe_segment", "{\"Source\":{\"Data\":\"" + audio + "\"}}", ct).ConfigureAwait(false);
                        Check.True(noStart.Contains("StartMs"), "missing StartMs is a tool error: " + noStart);
                    }
                }, new[] { "mcp", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Analytics", "The caller's key passes through: admin key gets analytics, access key gets 403, unknown key gets 401", async ct =>
                {
                    using (McpHarness h = await McpHarness.StartAsync(ServerFixture.AdminKey, ct).ConfigureAwait(false))
                    {
                        await h.InitializeAsync(ct).ConfigureAwait(false);
                        string image = await TestEnvironment.Base64Async(await TestEnvironment.JpegAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        await h.ToolAsync("keyframe_image_analyze", "{\"Image\":{\"Data\":\"" + image + "\"}}", false, ct).ConfigureAwait(false);

                        JsonElement summary = default(JsonElement);
                        for (int i = 0; i < 40; i++)
                        {
                            summary = await h.ToolAsync("keyframe_analytics_summary", "{\"Bucket\":\"Hour\"}", false, ct).ConfigureAwait(false);
                            if (summary.GetProperty("Totals").GetProperty("Count").GetInt64() > 0) break;
                            await Task.Delay(100, ct).ConfigureAwait(false);
                        }

                        Check.True(summary.GetProperty("Totals").GetProperty("Count").GetInt64() > 0, "summary counts the image analysis");
                        Check.Equal("hour", summary.GetProperty("Bucket").GetString(), "bucket");
                        Check.True(summary.GetProperty("ByMediaKind").TryGetProperty("Audio", out JsonElement _), "ByMediaKind.Audio");

                        JsonElement list = await h.ToolAsync("keyframe_analytics_list", "{\"MediaKind\":\"Image\",\"MaxResults\":5}", false, ct).ConfigureAwait(false);
                        Check.True(list.GetProperty("Items").GetArrayLength() >= 1, "listed analyses");
                        Check.True(list.GetProperty("Items").EnumerateArray().All(r => r.GetProperty("MediaKind").GetString() == "Image"), "media kind filter");
                    }

                    using (McpHarness h = await McpHarness.StartAsync(ServerFixture.AccessKey, ct).ConfigureAwait(false))
                    {
                        await h.InitializeAsync(ct).ConfigureAwait(false);
                        string denied = await h.ToolErrorAsync("keyframe_analytics_summary", "{}", ct).ConfigureAwait(false);
                        Check.True(denied.Contains("403"), "access key is refused: " + denied);
                        string deniedList = await h.ToolErrorAsync("keyframe_analytics_list", "{}", ct).ConfigureAwait(false);
                        Check.True(deniedList.Contains("403"), "access key is refused for list: " + deniedList);
                    }

                    using (McpHarness h = await McpHarness.StartAsync(ServerFixture.AccessKey, ct).ConfigureAwait(false))
                    {
                        using (HttpResponseMessage unknown = await h.PostRawAsync("not-a-keyframe-key", "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode, "unknown key is refused at the transport");
                        }

                        using (HttpResponseMessage admin = await h.PostRawAsync(ServerFixture.AdminKey, "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", ct).ConfigureAwait(false))
                        {
                            Check.True(admin.StatusCode != HttpStatusCode.Unauthorized, "admin key is accepted at the transport");
                        }
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
            private readonly string _Key;
            private string? _Session = null;
            private int _NextId = 1;

            private McpHarness(ServerFixture rest, McpServerHost host, string url, string key)
            {
                _Rest = rest;
                _Host = host;
                _Url = url;
                _Key = key;
            }

            public static Task<McpHarness> StartAsync(CancellationToken token)
            {
                return StartAsync(ServerFixture.AccessKey, token);
            }

            public static async Task<McpHarness> StartAsync(string keyframeKey, CancellationToken token)
            {
                ServerFixture rest = await ServerFixture.StartAsync(null, token).ConfigureAwait(false);
                McpSettings settings = new McpSettings();
                settings.Keyframe.Endpoint = rest.Server.BaseUrl;
                // The server's own key is always the plain access key; the client's bearer key is what reaches Keyframe.
                settings.Keyframe.AccessKey = ServerFixture.AccessKey;
                settings.Http.Port = FreePort();
                settings.Tcp.Enable = false;
                settings.WebSocket.Enable = false;
                settings.Telemetry.Enable = false;
                LoggingModule logging = new LoggingModule();
                logging.Settings.EnableConsole = false;
                McpServerHost host = new McpServerHost(settings, logging);
                McpHarness harness = new McpHarness(rest, host, "http://127.0.0.1:" + settings.Http.Port + "/mcp", keyframeKey);
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
                using (HttpResponseMessage r = await PostRawAsync(_Key, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", token).ConfigureAwait(false))
                {
                }
            }

            public async Task<JsonElement> RequestAsync(string method, string? paramsJson, CancellationToken token)
            {
                int id = _NextId++;
                string body = "{\"jsonrpc\":\"2.0\",\"id\":" + id + ",\"method\":\"" + method + "\"" + (paramsJson != null ? ",\"params\":" + paramsJson : String.Empty) + "}";
                using (HttpResponseMessage response = await PostRawAsync(_Key, body, token).ConfigureAwait(false))
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
