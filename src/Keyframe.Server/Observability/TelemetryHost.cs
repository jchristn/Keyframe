namespace Keyframe.Server.Observability
{
    using System;
    using System.Threading;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Settings;
    using Microsoft.Extensions.Logging;
    using Radiant;
    using SyslogLogging;

    /// <summary>
    /// The single telemetry host for the server process. Subscribes to the Keyframe and Watson meters and activity
    /// sources plus the .NET HTTP client meter, exports over OTLP, serves an in-process Prometheus endpoint, and
    /// optionally forwards log messages into the log pipeline. Best effort: a start failure leaves the server running
    /// without export. Thread safety: safe for concurrent use; dispose once on shutdown.
    /// </summary>
    public class TelemetryHost : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Seconds-scale histogram buckets shared by every duration instrument.
        /// </summary>
        public static readonly double[] DurationBuckets = new double[] { 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30, 60, 120, 300, 600, 1800 };

        /// <summary>
        /// Whether a Radiant host is running.
        /// </summary>
        public bool IsRunning
        {
            get
            {
                return _Host != null;
            }
        }

        /// <summary>
        /// Prometheus scrape URL when enabled and running; otherwise null.
        /// </summary>
        public string? PrometheusUrl
        {
            get
            {
                return _PrometheusUrl;
            }
        }

        /// <summary>
        /// Why the host is not running; null while running.
        /// </summary>
        public string? StatusMessage
        {
            get
            {
                return _StatusMessage;
            }
        }

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private RadiantHost? _Host = null;
        private ILogger? _Logger = null;
        private LoggingModule? _Logging = null;
        private string? _PrometheusUrl = null;
        private string? _StatusMessage = null;
        private int _Disposed = 0;

        #endregion

        #region Constructors-and-Factories

        private TelemetryHost()
        {
        }

        /// <summary>
        /// Start from settings. Never throws: when disabled or when start fails, the host is inert and
        /// <see cref="StatusMessage"/> explains why.
        /// </summary>
        /// <param name="settings">Telemetry settings; null disables export.</param>
        /// <returns>Host.</returns>
        public static TelemetryHost Start(TelemetrySettings? settings)
        {
            TelemetryHost host = new TelemetryHost();
            if (settings == null || !settings.Enable)
            {
                host._StatusMessage = "telemetry export disabled in settings";
                return host;
            }

            try
            {
                RadiantSettings radiant = BuildRadiantSettings(settings);
                host._Host = RadiantHost.Start(radiant);
                if (settings.PrometheusEnable) host._PrometheusUrl = radiant.Prometheus.ToScrapeUrl();
                if (settings.ExportLogs) host._Logger = host._Host.CreateLogger("Keyframe.Server");
            }
            catch (Exception ex)
            {
                host._Host = null;
                host._StatusMessage = "telemetry export disabled (start failed): " + Describe(ex);
            }

            return host;
        }

        /// <summary>
        /// Build the Radiant settings. Exposed for tests.
        /// </summary>
        /// <param name="settings">Telemetry settings.</param>
        /// <returns>Radiant settings.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static RadiantSettings BuildRadiantSettings(TelemetrySettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            RadiantSettings radiant = new RadiantSettings(settings.ServiceName) { Enable = true };
            radiant.Sources.AddMeter(TelemetryNames.SourceName);
            radiant.Sources.AddActivitySource(TelemetryNames.SourceName);
            radiant.Sources.AddMeter(TelemetryNames.WatsonSourceName);
            radiant.Sources.AddActivitySource(TelemetryNames.WatsonSourceName);
            radiant.Sources.AddMeter(TelemetryNames.HttpClientMeterName);

            radiant.Metrics.IncludeRuntime = true;
            radiant.Metrics.ExportIntervalMs = settings.MetricExportIntervalMs;
            radiant.Metrics.Define(Convention.Histogram(TelemetryNames.AnalysisDuration, "s", DurationBuckets, "media_kind"));
            radiant.Metrics.Define(Convention.Histogram(TelemetryNames.StageDuration, "s", DurationBuckets, "media_kind", "stage"));
            radiant.Metrics.Define(Convention.Histogram(TelemetryNames.ModelCallDuration, "s", DurationBuckets, "purpose", "provider"));
            radiant.Metrics.Define(Convention.Histogram(TelemetryNames.FfmpegDuration, "s", DurationBuckets, "operation"));

            // Watson records http.server.request.duration in seconds without bucket advice; give it seconds buckets.
            radiant.Metrics.Define(Convention.Histogram("http.server.request.duration", "s", DurationBuckets,
                "http.request.method", "http.response.status_code", "http.route", "url.scheme", "network.protocol.version", "error.type"));

            radiant.Traces.SamplingRatio = settings.TraceSamplingRatio;
            radiant.Logs.Enable = settings.ExportLogs;

            radiant.Otlp.Enable = settings.OtlpEnable;
            radiant.Otlp.Endpoint = settings.OtlpEndpoint;
            radiant.Otlp.Protocol = String.Equals(settings.OtlpProtocol, "httpprotobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpProtocolEnum.HttpProtobuf
                : OtlpProtocolEnum.Grpc;

            radiant.Prometheus.Enable = settings.PrometheusEnable;
            radiant.Prometheus.Hostname = settings.PrometheusHostname;
            radiant.Prometheus.Port = settings.PrometheusPort;
            radiant.Prometheus.Path = settings.PrometheusPath;

            radiant.Loki.Enable = settings.LokiEnable;
            radiant.Loki.Endpoint = settings.LokiEndpoint;
            return radiant;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Forward messages written to a logging module into the telemetry log pipeline. No-op when log export is off.
        /// </summary>
        /// <param name="logging">Logging module; null is ignored.</param>
        public void AttachLogging(LoggingModule? logging)
        {
            if (logging == null || _Logger == null) return;
            lock (_Lock)
            {
                if (_Logging != null) return;
                _Logging = logging;
                _Logging.MessageLogged += OnMessageLogged;
            }
        }

        /// <summary>
        /// Flush, stop exporters, and release the Prometheus port. Never throws.
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _Disposed, 1) == 1) return;

            lock (_Lock)
            {
                if (_Logging != null)
                {
                    try
                    {
                        _Logging.MessageLogged -= OnMessageLogged;
                    }
                    catch (Exception)
                    {
                    }

                    _Logging = null;
                }

                _Logger = null;
            }

            try
            {
                _Host?.Dispose();
            }
            catch (Exception)
            {
            }

            _Host = null;
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        private void OnMessageLogged(LogEntry entry)
        {
            ILogger? logger = _Logger;
            if (logger == null || entry == null) return;
            try
            {
                LogLevel level = ToLogLevel(entry.Severity);
                if (!logger.IsEnabled(level)) return;
                logger.Log(level, default(EventId), entry.Message, entry.Exception, (state, ex) => state);
            }
            catch (Exception)
            {
                // Log export is best effort and must never interrupt logging.
            }
        }

        private static string Describe(Exception ex)
        {
            string message = ex.Message;
            for (Exception? inner = ex.InnerException; inner != null; inner = inner.InnerException)
                message += " -> " + inner.GetType().Name + ": " + inner.Message;
            return message;
        }

        private static LogLevel ToLogLevel(Severity severity)
        {
            switch (severity)
            {
                case Severity.Debug:
                    return LogLevel.Debug;
                case Severity.Info:
                    return LogLevel.Information;
                case Severity.Warn:
                    return LogLevel.Warning;
                case Severity.Error:
                    return LogLevel.Error;
                default:
                    return LogLevel.Critical;
            }
        }

        #endregion
    }
}
