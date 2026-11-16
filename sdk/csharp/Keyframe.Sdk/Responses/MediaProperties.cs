namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Deterministic media properties from magic-byte detection and ffprobe.
    /// </summary>
    public class MediaProperties
    {
        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// Detected MIME type.
        /// </summary>
        public string? ContentType { get; set; } = null;

        /// <summary>
        /// Container or image format name reported by ffprobe.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// SHA-256 of the media bytes, lowercase hex.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Width in pixels.
        /// </summary>
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height in pixels.
        /// </summary>
        public int? Height { get; set; } = null;

        /// <summary>
        /// Duration in milliseconds (video and audio).
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Frames per second (video).
        /// </summary>
        public double? FrameRate { get; set; } = null;

        /// <summary>
        /// Overall bit rate in bits per second.
        /// </summary>
        public long? BitRate { get; set; } = null;

        /// <summary>
        /// Video or image codec.
        /// </summary>
        public string? VideoCodec { get; set; } = null;

        /// <summary>
        /// Whether an audio stream is present.
        /// </summary>
        public bool HasAudio { get; set; } = false;

        /// <summary>
        /// Audio codec.
        /// </summary>
        public string? AudioCodec { get; set; } = null;

        /// <summary>
        /// Audio channel count.
        /// </summary>
        public int? AudioChannels { get; set; } = null;

        /// <summary>
        /// Audio sample rate in hertz.
        /// </summary>
        public int? AudioSampleRate { get; set; } = null;

        /// <summary>
        /// Container and stream tags reported by ffprobe (for example camera make and model, creation time). Never null.
        /// </summary>
        public Dictionary<string, string> Tags { get; set; } = new Dictionary<string, string>();
    }
}
