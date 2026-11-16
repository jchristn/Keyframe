namespace Keyframe.Core.Inference
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;

    /// <summary>
    /// A provider-neutral transcription response. Clients report failures here rather than throwing.
    /// </summary>
    public class TranscriptionResponse
    {
        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Full text.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Detected language.
        /// </summary>
        public string? Language { get; set; } = null;

        /// <summary>
        /// Segments relative to the start of the submitted audio. Never null.
        /// </summary>
        public List<TranscriptionSegment> Segments { get; set; } = new List<TranscriptionSegment>();

        /// <summary>
        /// Error when the call failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Whether a retry may succeed.
        /// </summary>
        public bool Retryable { get; set; } = false;

        /// <summary>
        /// HTTP status code, when known.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// Model that served the call.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Call duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Time spent waiting on the endpoint's concurrency gate in milliseconds. Set by the gated wrapper.
        /// </summary>
        public double QueueMs { get; set; } = 0;
    }
}
