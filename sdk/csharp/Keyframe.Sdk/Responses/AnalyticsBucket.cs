namespace Keyframe.Sdk.Responses
{
    using System;

    /// <summary>
    /// One time bucket of an analytics summary.
    /// </summary>
    public class AnalyticsBucket
    {
        /// <summary>
        /// UTC start of the bucket (inclusive).
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Statistics for analyses that completed in the bucket.
        /// </summary>
        public StatBlock Stats { get; set; } = new StatBlock();
    }
}
