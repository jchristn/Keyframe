namespace Keyframe.Sdk.Requests
{
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Request to cut a time range (a video clip, an audio clip, or one frame) out of a video or audio source.
    /// The source is resolved again on every call; Keyframe does not retain uploaded media.
    /// </summary>
    public class SegmentRequest
    {
        /// <summary>
        /// Source video or audio. Required.
        /// </summary>
        public MediaSource? Source { get; set; } = null;

        /// <summary>
        /// Start of the range in milliseconds. Minimum 0. For Frame output, the frame time.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End of the range in milliseconds (exclusive). Required for Video and Audio output and must exceed StartMs;
        /// ignored for Frame output. Values past the end of the media are clamped.
        /// </summary>
        public long? EndMs { get; set; } = null;

        /// <summary>
        /// What to return. Default Video.
        /// </summary>
        public SegmentOutputEnum Output { get; set; } = SegmentOutputEnum.Video;

        /// <summary>
        /// Container or image format. Video: mp4 (default) or webm. Audio: mp3 (default), wav, flac, m4a, or ogg.
        /// Frame: jpg (default) or png. Null uses the default for the output.
        /// </summary>
        public string? Format { get; set; } = null;

        /// <summary>
        /// Optional cap on the long edge of video and frame output in pixels, 16 to 4096. Smaller media is not enlarged.
        /// Null keeps the source size.
        /// </summary>
        public int? MaxEdge { get; set; } = null;

        /// <summary>
        /// Binary (default) returns the media as the body; Json returns a SegmentResponse with base64 data.
        /// </summary>
        public SegmentResponseFormatEnum ResponseFormat { get; set; } = SegmentResponseFormatEnum.Binary;
    }
}
