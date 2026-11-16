namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// Totals across every model call in an analysis.
    /// </summary>
    public class TokenTotals
    {
        /// <summary>
        /// Model calls made.
        /// </summary>
        public int ModelCalls { get; set; } = 0;

        /// <summary>
        /// Model calls that failed.
        /// </summary>
        public int FailedModelCalls { get; set; } = 0;

        /// <summary>
        /// Sum of model call durations in milliseconds. Parallel calls make this larger than wall-clock time.
        /// </summary>
        public double ModelMs { get; set; } = 0;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long CompletionTokens { get; set; } = 0;

        /// <summary>
        /// Total tokens.
        /// </summary>
        public long TotalTokens { get; set; } = 0;

        /// <summary>
        /// Cached input tokens.
        /// </summary>
        public long CachedTokens { get; set; } = 0;

        /// <summary>
        /// Reasoning tokens.
        /// </summary>
        public long ReasoningTokens { get; set; } = 0;

        /// <summary>
        /// Images sent to models.
        /// </summary>
        public int InputImages { get; set; } = 0;

        /// <summary>
        /// Bytes of images sent to models.
        /// </summary>
        public long InputImageBytes { get; set; } = 0;
    }
}
