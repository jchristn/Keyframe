namespace Keyframe.McpServer.Settings
{
    using System;

    /// <summary>
    /// MCP server telemetry export.
    /// </summary>
    public class McpTelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Master switch. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Service name. Default "keyframe-mcp".
        /// </summary>
        public string ServiceName { get; set; } = "keyframe-mcp";

        /// <summary>
        /// Export over OTLP. Default false.
        /// </summary>
        public bool OtlpEnable { get; set; } = false;

        /// <summary>
        /// OTLP endpoint. Default "http://127.0.0.1:4717".
        /// </summary>
        public string OtlpEndpoint { get; set; } = "http://127.0.0.1:4717";

        /// <summary>
        /// "grpc" or "httpprotobuf". Default "grpc".
        /// </summary>
        public string OtlpProtocol { get; set; } = "grpc";

        /// <summary>
        /// Serve a Prometheus scrape endpoint. Default true.
        /// </summary>
        public bool PrometheusEnable { get; set; } = true;

        /// <summary>
        /// Prometheus hostname. Default "127.0.0.1".
        /// </summary>
        public string PrometheusHostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// Prometheus port. Default 9465. Minimum 1, maximum 65535.
        /// </summary>
        public int PrometheusPort
        {
            get
            {
                return _PrometheusPort;
            }
            set
            {
                _PrometheusPort = Math.Clamp(value, 1, 65535);
            }
        }

        #endregion

        #region Private-Members

        private int _PrometheusPort = 9465;

        #endregion
    }
}
