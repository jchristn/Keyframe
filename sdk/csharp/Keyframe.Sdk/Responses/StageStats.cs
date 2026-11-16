namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// Duration statistics for one pipeline stage.
    /// </summary>
    public class StageStats
    {
        /// <summary>
        /// Stage name (for example Normalize, Transcribe, Summarize).
        /// </summary>
        public string Stage { get; set; } = string.Empty;

        /// <summary>
        /// Stage duration in milliseconds.
        /// </summary>
        public Distribution? DurationMs { get; set; } = null;
    }
}
