namespace Keyframe.McpServer.Tools.Inputs
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Arguments of keyframe_analytics_list.
    /// </summary>
    public class AnalyticsListInput
    {
        /// <summary>
        /// Media kind filter.
        /// </summary>
        public MediaKindEnum? MediaKind { get; set; } = null;

        /// <summary>
        /// Success filter.
        /// </summary>
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Inclusive lower bound on completion time.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive upper bound on completion time.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Page size, 1 to 1000. Default 50.
        /// </summary>
        public int? MaxResults { get; set; } = null;

        /// <summary>
        /// Records to skip. Default 0.
        /// </summary>
        public int? Skip { get; set; } = null;
    }
}
