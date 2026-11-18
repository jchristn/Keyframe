namespace Keyframe.Core.Enums
{
    /// <summary>
    /// What the whole-recording model calls heard of the audio.
    /// </summary>
    public enum ListeningModeEnum
    {
        /// <summary>
        /// One clip of the whole recording.
        /// </summary>
        Full,

        /// <summary>
        /// The whole-recording results were reduced from per-section results; each section heard its own clip.
        /// </summary>
        Sections,

        /// <summary>
        /// Evenly spaced excerpts, because the recording is longer than the listen budget or the inline limit.
        /// </summary>
        Excerpts,

        /// <summary>
        /// Video only: each scene call carried that scene's audio.
        /// </summary>
        Scenes,

        /// <summary>
        /// No model heard the audio; results come from the transcript, or nothing needed audio.
        /// </summary>
        None
    }
}
