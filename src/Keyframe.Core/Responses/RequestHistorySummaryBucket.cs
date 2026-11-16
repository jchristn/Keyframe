namespace Keyframe.Core.Responses
{
    using System;

    /// <summary>
    /// One time bucket of request history counts.
    /// </summary>
    public class RequestHistorySummaryBucket
    {
        /// <summary>
        /// Inclusive bucket start (UTC).
        /// </summary>
        public DateTime BucketStartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Exclusive bucket end (UTC).
        /// </summary>
        public DateTime BucketEndUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Requests with a status code below 400.
        /// </summary>
        public long SuccessCount { get; set; } = 0;

        /// <summary>
        /// Requests with a status code of 400 or more.
        /// </summary>
        public long FailureCount { get; set; } = 0;

        /// <summary>
        /// Average duration in milliseconds, zero for empty buckets.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;
    }
}
