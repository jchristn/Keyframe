namespace Keyframe.Sdk.Requests
{
    /// <summary>
    /// Input media, supplied as a URL or as base64 data. Exactly one of Url or Data is required.
    /// </summary>
    public class MediaSource
    {
        /// <summary>
        /// HTTP or HTTPS URL to download.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Base64-encoded media bytes.
        /// </summary>
        public string? Data { get; set; } = null;

        /// <summary>
        /// Declared MIME type. Optional; detection uses magic bytes and falls back to this value.
        /// </summary>
        public string? MediaType { get; set; } = null;
    }
}
