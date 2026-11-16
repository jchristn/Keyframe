namespace Keyframe.Core.Responses
{
    /// <summary>
    /// Aggregate statistics for a set of analyses and their model call attempts. Analysis distributions are over
    /// analyses; ModelCallMs, TimeToFirstTokenMs, GenerationMs, and TokensPerSecond are over model call attempts.
    /// Distributions are null when there are no samples.
    /// </summary>
    public class StatBlock
    {
        /// <summary>
        /// Analyses.
        /// </summary>
        public long Count { get; set; } = 0;

        /// <summary>
        /// Analyses that succeeded.
        /// </summary>
        public long Succeeded { get; set; } = 0;

        /// <summary>
        /// Analyses that failed.
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Succeeded divided by Count, 0 to 1. 0 when there are no analyses.
        /// </summary>
        public double SuccessRate { get; set; } = 0;

        /// <summary>
        /// Analysis wall-clock time in milliseconds.
        /// </summary>
        public Distribution? AnalysisMs { get; set; } = null;

        /// <summary>
        /// Job queue wait in milliseconds (0 for images).
        /// </summary>
        public Distribution? QueueWaitMs { get; set; } = null;

        /// <summary>
        /// Media duration in milliseconds (video and audio).
        /// </summary>
        public Distribution? MediaDurationMs { get; set; } = null;

        /// <summary>
        /// AnalysisMs divided by MediaDurationMs (video and audio).
        /// </summary>
        public Distribution? RealtimeFactor { get; set; } = null;

        /// <summary>
        /// Media size in bytes.
        /// </summary>
        public Distribution? MediaBytes { get; set; } = null;

        /// <summary>
        /// Logical model calls.
        /// </summary>
        public long ModelCalls { get; set; } = 0;

        /// <summary>
        /// Logical model calls that failed.
        /// </summary>
        public long FailedModelCalls { get; set; } = 0;

        /// <summary>
        /// Retries for transient failures.
        /// </summary>
        public long Retries { get; set; } = 0;

        /// <summary>
        /// Repair attempts for unusable output.
        /// </summary>
        public long RepairAttempts { get; set; } = 0;

        /// <summary>
        /// Model call attempt duration in milliseconds.
        /// </summary>
        public Distribution? ModelCallMs { get; set; } = null;

        /// <summary>
        /// Time to first token in milliseconds, over attempts that reported it.
        /// </summary>
        public Distribution? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Generation time in milliseconds, over attempts that reported it.
        /// </summary>
        public Distribution? GenerationMs { get; set; } = null;

        /// <summary>
        /// Tokens per second, over attempts that reported it.
        /// </summary>
        public Distribution? TokensPerSecond { get; set; } = null;

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
        /// Total tokens per analysis.
        /// </summary>
        public Distribution? TokensPerAnalysis { get; set; } = null;

        /// <summary>
        /// Images sent to models.
        /// </summary>
        public long InputImages { get; set; } = 0;

        /// <summary>
        /// Bytes of images sent to models.
        /// </summary>
        public long InputImageBytes { get; set; } = 0;

        /// <summary>
        /// Bytes of audio sent to transcription.
        /// </summary>
        public long InputAudioBytes { get; set; } = 0;

        /// <summary>
        /// Scenes detected.
        /// </summary>
        public long Scenes { get; set; } = 0;

        /// <summary>
        /// Transcript characters produced.
        /// </summary>
        public long TranscriptCharacters { get; set; } = 0;
    }
}
