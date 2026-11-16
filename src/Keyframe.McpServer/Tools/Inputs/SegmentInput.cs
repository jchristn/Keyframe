namespace Keyframe.McpServer.Tools.Inputs
{
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;

    /// <summary>
    /// Arguments of keyframe_segment.
    /// </summary>
    public class SegmentInput
    {
        /// <summary>
        /// Source video or audio by Url (preferred) or base64 Data.
        /// </summary>
        public MediaSource? Source { get; set; } = null;

        /// <summary>
        /// Start of the range in milliseconds (the frame time for Frame output).
        /// </summary>
        public long? StartMs { get; set; } = null;

        /// <summary>
        /// End of the range in milliseconds. Required for Video and Audio output; ignored for Frame.
        /// </summary>
        public long? EndMs { get; set; } = null;

        /// <summary>
        /// Video, Audio, or Frame. Default Video.
        /// </summary>
        public SegmentOutputEnum? Output { get; set; } = null;

        /// <summary>
        /// Output format, for example mp4, webm, mp3, wav, jpg, or png. Null uses the default for the output.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Optional cap on the long edge of video and frame output in pixels.
        /// </summary>
        public int? MaxEdge { get; set; } = null;
    }
}
