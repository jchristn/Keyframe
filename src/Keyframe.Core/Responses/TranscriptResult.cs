namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Transcript of a media audio track.
    /// </summary>
    public class TranscriptResult
    {
        /// <summary>
        /// Detected or requested language.
        /// </summary>
        public string? Language { get; set; } = null;

        /// <summary>
        /// Full transcript text.
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// Timed segments in order. Never null.
        /// </summary>
        public List<TranscriptSegmentResult> Segments { get; set; } = new List<TranscriptSegmentResult>();
    }
}
