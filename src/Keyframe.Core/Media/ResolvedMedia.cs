namespace Keyframe.Core.Media
{
    using Keyframe.Core.Enums;

    /// <summary>
    /// Input media written to a workspace file.
    /// </summary>
    public class ResolvedMedia
    {
        /// <summary>
        /// Absolute file path inside the workspace.
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the bytes, lowercase hex.
        /// </summary>
        public string Sha256 { get; set; } = string.Empty;

        /// <summary>
        /// Detected MIME type, falling back to the declared or server-reported type.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Media kind from magic bytes. Unknown when detection failed; the prober decides in that case.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// Source URL, when the media was downloaded.
        /// </summary>
        public string? SourceUrl { get; set; } = null;
    }
}
