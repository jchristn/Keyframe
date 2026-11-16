namespace Keyframe.McpServer.Tools.Inputs
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Arguments of keyframe_analytics_summary.
    /// </summary>
    public class AnalyticsSummaryInput
    {
        /// <summary>
        /// Inclusive lower bound on completion time. Default 24 hours before ToUtc.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive upper bound on completion time. Default now.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Bucket width: Minute, Hour, or Day. Default chosen from the range.
        /// </summary>
        public AnalyticsBucketEnum? Bucket { get; set; } = null;

        /// <summary>
        /// Media kind filter.
        /// </summary>
        public MediaKindEnum? MediaKind { get; set; } = null;

        /// <summary>
        /// Endpoint name filter.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Model name filter.
        /// </summary>
        public string? Model { get; set; } = null;
    }
}
