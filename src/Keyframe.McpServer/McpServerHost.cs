namespace Keyframe.McpServer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.McpServer.Settings;
    using Keyframe.McpServer.Tools;
    using Keyframe.Sdk;
    using Radiant;
    using SyslogLogging;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// Hosts the Keyframe MCP tools on HTTP (Streamable HTTP at /mcp, JSON-RPC at /rpc), TCP, and WebSocket.
    /// Thread safety: start and stop from one thread.
    /// </summary>
    public class McpServerHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Server version reported to MCP clients.
        /// </summary>
        public const string Version = "0.1.0";

        #endregion

        #region Private-Members

        private const string Header = "[KeyframeMcp] ";

        private readonly McpSettings _Settings;
        private readonly LoggingModule _Logging;
        private readonly KeyframeClient _Client;
        private readonly KeyframeTools _Tools;
        private readonly List<Task> _Running = new List<Task>();
        private McpHttpServer? _Http = null;
        private McpTcpServer? _Tcp = null;
        private McpWebsocketsServer? _WebSocket = null;
        private RadiantHost? _Telemetry = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public McpServerHost(McpSettings settings, LoggingModule logging)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _Client = new KeyframeClient(settings.Keyframe.Endpoint, settings.Keyframe.AccessKey) { TimeoutMs = settings.Keyframe.TimeoutMs };
            _Tools = new KeyframeTools(_Client, message => _Logging.Debug(message));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start telemetry and every enabled transport.
        /// </summary>
        /// <param name="token">Cancellation token that stops the transports.</param>
        /// <returns>Task.</returns>
        public Task StartAsync(CancellationToken token)
        {
            StartTelemetry();

            if (_Settings.Http.Enable)
            {
                _Http = new McpHttpServer(_Settings.Http.Hostname, _Settings.Http.Port, "/rpc", "/events", false, "/mcp");
                _Http.ServerName = "Keyframe";
                _Http.ServerVersion = Version;
                if (_Settings.RequireAuthentication) _Http.AuthenticationHandler = request => Task.FromResult(Authenticate(request));
                _Tools.RegisterAll((name, description, schema, handler) => _Http.RegisterTool(name, description, schema, handler));
                _Running.Add(_Http.StartAsync(token));
                _Logging.Info(Header + "HTTP      http://" + _Settings.Http.Hostname + ":" + _Settings.Http.Port + "/mcp (Streamable HTTP) and /rpc");
            }

            if (_Settings.Tcp.Enable)
            {
                IPAddress address = _Settings.Tcp.Hostname == "*" ? IPAddress.Any : IPAddress.Parse(_Settings.Tcp.Hostname);
                _Tcp = new McpTcpServer(address, _Settings.Tcp.Port);
                _Tcp.ServerName = "Keyframe";
                _Tcp.ServerVersion = Version;
                _Tools.RegisterAll((name, description, schema, handler) => _Tcp.RegisterTool(name, description, schema, handler));
                _Running.Add(_Tcp.StartAsync(token));
                _Logging.Info(Header + "TCP       tcp://" + _Settings.Tcp.Hostname + ":" + _Settings.Tcp.Port + " (no authentication; keep it on 127.0.0.1)");
            }

            if (_Settings.WebSocket.Enable)
            {
                _WebSocket = new McpWebsocketsServer(_Settings.WebSocket.Hostname, _Settings.WebSocket.Port, "/mcp");
                _WebSocket.ServerName = "Keyframe";
                _WebSocket.ServerVersion = Version;
                if (_Settings.RequireAuthentication) _WebSocket.AuthenticationHandler = request => Task.FromResult(Authenticate(request));
                _Tools.RegisterAll((name, description, schema, handler) => _WebSocket.RegisterTool(name, description, schema, handler));
                _Running.Add(_WebSocket.StartAsync(token));
                _Logging.Info(Header + "WebSocket ws://" + _Settings.WebSocket.Hostname + ":" + _Settings.WebSocket.Port + "/mcp");
            }

            _Logging.Info(Header + KeyframeTools.ToolNames.Length + " tools, Keyframe at " + _Settings.Keyframe.Endpoint
                + (_Settings.RequireAuthentication ? ", bearer authentication required" : ", authentication DISABLED"));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stop every transport.
        /// </summary>
        public void Stop()
        {
            try { _Http?.Stop(); } catch (Exception ex) { _Logging.Debug(Header + "http stop: " + ex.Message); }
            try { _Tcp?.Stop(); } catch (Exception ex) { _Logging.Debug(Header + "tcp stop: " + ex.Message); }
            try { _WebSocket?.Stop(); } catch (Exception ex) { _Logging.Debug(Header + "websocket stop: " + ex.Message); }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                Stop();
                _Http?.Dispose();
                _Tcp?.Dispose();
                _WebSocket?.Dispose();
                _Client.Dispose();
                try { _Telemetry?.Dispose(); } catch (Exception) { }
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private AuthenticationResult Authenticate(HttpListenerRequest request)
        {
            string? header = request.Headers["Authorization"];
            string? presented = null;
            if (!String.IsNullOrWhiteSpace(header) && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) presented = header.Substring(7).Trim();
            if (String.IsNullOrEmpty(presented)) presented = request.Headers["x-api-key"];

            string expected = _Settings.Keyframe.AccessKey ?? String.Empty;
            bool ok = !String.IsNullOrEmpty(presented) && expected.Length > 0
                && CryptographicOperations.FixedTimeEquals(
                    SHA256.HashData(Encoding.UTF8.GetBytes(presented)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

            if (ok) return new AuthenticationResult { IsAuthenticated = true, Principal = "mcp-client" };
            return new AuthenticationResult
            {
                IsAuthenticated = false,
                StatusCode = 401,
                ErrorMessage = "Send the Keyframe access key as 'Authorization: Bearer <key>'."
            };
        }

        private void StartTelemetry()
        {
            McpTelemetrySettings t = _Settings.Telemetry;
            if (!t.Enable) return;
            try
            {
                RadiantSettings radiant = new RadiantSettings(t.ServiceName) { Enable = true };
                radiant.Sources.AddMeter(KeyframeTools.MeterName);
                radiant.Sources.AddMeter("Voltaic");
                radiant.Sources.AddActivitySource("Voltaic");
                radiant.Sources.AddMeter("System.Net.Http");
                radiant.Metrics.IncludeRuntime = true;
                radiant.Otlp.Enable = t.OtlpEnable;
                radiant.Otlp.Endpoint = t.OtlpEndpoint;
                radiant.Otlp.Protocol = String.Equals(t.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase) ? OtlpProtocolEnum.HttpProtobuf : OtlpProtocolEnum.Grpc;
                radiant.Prometheus.Enable = t.PrometheusEnable;
                radiant.Prometheus.Hostname = t.PrometheusHostname;
                radiant.Prometheus.Port = t.PrometheusPort;
                _Telemetry = RadiantHost.Start(radiant);
                if (t.PrometheusEnable) _Logging.Info(Header + "Prometheus at " + radiant.Prometheus.ToScrapeUrl());
            }
            catch (Exception ex)
            {
                _Logging.Warn(Header + "telemetry disabled (start failed): " + ex.Message);
            }
        }

        #endregion
    }
}
