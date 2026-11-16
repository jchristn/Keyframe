namespace Keyframe.Core
{
    /// <summary>
    /// Fixed product identifiers and identifier prefixes. Configurable values live in settings, not here.
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Product name.
        /// </summary>
        public const string ProductName = "Keyframe";

        /// <summary>
        /// Telemetry meter and activity source name for application-level instrumentation.
        /// </summary>
        public const string TelemetrySourceName = "Keyframe";

        /// <summary>
        /// Identifier prefix for analysis results.
        /// </summary>
        public const string AnalysisIdPrefix = "kfa_";

        /// <summary>
        /// Identifier prefix for jobs.
        /// </summary>
        public const string JobIdPrefix = "kfj_";

        /// <summary>
        /// Identifier prefix for request history entries.
        /// </summary>
        public const string RequestHistoryIdPrefix = "req_";

        /// <summary>
        /// Identifier prefix for request identifiers used in logs and traces.
        /// </summary>
        public const string RequestIdPrefix = "rqi_";

        /// <summary>
        /// Identifier prefix for analytics model call records.
        /// </summary>
        public const string ModelCallIdPrefix = "kfc_";

        /// <summary>
        /// Default key under which a summary is returned in the results dictionary.
        /// </summary>
        public const string DefaultSummaryKey = "Summary";

        /// <summary>
        /// JSON content type.
        /// </summary>
        public const string JsonContentType = "application/json";
    }
}
