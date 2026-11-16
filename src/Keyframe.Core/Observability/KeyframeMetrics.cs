namespace Keyframe.Core.Observability
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;

    /// <summary>
    /// Keyframe application metrics. Every recorder accepts only bounded label values and is best-effort: a telemetry
    /// failure never reaches the caller. With no listener attached, recording is a cheap no-op.
    /// Thread safety: all members are safe for concurrent use.
    /// </summary>
    public static class KeyframeMetrics
    {
        #region Public-Members

        /// <summary>
        /// The Keyframe meter.
        /// </summary>
        public static Meter Meter
        {
            get
            {
                return _Meter;
            }
        }

        /// <summary>
        /// Provider of the running job count for the jobs-active gauge. Null reports nothing.
        /// </summary>
        public static Func<int>? ActiveJobsProvider { get; set; } = null;

        /// <summary>
        /// Provider of the queued job count for the jobs-queued gauge. Null reports nothing.
        /// </summary>
        public static Func<int>? QueuedJobsProvider { get; set; } = null;

        /// <summary>
        /// Provider of endpoint health by endpoint name for the endpoint-healthy gauge. Null reports nothing.
        /// </summary>
        public static Func<IReadOnlyDictionary<string, bool>>? EndpointHealthProvider { get; set; } = null;

        #endregion

        #region Private-Members

        private static readonly Meter _Meter = new Meter(TelemetryNames.SourceName);
        private static readonly Counter<long> _AnalysisTotal = _Meter.CreateCounter<long>(TelemetryNames.AnalysisTotal, "{analysis}", "Analyses completed.");
        private static readonly Histogram<double> _AnalysisDuration = _Meter.CreateHistogram<double>(TelemetryNames.AnalysisDuration, "s", "Analysis duration.");
        private static readonly Histogram<double> _StageDuration = _Meter.CreateHistogram<double>(TelemetryNames.StageDuration, "s", "Pipeline stage duration.");
        private static readonly Counter<long> _ModelCalls = _Meter.CreateCounter<long>(TelemetryNames.ModelCallsTotal, "{call}", "Model calls.");
        private static readonly Histogram<double> _ModelCallDuration = _Meter.CreateHistogram<double>(TelemetryNames.ModelCallDuration, "s", "Model call duration.");
        private static readonly Counter<long> _ModelTokens = _Meter.CreateCounter<long>(TelemetryNames.ModelTokens, "{token}", "Model tokens.");
        private static readonly Counter<long> _FfmpegInvocations = _Meter.CreateCounter<long>(TelemetryNames.FfmpegInvocations, "{invocation}", "ffmpeg and ffprobe invocations.");
        private static readonly Histogram<double> _FfmpegDuration = _Meter.CreateHistogram<double>(TelemetryNames.FfmpegDuration, "s", "ffmpeg and ffprobe duration.");
        private static readonly Counter<long> _JobsTotal = _Meter.CreateCounter<long>(TelemetryNames.JobsTotal, "{job}", "Jobs finished.");
        private static readonly Counter<long> _AuthRequests = _Meter.CreateCounter<long>(TelemetryNames.AuthRequests, "{request}", "Authentication outcomes.");
        private static readonly Histogram<double> _ModelQueueDuration = _Meter.CreateHistogram<double>(TelemetryNames.ModelQueueDuration, "s", "Time a model call waited on its endpoint concurrency gate.");
        private static readonly Histogram<double> _ModelTimeToFirstToken = _Meter.CreateHistogram<double>(TelemetryNames.ModelTimeToFirstToken, "s", "Model time to first token.");
        private static readonly Histogram<double> _ModelGenerationDuration = _Meter.CreateHistogram<double>(TelemetryNames.ModelGenerationDuration, "s", "Model generation time, first to last token.");
        private static readonly Histogram<double> _ModelTokensPerSecond = _Meter.CreateHistogram<double>(TelemetryNames.ModelTokensPerSecond, "{token}/s", "Completion tokens per second of generation time.");
        private static readonly Histogram<double> _JobQueueDuration = _Meter.CreateHistogram<double>(TelemetryNames.JobQueueDuration, "s", "Time a job waited in the queue.");
        private static readonly Histogram<double> _AnalysisRealtimeFactor = _Meter.CreateHistogram<double>(TelemetryNames.AnalysisRealtimeFactor, "1", "Analysis time divided by media duration.");
        private static readonly Counter<long> _AnalyticsWrites = _Meter.CreateCounter<long>(TelemetryNames.AnalyticsWrites, "{analysis}", "Analytics rows written.");

        #endregion

        #region Constructors-and-Factories

        static KeyframeMetrics()
        {
            try
            {
                _Meter.CreateObservableGauge<int>(TelemetryNames.JobsActive, ObserveActiveJobs, "{job}", "Jobs running.");
                _Meter.CreateObservableGauge<int>(TelemetryNames.JobsQueued, ObserveQueuedJobs, "{job}", "Jobs queued.");
                _Meter.CreateObservableGauge<int>(TelemetryNames.EndpointHealthy, ObserveEndpointHealth, "1", "Endpoint health (1 healthy, 0 unhealthy).");
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Record a completed analysis.
        /// </summary>
        /// <param name="mediaKind">Media kind label (image or video).</param>
        /// <param name="outcome">Outcome label (success, partial, failure).</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordAnalysis(string mediaKind, string outcome, double seconds)
        {
            try
            {
                _AnalysisTotal.Add(1, new KeyValuePair<string, object?>("media_kind", mediaKind), new KeyValuePair<string, object?>("outcome", outcome));
                _AnalysisDuration.Record(seconds, new KeyValuePair<string, object?>("media_kind", mediaKind));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a pipeline stage duration.
        /// </summary>
        /// <param name="mediaKind">Media kind label.</param>
        /// <param name="stage">Stage label.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordStage(string mediaKind, string stage, double seconds)
        {
            try
            {
                _StageDuration.Record(seconds, new KeyValuePair<string, object?>("media_kind", mediaKind), new KeyValuePair<string, object?>("stage", stage));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record the timing of one model call attempt. Null values are skipped.
        /// </summary>
        /// <param name="purpose">Purpose label.</param>
        /// <param name="provider">Provider label.</param>
        /// <param name="queueMs">Time waiting on the endpoint gate in milliseconds.</param>
        /// <param name="timeToFirstTokenMs">Time to first token in milliseconds, when known.</param>
        /// <param name="generationMs">Generation time in milliseconds, when known.</param>
        /// <param name="tokensPerSecond">Tokens per second, when known.</param>
        public static void RecordModelTiming(string purpose, string provider, double queueMs, double? timeToFirstTokenMs, double? generationMs, double? tokensPerSecond)
        {
            try
            {
                KeyValuePair<string, object?> p = new KeyValuePair<string, object?>("purpose", purpose);
                KeyValuePair<string, object?> v = new KeyValuePair<string, object?>("provider", provider);
                _ModelQueueDuration.Record(Math.Max(0, queueMs) / 1000.0, p, v);
                if (timeToFirstTokenMs.HasValue) _ModelTimeToFirstToken.Record(timeToFirstTokenMs.Value / 1000.0, p, v);
                if (generationMs.HasValue) _ModelGenerationDuration.Record(generationMs.Value / 1000.0, p, v);
                if (tokensPerSecond.HasValue) _ModelTokensPerSecond.Record(tokensPerSecond.Value, p, v);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record how long a job waited in the queue.
        /// </summary>
        /// <param name="mediaKind">Media kind label.</param>
        /// <param name="seconds">Queue wait in seconds.</param>
        public static void RecordJobQueue(string mediaKind, double seconds)
        {
            try
            {
                _JobQueueDuration.Record(Math.Max(0, seconds), new KeyValuePair<string, object?>("media_kind", mediaKind));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record an analysis realtime factor (analysis time divided by media duration).
        /// </summary>
        /// <param name="mediaKind">Media kind label.</param>
        /// <param name="factor">Realtime factor.</param>
        public static void RecordRealtimeFactor(string mediaKind, double factor)
        {
            try
            {
                _AnalysisRealtimeFactor.Record(factor, new KeyValuePair<string, object?>("media_kind", mediaKind));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record an analytics write.
        /// </summary>
        /// <param name="outcome">Outcome label (success or failure).</param>
        public static void RecordAnalyticsWrite(string outcome)
        {
            try
            {
                _AnalyticsWrites.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a model call.
        /// </summary>
        /// <param name="purpose">Purpose label.</param>
        /// <param name="provider">Provider label.</param>
        /// <param name="outcome">Outcome label (success or failure).</param>
        /// <param name="seconds">Duration in seconds.</param>
        /// <param name="promptTokens">Prompt tokens, when known.</param>
        /// <param name="completionTokens">Completion tokens, when known.</param>
        public static void RecordModelCall(string purpose, string provider, string outcome, double seconds, long? promptTokens, long? completionTokens)
        {
            try
            {
                _ModelCalls.Add(1,
                    new KeyValuePair<string, object?>("purpose", purpose),
                    new KeyValuePair<string, object?>("provider", provider),
                    new KeyValuePair<string, object?>("outcome", outcome));
                _ModelCallDuration.Record(seconds,
                    new KeyValuePair<string, object?>("purpose", purpose),
                    new KeyValuePair<string, object?>("provider", provider));
                if (promptTokens.HasValue && promptTokens.Value > 0)
                    _ModelTokens.Add(promptTokens.Value, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("kind", "prompt"));
                if (completionTokens.HasValue && completionTokens.Value > 0)
                    _ModelTokens.Add(completionTokens.Value, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("kind", "completion"));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record an ffmpeg or ffprobe invocation.
        /// </summary>
        /// <param name="operation">Operation label.</param>
        /// <param name="outcome">Outcome label.</param>
        /// <param name="seconds">Duration in seconds.</param>
        public static void RecordFfmpeg(string operation, string outcome, double seconds)
        {
            try
            {
                _FfmpegInvocations.Add(1, new KeyValuePair<string, object?>("operation", operation), new KeyValuePair<string, object?>("outcome", outcome));
                _FfmpegDuration.Record(seconds, new KeyValuePair<string, object?>("operation", operation));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record a finished job.
        /// </summary>
        /// <param name="outcome">Outcome label (the final job status).</param>
        public static void RecordJob(string outcome)
        {
            try
            {
                _JobsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Record an authentication outcome.
        /// </summary>
        /// <param name="result">Result label (success, missing, invalid, forbidden, throttled).</param>
        public static void RecordAuth(string result)
        {
            try
            {
                _AuthRequests.Add(1, new KeyValuePair<string, object?>("result", result));
            }
            catch (Exception)
            {
            }
        }

        #endregion

        #region Private-Methods

        private static IEnumerable<Measurement<int>> ObserveActiveJobs()
        {
            Func<int>? provider = ActiveJobsProvider;
            if (provider == null) return Array.Empty<Measurement<int>>();
            try
            {
                return new[] { new Measurement<int>(provider()) };
            }
            catch (Exception)
            {
                return Array.Empty<Measurement<int>>();
            }
        }

        private static IEnumerable<Measurement<int>> ObserveQueuedJobs()
        {
            Func<int>? provider = QueuedJobsProvider;
            if (provider == null) return Array.Empty<Measurement<int>>();
            try
            {
                return new[] { new Measurement<int>(provider()) };
            }
            catch (Exception)
            {
                return Array.Empty<Measurement<int>>();
            }
        }

        private static IEnumerable<Measurement<int>> ObserveEndpointHealth()
        {
            Func<IReadOnlyDictionary<string, bool>>? provider = EndpointHealthProvider;
            if (provider == null) return Array.Empty<Measurement<int>>();
            try
            {
                List<Measurement<int>> list = new List<Measurement<int>>();
                foreach (KeyValuePair<string, bool> kvp in provider())
                    list.Add(new Measurement<int>(kvp.Value ? 1 : 0, new KeyValuePair<string, object?>("endpoint", kvp.Key)));
                return list;
            }
            catch (Exception)
            {
                return Array.Empty<Measurement<int>>();
            }
        }

        #endregion
    }
}
