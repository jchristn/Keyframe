namespace Keyframe.Core.Media
{
    using System.Text.Json.Serialization;

    /// <summary>
    /// The disposition flags ffprobe reports for a stream. Only the flags Keyframe uses are mapped.
    /// </summary>
    public class FfprobeDisposition
    {
        /// <summary>
        /// 1 when the stream is an attached picture (cover art in an audio file), otherwise 0.
        /// </summary>
        [JsonPropertyName("attached_pic")]
        public int AttachedPic { get; set; } = 0;

        /// <summary>
        /// 1 when the stream is the default stream of its type, otherwise 0.
        /// </summary>
        [JsonPropertyName("default")]
        public int Default { get; set; } = 0;
    }
}
