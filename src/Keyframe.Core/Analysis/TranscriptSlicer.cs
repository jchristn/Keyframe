namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Slices a transcript to a time window. Shared by video scenes and audio sections.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class TranscriptSlicer
    {
        /// <summary>
        /// Join the text of every segment that overlaps [startMs, endMs).
        /// </summary>
        /// <param name="transcript">Transcript.</param>
        /// <param name="startMs">Window start in milliseconds.</param>
        /// <param name="endMs">Window end in milliseconds.</param>
        /// <returns>Joined segment text; empty when no segment overlaps.</returns>
        /// <exception cref="ArgumentNullException">Thrown when transcript is null.</exception>
        public static string Slice(TranscriptResult transcript, long startMs, long endMs)
        {
            ArgumentNullException.ThrowIfNull(transcript);
            IEnumerable<string> parts = transcript.Segments
                .Where(s => s.EndMs > startMs && s.StartMs < endMs)
                .Select(s => s.Text.Trim())
                .Where(s => s.Length > 0);
            return String.Join(" ", parts);
        }
    }
}
