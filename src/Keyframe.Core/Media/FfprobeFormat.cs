namespace Keyframe.Core.Media
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Container format section of ffprobe JSON output.
    /// </summary>
    public class FfprobeFormat
    {
        /// <summary>
        /// Comma-separated format names.
        /// </summary>
        [JsonPropertyName("format_name")]
        public string? FormatName { get; set; } = null;

        /// <summary>
        /// Duration in seconds.
        /// </summary>
        [JsonPropertyName("duration")]
        public string? Duration { get; set; } = null;

        /// <summary>
        /// Size in bytes.
        /// </summary>
        [JsonPropertyName("size")]
        public string? Size { get; set; } = null;

        /// <summary>
        /// Overall bit rate.
        /// </summary>
        [JsonPropertyName("bit_rate")]
        public string? BitRate { get; set; } = null;

        /// <summary>
        /// Container tags.
        /// </summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; } = null;
    }
}
