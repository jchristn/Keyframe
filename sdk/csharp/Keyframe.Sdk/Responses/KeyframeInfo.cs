namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// A representative frame extracted from a video.
    /// </summary>
    public class KeyframeInfo
    {
        /// <summary>
        /// Timestamp in milliseconds.
        /// </summary>
        public long TimestampMs { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the extracted JPEG, lowercase hex.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Width of the extracted frame in pixels.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height of the extracted frame in pixels.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Base64 JPEG thumbnail, when requested.
        /// </summary>
        public string? Data { get; set; } = null;
    }
}
