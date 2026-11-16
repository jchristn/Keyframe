namespace Keyframe.Core.Media
{
    /// <summary>
    /// A model-ready image.
    /// </summary>
    public class NormalizedImage
    {
        /// <summary>
        /// File path of the encoded image.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Encoded bytes.
        /// </summary>
        public byte[] Data { get; set; } = System.Array.Empty<byte>();

        /// <summary>
        /// MIME type of the encoded bytes.
        /// </summary>
        public string MediaType { get; set; } = "image/jpeg";

        /// <summary>
        /// Width in pixels.
        /// </summary>
        public int Width { get; set; } = 0;

        /// <summary>
        /// Height in pixels.
        /// </summary>
        public int Height { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the encoded bytes, lowercase hex.
        /// </summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp in milliseconds when the image is a video frame.
        /// </summary>
        public long? TimestampMs { get; set; } = null;
    }
}
