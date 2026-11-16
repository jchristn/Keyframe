namespace Keyframe.Core.Responses
{
    /// <summary>
    /// Summary statistics over a set of samples. Percentiles use linear interpolation between closest ranks; StdDev
    /// is the population standard deviation. A distribution with no samples is returned as null instead.
    /// </summary>
    public class Distribution
    {
        /// <summary>
        /// Number of samples.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Smallest sample.
        /// </summary>
        public double Min { get; set; } = 0;

        /// <summary>
        /// Largest sample.
        /// </summary>
        public double Max { get; set; } = 0;

        /// <summary>
        /// Mean.
        /// </summary>
        public double Avg { get; set; } = 0;

        /// <summary>
        /// 50th percentile (median).
        /// </summary>
        public double P50 { get; set; } = 0;

        /// <summary>
        /// 90th percentile.
        /// </summary>
        public double P90 { get; set; } = 0;

        /// <summary>
        /// 95th percentile.
        /// </summary>
        public double P95 { get; set; } = 0;

        /// <summary>
        /// 99th percentile.
        /// </summary>
        public double P99 { get; set; } = 0;

        /// <summary>
        /// Population standard deviation.
        /// </summary>
        public double StdDev { get; set; } = 0;

        /// <summary>
        /// Sum of samples.
        /// </summary>
        public double Sum { get; set; } = 0;
    }
}
