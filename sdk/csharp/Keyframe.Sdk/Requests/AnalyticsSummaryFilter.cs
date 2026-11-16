namespace Keyframe.Sdk.Requests
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Range, bucket width, and filters for an analytics summary.
    /// </summary>
    public class AnalyticsSummaryFilter
    {
        /// <summary>
        /// Inclusive lower bound on CompletedUtc. Null means 24 hours before ToUtc.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive upper bound on CompletedUtc. Null means now.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Bucket width. Null picks one from the range: minute up to 2 hours, hour up to 7 days, otherwise day.
        /// </summary>
        public AnalyticsBucketEnum? Bucket { get; set; } = null;

        /// <summary>
        /// Media kind to match.
        /// </summary>
        public MediaKindEnum? MediaKind { get; set; } = null;

        /// <summary>
        /// Endpoint to match. Analyses match when any of their vision, text, or transcription endpoints match; model
        /// call statistics include only calls to this endpoint.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Model to match. Analyses match when any of their models match; model call statistics include only calls to this model.
        /// </summary>
        public string? Model { get; set; } = null;
    }
}
