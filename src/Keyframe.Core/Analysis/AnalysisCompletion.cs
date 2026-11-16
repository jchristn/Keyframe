namespace Keyframe.Core.Analysis
{
    using Keyframe.Core.Enums;
    using Keyframe.Core.Responses;

    /// <summary>
    /// A finished analysis, handed to <see cref="AnalysisServices.Completed"/> once the response is complete. Carries
    /// the response (with telemetry), how the analysis ended, and the endpoints and media facts the analytics record needs.
    /// </summary>
    public class AnalysisCompletion
    {
        #region Public-Members

        /// <summary>
        /// The response, complete with telemetry, warnings, and errors.
        /// </summary>
        public AnalysisResponse Response { get; set; } = new AnalysisResponse();

        /// <summary>
        /// Media kind analyzed.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// Origin supplied by the host, when any.
        /// </summary>
        public AnalysisOrigin? Origin { get; set; } = null;

        /// <summary>
        /// Message of the exception that ended the analysis, or null when it ran to completion.
        /// </summary>
        public string? Exception { get; set; } = null;

        /// <summary>
        /// Whether the analysis was canceled after it started.
        /// </summary>
        public bool Canceled { get; set; } = false;

        /// <summary>
        /// Media size in bytes, when resolved.
        /// </summary>
        public long MediaBytes { get; set; } = 0;

        /// <summary>
        /// Media duration in milliseconds (video and audio), when probed.
        /// </summary>
        public long? MediaDurationMs { get; set; } = null;

        /// <summary>
        /// Vision endpoint name, when used.
        /// </summary>
        public string? VisionEndpoint { get; set; } = null;

        /// <summary>
        /// Vision model, when used.
        /// </summary>
        public string? VisionModel { get; set; } = null;

        /// <summary>
        /// Text endpoint name, when used.
        /// </summary>
        public string? TextEndpoint { get; set; } = null;

        /// <summary>
        /// Text model, when used.
        /// </summary>
        public string? TextModel { get; set; } = null;

        /// <summary>
        /// Transcription endpoint name, when used.
        /// </summary>
        public string? TranscriptionEndpoint { get; set; } = null;

        /// <summary>
        /// Transcription model, when used.
        /// </summary>
        public string? TranscriptionModel { get; set; } = null;

        /// <summary>
        /// Scenes in the result (video).
        /// </summary>
        public int Scenes { get; set; } = 0;

        /// <summary>
        /// Characters in the transcript, when transcription ran.
        /// </summary>
        public long TranscriptCharacters { get; set; } = 0;

        #endregion
    }
}
