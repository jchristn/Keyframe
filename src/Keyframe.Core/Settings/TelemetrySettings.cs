namespace Keyframe.Core.Settings
{
    using System;

    /// <summary>
    /// Telemetry export settings. Changes require a restart.
    /// </summary>
    public class TelemetrySettings
    {
        #region Public-Members

        /// <summary>
        /// Master switch for telemetry export. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Service name reported to the collector. Default "keyframe-server".
        /// </summary>
        public string ServiceName
        {
            get
            {
                return _ServiceName;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(ServiceName));
                _ServiceName = value;
            }
        }

        /// <summary>
        /// Whether to export metrics, traces, and logs over OTLP. Default false so a standalone server does not
        /// retry against a missing collector. The Docker stack enables it.
        /// </summary>
        public bool OtlpEnable { get; set; } = false;

        /// <summary>
        /// OTLP endpoint. Default "http://127.0.0.1:4717".
        /// </summary>
        public string OtlpEndpoint
        {
            get
            {
                return _OtlpEndpoint;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(OtlpEndpoint));
                _OtlpEndpoint = value;
            }
        }

        /// <summary>
        /// OTLP protocol: "grpc" or "httpprotobuf". Default "grpc".
        /// </summary>
        public string OtlpProtocol { get; set; } = "grpc";

        /// <summary>
        /// Whether to serve an in-process Prometheus scrape endpoint. Default true.
        /// </summary>
        public bool PrometheusEnable { get; set; } = true;

        /// <summary>
        /// Prometheus listener hostname. Default "127.0.0.1".
        /// </summary>
        public string PrometheusHostname { get; set; } = "127.0.0.1";

        /// <summary>
        /// Prometheus listener port. Default 9464. Minimum 1, maximum 65535.
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

        /// <summary>
        /// Prometheus scrape path. Default "/metrics".
        /// </summary>
        public string PrometheusPath { get; set; } = "/metrics";

        /// <summary>
        /// Whether to push logs to Loki. Default false.
        /// </summary>
        public bool LokiEnable { get; set; } = false;

        /// <summary>
        /// Loki push endpoint. Default "http://127.0.0.1:3100".
        /// </summary>
        public string LokiEndpoint { get; set; } = "http://127.0.0.1:3100";

        /// <summary>
        /// Whether Keyframe log messages are forwarded into the telemetry log pipeline. Default false.
        /// </summary>
        public bool ExportLogs { get; set; } = false;

        /// <summary>
        /// Metric export interval in milliseconds. Default 15000. Minimum 1000, maximum 300000.
        /// </summary>
        public int MetricExportIntervalMs
        {
            get
            {
                return _MetricExportIntervalMs;
            }
            set
            {
                _MetricExportIntervalMs = Math.Clamp(value, 1000, 300000);
            }
        }

        /// <summary>
        /// Trace sampling ratio. Default 1.0. Minimum 0, maximum 1.
        /// </summary>
        public double TraceSamplingRatio
        {
            get
            {
                return _TraceSamplingRatio;
            }
            set
            {
                _TraceSamplingRatio = Math.Clamp(value, 0.0, 1.0);
            }
        }

        #endregion

        #region Private-Members

        private string _ServiceName = "keyframe-server";
        private string _OtlpEndpoint = "http://127.0.0.1:4717";
        private int _PrometheusPort = 9464;
        private int _MetricExportIntervalMs = 15000;
        private double _TraceSamplingRatio = 1.0;

        #endregion
    }
}
