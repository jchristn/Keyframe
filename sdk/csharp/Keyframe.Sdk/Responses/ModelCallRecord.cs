namespace Keyframe.Sdk.Responses
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// One model call attempt stored for analytics. A logical call with a retry or a repair produces one record per attempt.
    /// </summary>
    public class ModelCallRecord
    {
        /// <summary>
        /// Record identifier (prefix "kfc_").
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Owning analysis identifier (prefix "kfa_").
        /// </summary>
        public string AnalysisId { get; set; } = String.Empty;

        /// <summary>
        /// Why the call was made.
        /// </summary>
        public ModelCallPurposeEnum Purpose { get; set; } = ModelCallPurposeEnum.Summarize;

        /// <summary>
        /// Field key, scene, or chunk the call served, when applicable.
        /// </summary>
        public string? Target { get; set; } = null;

        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Endpoint { get; set; } = String.Empty;

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// One-based attempt number within the logical call.
        /// </summary>
        public int Attempt { get; set; } = 1;

        /// <summary>
        /// UTC start time of the attempt.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the attempt produced a usable response.
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
        /// Attempt duration in milliseconds, request sent to last token, including the endpoint gate wait.
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
        /// Generation time (first to last token) in milliseconds, when known.
        /// </summary>
        public double? GenerationMs { get; set; } = null;

        /// <summary>
        /// Completion tokens per second of generation time, when known.
        /// </summary>
        public double? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Prompt tokens, when reported.
        /// </summary>
        public long? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion tokens, when reported.
        /// </summary>
        public long? CompletionTokens { get; set; } = null;

        /// <summary>
        /// Total tokens, when reported.
        /// </summary>
        public long? TotalTokens { get; set; } = null;

        /// <summary>
        /// Cached input tokens, when reported.
        /// </summary>
        public long? CachedTokens { get; set; } = null;

        /// <summary>
        /// Reasoning tokens, when reported.
        /// </summary>
        public long? ReasoningTokens { get; set; } = null;

        /// <summary>
        /// Images sent with the call.
        /// </summary>
        public int InputImages { get; set; } = 0;

        /// <summary>
        /// Bytes of images sent with the call.
        /// </summary>
        public long InputImageBytes { get; set; } = 0;

        /// <summary>
        /// Bytes of audio sent with the call.
        /// </summary>
        public long InputAudioBytes { get; set; } = 0;

        /// <summary>
        /// Finish reason, when reported.
        /// </summary>
        public string? FinishReason { get; set; } = null;
    }
}
