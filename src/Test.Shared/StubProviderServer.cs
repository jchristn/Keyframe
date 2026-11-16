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
    /// <c>/chat/completions</c> and <c>/audio/transcriptions</c> and records every request.
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
                reply = "{\"text\":\"hello world\",\"language\":\"english\",\"duration\":2.0,\"segments\":["
                    + "{\"id\":0,\"start\":0.0,\"end\":1.25,\"text\":\" hello\"},"
                    + "{\"id\":1,\"start\":1.25,\"end\":2.0,\"text\":\" world\"}]}";
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

        #endregion
    }
}
