namespace Keyframe.Sdk.Enums
{
    /// <summary>
    /// Named stage of an analysis pipeline.
    /// </summary>
    public enum AnalysisStageEnum
    {
        /// <summary>
        /// Download or decode the input media.
        /// </summary>
        Resolve,

        /// <summary>
        /// Read media properties with ffprobe.
        /// </summary>
        Probe,

        /// <summary>
        /// Re-encode an image for the model.
        /// </summary>
        Normalize,

        /// <summary>
        /// Find scene boundaries.
        /// </summary>
        SceneDetection,

        /// <summary>
        /// Extract keyframes.
        /// </summary>
        FrameExtraction,

        /// <summary>
        /// Extract the audio track.
        /// </summary>
        AudioExtraction,

        /// <summary>
        /// Transcribe speech.
        /// </summary>
        Transcribe,

        /// <summary>
        /// Analyze each scene.
        /// </summary>
        Scenes,

        /// <summary>
        /// Summarize the media.
        /// </summary>
        Summarize,

        /// <summary>
        /// Extract fields from the media.
        /// </summary>
        Extract,

        /// <summary>
        /// Combine scene results into whole-video results.
        /// </summary>
        Reduce
    }
}
