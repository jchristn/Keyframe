namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Helpers;

    /// <summary>
    /// Common analysis response fields.
    /// </summary>
    public class AnalysisResponse
    {
        /// <summary>
        /// Analysis identifier (prefix "kfa_").
        /// </summary>
        public string Id { get; set; } = IdGenerator.GenerateAnalysisId();

        /// <summary>
        /// Whether every enabled function produced a result. Partial field failures keep this true.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Media kind analyzed.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// Deterministic media properties, when requested.
        /// </summary>
        public MediaProperties? Properties { get; set; } = null;

        /// <summary>
        /// Results keyed by the caller's summary and field keys. Values are schemaless by design. Never null.
        /// </summary>
        public Dictionary<string, object?> Results { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// Per-field detail. Never null.
        /// </summary>
        public List<FieldResult> Fields { get; set; } = new List<FieldResult>();

        /// <summary>
        /// Telemetry.
        /// </summary>
        public AnalysisTelemetry Telemetry { get; set; } = new AnalysisTelemetry();

        /// <summary>
        /// Non-fatal warnings. Never null.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Errors that prevented a function from completing. Never null.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }
}
