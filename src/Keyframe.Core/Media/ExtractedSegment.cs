namespace Keyframe.Core.Media
{
    /// <summary>
    /// A segment cut from a source file.
    /// </summary>
    public class ExtractedSegment
    {
        /// <summary>
        /// Path of the encoded segment in the workspace.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Media type.
        /// </summary>
        public string MediaType { get; set; } = string.Empty;

        /// <summary>
        /// Suggested file name.
        /// </summary>
        public string Filename { get; set; } = string.Empty;

        /// <summary>
        /// Start in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End in milliseconds after clamping. Equal to StartMs for frames.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds. 0 for frames.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Whether EndMs was clamped to the media duration.
        /// </summary>
        public bool Clamped { get; set; } = false;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Encoded bytes.
        /// </summary>
        public byte[] Data { get; set; } = System.Array.Empty<byte>();
    }
}
