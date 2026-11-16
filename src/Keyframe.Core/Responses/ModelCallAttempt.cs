namespace Keyframe.Core.Responses
{
    using System;

    /// <summary>
    /// One attempt of a model call (the first try, a retry, or a repair). Collected for analytics; not part of the
    /// response contract.
    /// </summary>
    public class ModelCallAttempt
    {
        /// <summary>
        /// One-based attempt number within the logical call.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// UTC time the attempt started.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the provider returned a successful response. A successful response with unusable output still counts as a success here.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Error message when the attempt failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// HTTP status code, when known.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Attempt duration in milliseconds, including time waiting on the endpoint's concurrency gate.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Time spent waiting on the endpoint's concurrency gate in milliseconds.
        /// </summary>
        public double QueueMs { get; set; } = 0;

        /// <summary>
        /// Time to first token in milliseconds, when known.
        /// </summary>
        public double? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Generation time in milliseconds, when known.
        /// </summary>
        public double? GenerationMs { get; set; } = null;

        /// <summary>
        /// Completion tokens per second of generation time, when known.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

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
        /// Finish reason.
        /// </summary>
        public string? FinishReason { get; set; } = null;
    }
}
