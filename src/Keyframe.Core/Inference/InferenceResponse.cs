namespace Keyframe.Core.Inference
{
    using Keyframe.Core.Enums;

    /// <summary>
    /// A provider-neutral completion response. Clients report failures here rather than throwing.
    /// </summary>
    public class InferenceResponse
    {
        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Response text.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Error message when the call failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Whether a retry may succeed (rate limits, server errors, timeouts).
        /// </summary>
        public bool Retryable { get; set; } = false;

        /// <summary>
        /// HTTP status code, when known.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Finish reason reported by the provider.
        /// </summary>
        public string? FinishReason { get; set; } = null;

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// Model that served the call.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long? CompletionTokens { get; set; } = null;

        /// <summary>
        /// Total tokens.
        /// </summary>
        public long? TotalTokens { get; set; } = null;

        /// <summary>
        /// Cached input tokens.
        /// </summary>
        public long? CachedTokens { get; set; } = null;

        /// <summary>
        /// Reasoning tokens.
        /// </summary>
        public long? ReasoningTokens { get; set; } = null;

        /// <summary>
        /// Time to first token in milliseconds, when streaming reports it.
        /// </summary>
        public double? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Call duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;
    }
}
