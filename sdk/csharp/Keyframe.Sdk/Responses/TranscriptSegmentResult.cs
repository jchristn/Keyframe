namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// One timed transcript segment.
    /// </summary>
    public class TranscriptSegmentResult
    {
        /// <summary>
        /// Start offset in milliseconds from the beginning of the media.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End offset in milliseconds.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Segment text.
        /// </summary>
        public string Text { get; set; } = string.Empty;
    }
}
