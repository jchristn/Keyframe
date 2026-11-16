namespace Keyframe.McpServer.Settings
{
    using System;

    /// <summary>
    /// Settings for the Keyframe MCP server (keyframe-mcp.json).
    /// </summary>
    public class McpSettings
    {
        #region Public-Members

        /// <summary>
        /// Keyframe REST connection.
        /// </summary>
        public KeyframeConnectionSettings Keyframe
        {
            get
            {
                return _Keyframe;
            }
            set
            {
                _Keyframe = value ?? new KeyframeConnectionSettings();
            }
        }

        /// <summary>
        /// Whether MCP HTTP and WebSocket clients must present the access key as a bearer token. Default true.
        /// The TCP transport has no authentication and binds to 127.0.0.1 by default.
        /// </summary>
        public bool RequireAuthentication { get; set; } = true;

        /// <summary>
        /// HTTP transport (Streamable HTTP at /mcp, JSON-RPC at /rpc). Default port 8702.
        /// </summary>
        public TransportSettings Http
        {
            get
            {
                return _Http;
            }
            set
            {
                _Http = value ?? new TransportSettings { Port = 8702 };
            }
        }

        /// <summary>
        /// TCP transport (newline-delimited JSON-RPC). Default port 8703.
        /// </summary>
        public TransportSettings Tcp
        {
            get
            {
                return _Tcp;
            }
            set
            {
                _Tcp = value ?? new TransportSettings { Port = 8703 };
            }
        }

        /// <summary>
        /// WebSocket transport at /mcp. Default port 8704.
        /// </summary>
        public TransportSettings WebSocket
        {
            get
            {
                return _WebSocket;
            }
            set
            {
                _WebSocket = value ?? new TransportSettings { Port = 8704 };
            }
        }

        /// <summary>
        /// Logging.
        /// </summary>
        public McpLoggingSettings Logging
        {
            get
            {
                return _Logging;
            }
            set
            {
                _Logging = value ?? new McpLoggingSettings();
            }
        }

        /// <summary>
        /// Telemetry.
        /// </summary>
        public McpTelemetrySettings Telemetry
        {
            get
            {
                return _Telemetry;
            }
            set
            {
                _Telemetry = value ?? new McpTelemetrySettings();
            }
        }

        #endregion

        #region Private-Members

        private KeyframeConnectionSettings _Keyframe = new KeyframeConnectionSettings();
        private TransportSettings _Http = new TransportSettings { Port = 8702 };
        private TransportSettings _Tcp = new TransportSettings { Port = 8703 };
        private TransportSettings _WebSocket = new TransportSettings { Port = 8704 };
        private McpLoggingSettings _Logging = new McpLoggingSettings();
        private McpTelemetrySettings _Telemetry = new McpTelemetrySettings();

        #endregion
    }
}
