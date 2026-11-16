namespace Keyframe.Core.Media
{
    /// <summary>
    /// A segment of extracted audio for transcription.
    /// </summary>
    public class AudioChunk
    {
        /// <summary>
        /// Zero-based chunk index.
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// File path (16 kHz mono FLAC).
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Offset of the chunk from the start of the media in milliseconds.
        /// </summary>
        public long OffsetMs { get; set; } = 0;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// MIME type.
        /// </summary>
        public string MediaType { get; set; } = "audio/flac";
    }
}
