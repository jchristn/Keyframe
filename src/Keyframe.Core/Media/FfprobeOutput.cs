namespace Keyframe.Core.Media
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>
    /// ffprobe JSON output.
    /// </summary>
    public class FfprobeOutput
    {
        /// <summary>
        /// Streams.
        /// </summary>
        [JsonPropertyName("streams")]
        public List<FfprobeStream>? Streams { get; set; } = null;

        /// <summary>
        /// Container format.
        /// </summary>
        [JsonPropertyName("format")]
        public FfprobeFormat? Format { get; set; } = null;
    }
}
