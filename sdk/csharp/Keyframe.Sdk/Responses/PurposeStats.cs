namespace Keyframe.Sdk.Responses
{
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Model call attempt statistics for one call purpose.
    /// </summary>
    public class PurposeStats
    {
        /// <summary>
        /// Purpose.
        /// </summary>
        public ModelCallPurposeEnum Purpose { get; set; } = ModelCallPurposeEnum.Summarize;

        /// <summary>
        /// Attempts.
        /// </summary>
        public long Calls { get; set; } = 0;

        /// <summary>
        /// Attempts that failed.
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Attempt duration in milliseconds.
        /// </summary>
        public Distribution? DurationMs { get; set; } = null;

        /// <summary>
        /// Time to first token in milliseconds.
        /// </summary>
        public Distribution? TimeToFirstTokenMs { get; set; } = null;
    }
}
