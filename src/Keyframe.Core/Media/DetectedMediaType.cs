namespace Keyframe.Core.Media
{
    using Keyframe.Core.Enums;

    /// <summary>
    /// Result of magic-byte media detection.
    /// </summary>
    public class DetectedMediaType
    {
        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// MIME type, or null when unknown.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Conventional file extension including the dot, for example ".jpg".
        /// </summary>
        public string Extension { get; set; } = ".bin";
    }
}
