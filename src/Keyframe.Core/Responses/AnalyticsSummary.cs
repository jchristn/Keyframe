namespace Keyframe.Core.Responses
{
    using System;
    using System.Collections.Generic;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Analytics over a time range: totals, per media kind, gap-filled time buckets, and breakdowns by model, stage,
    /// and call purpose. Computed in memory over the analyses that completed in the range and match the filter.
    /// </summary>
    public class AnalyticsSummary
    {
        /// <summary>
        /// UTC start of the range (aligned down to the bucket width).
        /// </summary>
        public DateTime FromUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC end of the range (exclusive).
        /// </summary>
        public DateTime ToUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Bucket width: minute, hour, or day.
        /// </summary>
        public string Bucket { get; set; } = "hour";

        /// <summary>
        /// Media kind filter, or null for all.
        /// </summary>
        public MediaKindEnum? MediaKind { get; set; } = null;

        /// <summary>
        /// Endpoint filter, or null.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Model filter, or null.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Totals over the range.
        /// </summary>
        public StatBlock Totals { get; set; } = new StatBlock();

        /// <summary>
        /// Statistics keyed by media kind: Image, Video, and Audio are always present.
        /// </summary>
        public Dictionary<string, StatBlock> ByMediaKind { get; set; } = new Dictionary<string, StatBlock>();

        /// <summary>
        /// Every bucket in the range, in order, including empty ones.
        /// </summary>
        public List<AnalyticsBucket> Buckets { get; set; } = new List<AnalyticsBucket>();

        /// <summary>
        /// Model call attempt statistics per endpoint and model, most calls first.
        /// </summary>
        public List<ModelStats> ByModel { get; set; } = new List<ModelStats>();

        /// <summary>
        /// Stage duration statistics, in pipeline order.
        /// </summary>
        public List<StageStats> ByStage { get; set; } = new List<StageStats>();

        /// <summary>
        /// Model call attempt statistics per purpose.
        /// </summary>
        public List<PurposeStats> ByPurpose { get; set; } = new List<PurposeStats>();
    }
}
