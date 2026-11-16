namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// Result of an audio analysis.
    /// </summary>
    public class AudioAnalysisResponse : AnalysisResponse
    {
        /// <summary>
        /// Transcript, when transcription ran.
        /// </summary>
        public TranscriptResult? Transcript { get; set; } = null;
    }
}
