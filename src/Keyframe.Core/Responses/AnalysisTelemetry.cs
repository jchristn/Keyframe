namespace Keyframe.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Timing and token telemetry returned with every analysis.
    /// </summary>
    public class AnalysisTelemetry
    {
        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC end time.
        /// </summary>
        public DateTime? EndUtc { get; set; } = null;

        /// <summary>
        /// Wall-clock duration in milliseconds.
        /// </summary>
        public double TotalMs { get; set; } = 0;

        /// <summary>
        /// Time spent waiting in the job queue in milliseconds (video and audio jobs). Null for synchronous image calls.
        /// Kept for compatibility; <see cref="QueueWaitMs"/> carries the same value and is never null.
        /// </summary>
        public double? QueuedMs { get; set; } = null;

        /// <summary>
        /// Time from job queued to job started in milliseconds. 0 for synchronous image calls.
        /// </summary>
        public double QueueWaitMs { get; set; } = 0;

        /// <summary>
        /// Stage timings in order. Never null.
        /// </summary>
        public List<StageTiming> Stages { get; set; } = new List<StageTiming>();

        /// <summary>
        /// Model calls in start order. Never null.
        /// </summary>
        public List<ModelCallTelemetry> ModelCalls { get; set; } = new List<ModelCallTelemetry>();

        /// <summary>
        /// Totals.
        /// </summary>
        public TokenTotals Totals { get; set; } = new TokenTotals();
    }
}
