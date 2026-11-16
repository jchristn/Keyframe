namespace Keyframe.Sdk.Responses
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Telemetry for one model call, including every retry and repair attempt.
    /// </summary>
    public class ModelCallTelemetry
    {
        /// <summary>
        /// Why the call was made.
        /// </summary>
        public ModelCallPurposeEnum Purpose { get; set; } = ModelCallPurposeEnum.Summarize;

        /// <summary>
        /// Field key or scene index the call served, when applicable.
        /// </summary>
        public string? Target { get; set; } = null;

        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// Model name reported by the provider, or the requested model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// UTC start time of the first attempt.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Total duration in milliseconds across attempts, including backoff.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Time to first token in milliseconds, when streaming reports it.
        /// </summary>
        public double? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Attempts made (1 means no retries).
        /// </summary>
        public int Attempts { get; set; } = 1;

        /// <summary>
        /// Repair attempts made after unparseable output.
        /// </summary>
        public int RepairAttempts { get; set; } = 0;

        /// <summary>
        /// Whether the call produced a usable result.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Error message when the call failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// HTTP status code from the last attempt, when known.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Images sent with the call.
        /// </summary>
        public int InputImages { get; set; } = 0;

        /// <summary>
        /// Total bytes of images sent with the call.
        /// </summary>
        public long InputImageBytes { get; set; } = 0;

        /// <summary>
        /// Bytes of audio sent with the call (transcription).
        /// </summary>
        public long InputAudioBytes { get; set; } = 0;

        /// <summary>
        /// Prompt (input) tokens, summed across attempts.
        /// </summary>
        public long? PromptTokens { get; set; } = null;

        /// <summary>
        /// Completion (output) tokens, summed across attempts.
        /// </summary>
        public long? CompletionTokens { get; set; } = null;

        /// <summary>
        /// Total tokens, summed across attempts.
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
        /// Finish reason of the last attempt.
        /// </summary>
        public string? FinishReason { get; set; } = null;
    }
}
