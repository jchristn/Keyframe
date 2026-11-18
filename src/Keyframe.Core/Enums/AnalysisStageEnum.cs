namespace Keyframe.Core.Enums
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
        /// Measure loudness, levels, silence, and stereo width.
        /// </summary>
        Measure,

        /// <summary>
        /// Extract the audio track.
        /// </summary>
        AudioExtraction,

        /// <summary>
        /// Find section boundaries at silences.
        /// </summary>
        SectionDetection,

        /// <summary>
        /// Render the audio clips sent to listening models.
        /// </summary>
        ListenExtraction,

        /// <summary>
        /// Transcribe speech.
        /// </summary>
        Transcribe,

        /// <summary>
        /// Analyze each scene.
        /// </summary>
        Scenes,

        /// <summary>
        /// Analyze each audio section.
        /// </summary>
        Sections,

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
