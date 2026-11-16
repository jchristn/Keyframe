namespace Keyframe.Core.Media
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// One stream from ffprobe JSON output. ffprobe reports most numbers as strings.
    /// </summary>
    public class FfprobeStream
    {
        /// <summary>
        /// Stream index.
        /// </summary>
        [JsonPropertyName("index")]
        public int Index { get; set; } = 0;

        /// <summary>
        /// Codec name.
        /// </summary>
        [JsonPropertyName("codec_name")]
        public string? CodecName { get; set; } = null;

        /// <summary>
        /// Codec type: video, audio, subtitle, data.
        /// </summary>
        [JsonPropertyName("codec_type")]
        public string? CodecType { get; set; } = null;

        /// <summary>
        /// Width in pixels.
        /// </summary>
        [JsonPropertyName("width")]
        public int? Width { get; set; } = null;

        /// <summary>
        /// Height in pixels.
        /// </summary>
        [JsonPropertyName("height")]
        public int? Height { get; set; } = null;

        /// <summary>
        /// Average frame rate as a fraction, for example "30000/1001".
        /// </summary>
        [JsonPropertyName("avg_frame_rate")]
        public string? AverageFrameRate { get; set; } = null;

        /// <summary>
        /// Real base frame rate as a fraction.
        /// </summary>
        [JsonPropertyName("r_frame_rate")]
        public string? RealFrameRate { get; set; } = null;

        /// <summary>
        /// Stream duration in seconds.
        /// </summary>
        [JsonPropertyName("duration")]
        public string? Duration { get; set; } = null;

        /// <summary>
        /// Frame count, when known.
        /// </summary>
        [JsonPropertyName("nb_frames")]
        public string? FrameCount { get; set; } = null;

        /// <summary>
        /// Audio channel count.
        /// </summary>
        [JsonPropertyName("channels")]
        public int? Channels { get; set; } = null;

        /// <summary>
        /// Audio sample rate.
        /// </summary>
        [JsonPropertyName("sample_rate")]
        public string? SampleRate { get; set; } = null;

        /// <summary>
        /// Stream tags.
        /// </summary>
        [JsonPropertyName("tags")]
        public Dictionary<string, string>? Tags { get; set; } = null;

        /// <summary>
        /// Stream disposition flags.
        /// </summary>
        [JsonPropertyName("disposition")]
        public FfprobeDisposition? Disposition { get; set; } = null;

        /// <summary>
        /// Whether this is a video stream that holds embedded cover art (an attached picture) rather than motion video.
        /// </summary>
        [JsonIgnore]
        public bool IsAttachedPicture
        {
            get
            {
                return Disposition != null && Disposition.AttachedPic != 0;
            }
        }
    }
}
