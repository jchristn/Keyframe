namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of an audio analysis.
    /// </summary>
    public class AudioAnalysisResponse : AnalysisResponse
    {
        /// <summary>
        /// Transcript, when transcription ran.
        /// </summary>
        public TranscriptResult? Transcript { get; set; } = null;

        /// <summary>
        /// Whether and how the audio was heard. Always present.
        /// </summary>
        public ListeningInfo Listening { get; set; } = new ListeningInfo();

        /// <summary>
        /// Deterministic measurements, when Functions.Measure.Enable is true and the measurement succeeded.
        /// </summary>
        public AudioMeasurements? Measurements { get; set; } = null;

        /// <summary>
        /// Sections split at silences, when Functions.Sections.Enable is true.
        /// </summary>
        public List<SectionResult>? Sections { get; set; } = null;
    }
}
