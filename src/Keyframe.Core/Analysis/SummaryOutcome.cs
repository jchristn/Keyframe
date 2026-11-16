namespace Keyframe.Core.Analysis
{
    /// <summary>
    /// Outcome of a summarize function.
    /// </summary>
    public class SummaryOutcome
    {
        /// <summary>
        /// Whether a summary was produced.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Summary text.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Error when unsuccessful.
        /// </summary>
        public string? Error { get; set; } = null;
    }
}
