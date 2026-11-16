namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Bucketed request history counts for charts. Every bucket in the range is present, including empty ones.
    /// </summary>
    public class RequestHistorySummary
    {
        /// <summary>
        /// Total requests in range.
        /// </summary>
        public long TotalCount { get; set; } = 0;

        /// <summary>
        /// Requests with a status code below 400.
        /// </summary>
        public long TotalSuccess { get; set; } = 0;

        /// <summary>
        /// Requests with a status code of 400 or more.
        /// </summary>
        public long TotalFailure { get; set; } = 0;

        /// <summary>
        /// Average duration in milliseconds.
        /// </summary>
        public double AverageDurationMs { get; set; } = 0;

        /// <summary>
        /// Bucket width in minutes.
        /// </summary>
        public int BucketMinutes { get; set; } = 60;

        /// <summary>
        /// Buckets in ascending time order.
        /// </summary>
        public List<RequestHistorySummaryBucket> Buckets { get; set; } = new List<RequestHistorySummaryBucket>();
    }
}
