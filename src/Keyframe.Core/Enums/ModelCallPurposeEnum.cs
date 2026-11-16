namespace Keyframe.Core.Enums
{
    /// <summary>
    /// Why a model call was made.
    /// </summary>
    public enum ModelCallPurposeEnum
    {
        /// <summary>
        /// Summary of the whole image or video.
        /// </summary>
        Summarize,

        /// <summary>
        /// Field extraction for the whole image or video.
        /// </summary>
        Extract,

        /// <summary>
        /// Summary of one video scene.
        /// </summary>
        SceneSummarize,

        /// <summary>
        /// Field extraction for one video scene.
        /// </summary>
        SceneExtract,

        /// <summary>
        /// Speech-to-text transcription.
        /// </summary>
        Transcribe
    }
}
