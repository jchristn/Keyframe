namespace Keyframe.Core.Inference
{
    /// <summary>
    /// A timed segment returned by a transcription endpoint, relative to the start of the submitted audio.
    /// </summary>
    public class TranscriptionSegment
    {
        /// <summary>
        /// Start in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End in milliseconds.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Text.
        /// </summary>
        public string Text { get; set; } = string.Empty;
    }
}
