namespace Keyframe.Core.Models
{
    using System;
    using System.Collections.Generic;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Responses;

    /// <summary>
    /// One finished analysis stored for analytics.
    /// </summary>
    public class AnalysisRecord
    {
        #region Public-Members

        /// <summary>
        /// Analysis identifier (prefix "kfa_"), the same as the response Id.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// HTTP request identifier, when known.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Job identifier. Null for images.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// Access key name of the caller, when known.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Whether the analysis succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Error summary when the analysis failed.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC completion time.
        /// </summary>
        public DateTime CompletedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Analysis wall-clock time in milliseconds.
        /// </summary>
        public double AnalysisMs { get; set; } = 0;

        /// <summary>
        /// Job queue wait in milliseconds. 0 for images.
        /// </summary>
        public double QueueWaitMs { get; set; } = 0;

        /// <summary>
        /// Media duration in milliseconds (video and audio), when known.
        /// </summary>
        public long? MediaDurationMs { get; set; } = null;

        /// <summary>
        /// Media size in bytes.
        /// </summary>
        public long MediaBytes { get; set; } = 0;

        /// <summary>
        /// AnalysisMs divided by MediaDurationMs (video and audio), when both are known.
        /// </summary>
        public double? RealtimeFactor { get; set; } = null;

        /// <summary>
        /// Vision endpoint, when used.
        /// </summary>
        public string? VisionEndpoint { get; set; } = null;

        /// <summary>
        /// Vision model, when used.
        /// </summary>
        public string? VisionModel { get; set; } = null;

        /// <summary>
        /// Text endpoint, when used.
        /// </summary>
        public string? TextEndpoint { get; set; } = null;

        /// <summary>
        /// Text model, when used.
        /// </summary>
        public string? TextModel { get; set; } = null;

        /// <summary>
        /// Transcription endpoint, when used.
        /// </summary>
        public string? TranscriptionEndpoint { get; set; } = null;

        /// <summary>
        /// Transcription model, when used.
        /// </summary>
        public string? TranscriptionModel { get; set; } = null;

        /// <summary>
        /// Logical model calls (a call with retries or repairs counts once).
        /// </summary>
        public int ModelCalls { get; set; } = 0;

        /// <summary>
        /// Logical model calls that did not produce a usable result.
        /// </summary>
        public int FailedModelCalls { get; set; } = 0;

        /// <summary>
        /// Retries for transient failures, across calls.
        /// </summary>
        public int Retries { get; set; } = 0;

        /// <summary>
        /// Repair attempts for unusable output, across calls.
        /// </summary>
        public int RepairAttempts { get; set; } = 0;

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

        /// <summary>
        /// Bytes of audio sent to models: transcription audio plus audio clips attached to completion calls.
        /// </summary>
        public long InputAudioBytes { get; set; } = 0;

        /// <summary>
        /// Scenes in the result (video).
        /// </summary>
        public int Scenes { get; set; } = 0;

        /// <summary>
        /// Transcript length in characters.
        /// </summary>
        public long TranscriptCharacters { get; set; } = 0;

        /// <summary>
        /// Extracted fields, including per-scene fields.
        /// </summary>
        public int FieldCount { get; set; } = 0;

        /// <summary>
        /// Extracted fields whose value failed validation.
        /// </summary>
        public int InvalidFields { get; set; } = 0;

        /// <summary>
        /// Stage timings in order. Never null.
        /// </summary>
        public List<StageTiming> Stages
        {
            get
            {
                return _Stages;
            }
            set
            {
                _Stages = value ?? new List<StageTiming>();
            }
        }

        /// <summary>
        /// Model call attempts. Returned only when reading one analysis; null in listings.
        /// </summary>
        public List<ModelCallRecord>? Calls { get; set; } = null;

        #endregion

        #region Private-Members

        private List<StageTiming> _Stages = new List<StageTiming>();

        #endregion
    }
}
