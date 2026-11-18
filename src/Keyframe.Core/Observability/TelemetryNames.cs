namespace Keyframe.Core.Observability
{
    /// <summary>
    /// Telemetry source and instrument names. These names are a public contract consumed by Grafana dashboards.
    /// </summary>
    public static class TelemetryNames
    {
        /// <summary>
        /// Application meter and activity source name.
        /// </summary>
        public const string SourceName = "Keyframe";

        /// <summary>
        /// Watson meter and activity source name.
        /// </summary>
        public const string WatsonSourceName = "Watson";

        /// <summary>
        /// .NET HTTP client meter name.
        /// </summary>
        public const string HttpClientMeterName = "System.Net.Http";

        /// <summary>
        /// Analyses completed, by media kind and outcome.
        /// </summary>
        public const string AnalysisTotal = "keyframe.analysis.total";

        /// <summary>
        /// Analysis duration in seconds, by media kind.
        /// </summary>
        public const string AnalysisDuration = "keyframe.analysis.duration";

        /// <summary>
        /// Stage duration in seconds, by media kind and stage.
        /// </summary>
        public const string StageDuration = "keyframe.stage.duration";

        /// <summary>
        /// Model calls, by purpose, provider, and outcome.
        /// </summary>
        public const string ModelCallsTotal = "keyframe.model.calls";

        /// <summary>
        /// Model call duration in seconds, by purpose and provider.
        /// </summary>
        public const string ModelCallDuration = "keyframe.model.call.duration";

        /// <summary>
        /// Model tokens, by provider and kind (prompt or completion).
        /// </summary>
        public const string ModelTokens = "keyframe.model.tokens";

        /// <summary>
        /// ffmpeg and ffprobe invocations, by operation and outcome.
        /// </summary>
        public const string FfmpegInvocations = "keyframe.ffmpeg.invocations";

        /// <summary>
        /// ffmpeg and ffprobe duration in seconds, by operation.
        /// </summary>
        public const string FfmpegDuration = "keyframe.ffmpeg.duration";

        /// <summary>
        /// Jobs finished, by outcome.
        /// </summary>
        public const string JobsTotal = "keyframe.jobs.total";

        /// <summary>
        /// Jobs currently running.
        /// </summary>
        public const string JobsActive = "keyframe.jobs.active";

        /// <summary>
        /// Jobs currently queued.
        /// </summary>
        public const string JobsQueued = "keyframe.jobs.queued";

        /// <summary>
        /// Authentication outcomes, by result.
        /// </summary>
        public const string AuthRequests = "keyframe.auth.requests";

        /// <summary>
        /// Endpoint health, 1 healthy or 0 unhealthy, by endpoint name.
        /// </summary>
        public const string EndpointHealthy = "keyframe.endpoint.healthy";

        /// <summary>
        /// Time a model call attempt waited on its endpoint's concurrency gate, by purpose and provider.
        /// </summary>
        public const string ModelQueueDuration = "keyframe.model.queue.duration";

        /// <summary>
        /// Time to first token of a model call attempt, by purpose and provider.
        /// </summary>
        public const string ModelTimeToFirstToken = "keyframe.model.ttft";

        /// <summary>
        /// Generation time (first to last token) of a model call attempt, by purpose and provider.
        /// </summary>
        public const string ModelGenerationDuration = "keyframe.model.generation.duration";

        /// <summary>
        /// Completion tokens per second of generation time, by purpose and provider.
        /// </summary>
        public const string ModelTokensPerSecond = "keyframe.model.tokens_per_second";

        /// <summary>
        /// Time a job waited in the queue before a worker started it, by media kind.
        /// </summary>
        public const string JobQueueDuration = "keyframe.jobs.queue.duration";

        /// <summary>
        /// Analysis wall-clock time divided by media duration (video and audio), by media kind.
        /// </summary>
        public const string AnalysisRealtimeFactor = "keyframe.analysis.realtime_factor";

        /// <summary>
        /// Analytics rows written, by outcome.
        /// </summary>
        public const string AnalyticsWrites = "keyframe.analytics.writes";

        /// <summary>
        /// Bytes of audio attached to completion calls, by provider and purpose.
        /// </summary>
        public const string ModelInputAudioBytes = "keyframe.model.input.audio.bytes";

        /// <summary>
        /// Analyses that resolved a listening mode, by media kind, mode, and skip reason.
        /// </summary>
        public const string AnalysisListenTotal = "keyframe.analysis.listen.total";
    }
}
