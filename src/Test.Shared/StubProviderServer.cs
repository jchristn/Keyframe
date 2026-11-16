namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// A minimal OpenAI-compatible server on 127.0.0.1 for provider adapter tests. Answers
    /// <c>/chat/completions</c> (as server-sent events when the request sets "stream": true, otherwise as one JSON
    /// body) and <c>/audio/transcriptions</c>, and records every request.
    /// </summary>
    public sealed class StubProviderServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Base URL including <c>/v1</c>.
        /// </summary>
        public string BaseUrl { get; }

        /// <summary>
        /// Status code returned for every request. Default 200.
        /// </summary>
        public int StatusCode { get; set; } = 200;

        /// <summary>
        /// Chat reply text chosen from the request body. Default returns "Stub reply.".
        /// </summary>
        public Func<string, string> ChatReply { get; set; } = body => "Stub reply.";

        /// <summary>
        /// Delay between streamed chat chunks in milliseconds. Default 15.
        /// </summary>
        public int StreamChunkDelayMs { get; set; } = 15;

        /// <summary>
        /// Recorded requests, in arrival order.
        /// </summary>
        public List<StubRequest> Requests
        {
            get
            {
                return _Requests.ToList();
            }
        }

        #endregion

        #region Private-Members

        private readonly HttpListener _Listener = new HttpListener();
        private readonly ConcurrentQueue<StubRequest> _Requests = new ConcurrentQueue<StubRequest>();
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private readonly Task _Loop;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start on a free port.
        /// </summary>
        public StubProviderServer()
        {
            int port;
            TcpListener probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _Listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            _Listener.Start();
            BaseUrl = "http://127.0.0.1:" + port + "/v1";
            _Loop = Task.Run(() => LoopAsync(_Stopping.Token));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Stop the server.
        /// </summary>
        public void Dispose()
        {
            _Stopping.Cancel();
            try
            {
                _Listener.Stop();
                _Listener.Close();
            }
            catch (Exception)
            {
            }

            _Stopping.Dispose();
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _Listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                try
                {
                    await HandleAsync(ctx).ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            string body;
            using (StreamReader reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            string path = ctx.Request.Url?.AbsolutePath ?? "/";
            _Requests.Enqueue(new StubRequest { Path = path, ContentType = ctx.Request.ContentType, Body = body });

            string reply;
            if (StatusCode != 200)
            {
                reply = "{\"error\":{\"message\":\"stub status " + StatusCode + "\",\"type\":\"stub\"}}";
            }
            else if (path.EndsWith("/audio/transcriptions", StringComparison.Ordinal))
            {
                // The third segment imitates Whisper inventing "You" over silence; the client must drop it.
                reply = "{\"text\":\"hello world You\",\"language\":\"english\",\"duration\":3.0,\"segments\":["
                    + "{\"id\":0,\"start\":0.0,\"end\":1.25,\"text\":\" hello\",\"no_speech_prob\":0.01,\"avg_logprob\":-0.2},"
                    + "{\"id\":1,\"start\":1.25,\"end\":2.0,\"text\":\" world\",\"no_speech_prob\":0.02,\"avg_logprob\":-0.3},"
                    + "{\"id\":2,\"start\":2.0,\"end\":3.0,\"text\":\" You\",\"no_speech_prob\":0.82,\"avg_logprob\":-0.95}]}";
            }
            else if (path.EndsWith("/chat/completions", StringComparison.Ordinal) && IsStreaming(body))
            {
                await StreamChatAsync(ctx, ChatReply(body)).ConfigureAwait(false);
                return;
            }
            else if (path.EndsWith("/chat/completions", StringComparison.Ordinal))
            {
                string text = System.Text.Json.JsonSerializer.Serialize(ChatReply(body));
                reply = "{\"id\":\"chatcmpl-stub\",\"object\":\"chat.completion\",\"model\":\"stub-model\",\"choices\":[{\"index\":0,"
                    + "\"message\":{\"role\":\"assistant\",\"content\":" + text + "},\"finish_reason\":\"stop\"}],"
                    + "\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":3,\"total_tokens\":14}}";
            }
            else
            {
                ctx.Response.StatusCode = 404;
                reply = "{\"error\":{\"message\":\"not found\"}}";
            }

            if (ctx.Response.StatusCode != 404) ctx.Response.StatusCode = StatusCode;
            byte[] bytes = Encoding.UTF8.GetBytes(reply);
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            ctx.Response.Close();
        }

        private static bool IsStreaming(string body)
        {
            try
            {
                using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body))
                {
                    return doc.RootElement.TryGetProperty("stream", out System.Text.Json.JsonElement stream)
                        && stream.ValueKind == System.Text.Json.JsonValueKind.True;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task StreamChatAsync(HttpListenerContext ctx, string reply)
        {
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.SendChunked = true;

            List<string> pieces = Split(reply, 3);
            List<string> events = new List<string>
            {
                "{\"id\":\"chatcmpl-stub\",\"object\":\"chat.completion.chunk\",\"model\":\"stub-model\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"content\":\"\"},\"finish_reason\":null}]}"
            };

            foreach (string piece in pieces)
            {
                events.Add("{\"id\":\"chatcmpl-stub\",\"object\":\"chat.completion.chunk\",\"model\":\"stub-model\",\"choices\":[{\"index\":0,\"delta\":{\"content\":"
                    + System.Text.Json.JsonSerializer.Serialize(piece) + "},\"finish_reason\":null}]}");
            }

            events.Add("{\"id\":\"chatcmpl-stub\",\"object\":\"chat.completion.chunk\",\"model\":\"stub-model\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"stop\"}],"
                + "\"usage\":{\"prompt_tokens\":11,\"completion_tokens\":3,\"total_tokens\":14}}");

            Stream output = ctx.Response.OutputStream;
            for (int i = 0; i < events.Count; i++)
            {
                if (i > 0 && StreamChunkDelayMs > 0) await Task.Delay(StreamChunkDelayMs).ConfigureAwait(false);
                byte[] bytes = Encoding.UTF8.GetBytes("data: " + events[i] + "\n\n");
                await output.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await output.FlushAsync().ConfigureAwait(false);
            }

            byte[] done = Encoding.UTF8.GetBytes("data: [DONE]\n\n");
            await output.WriteAsync(done, 0, done.Length).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
            ctx.Response.Close();
        }

        private static List<string> Split(string text, int parts)
        {
            List<string> pieces = new List<string>();
            if (text.Length <= parts)
            {
                pieces.Add(text);
                return pieces;
            }

            int size = (int)Math.Ceiling(text.Length / (double)parts);
            for (int i = 0; i < text.Length; i += size) pieces.Add(text.Substring(i, Math.Min(size, text.Length - i)));
            return pieces;
        }

        #endregion
    }
}