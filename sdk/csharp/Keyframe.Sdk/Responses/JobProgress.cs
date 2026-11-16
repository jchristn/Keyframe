namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// Progress of a running job.
    /// </summary>
    public class JobProgress
    {
        /// <summary>
        /// Current stage name.
        /// </summary>
        public string? Stage { get; set; } = null;

        /// <summary>
        /// Units completed in the current stage.
        /// </summary>
        public int Completed { get; set; } = 0;

        /// <summary>
        /// Total units in the current stage.
        /// </summary>
        public int Total { get; set; } = 0;

        /// <summary>
        /// Overall percent complete, 0 to 100.
        /// </summary>
        public double Percent { get; set; } = 0;
    }
}
