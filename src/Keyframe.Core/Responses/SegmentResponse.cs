namespace Keyframe.Core.Responses
{
    /// <summary>
    /// A cut segment returned as JSON.
    /// </summary>
    public class SegmentResponse
    {
        /// <summary>
        /// Media type, for example video/mp4, audio/mpeg, or image/jpeg.
        /// </summary>
        public string MediaType { get; set; } = string.Empty;

        /// <summary>
        /// Suggested file name, for example segment-12000-18500.mp4.
        /// </summary>
        public string Filename { get; set; } = string.Empty;

        /// <summary>
        /// Start of the segment in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End of the segment in milliseconds, after clamping to the media duration. Equal to StartMs for frames.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Segment duration in milliseconds. 0 for frames.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Size of the media in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Media as base64.
        /// </summary>
        public string Data { get; set; } = string.Empty;
    }
}
