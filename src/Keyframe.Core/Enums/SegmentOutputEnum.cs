namespace Keyframe.Core.Enums
{
    /// <summary>
    /// What a segment request returns.
    /// </summary>
    public enum SegmentOutputEnum
    {
        /// <summary>
        /// A video clip (mp4 or webm), with the audio track when the source has one.
        /// </summary>
        Video,

        /// <summary>
        /// An audio clip (mp3, wav, flac, m4a, or ogg).
        /// </summary>
        Audio,

        /// <summary>
        /// A single still frame (jpg or png) at StartMs.
        /// </summary>
        Frame
    }
}
