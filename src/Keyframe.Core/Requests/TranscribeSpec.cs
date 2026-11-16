namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Speech-to-text options for video.
    /// </summary>
    public class TranscribeSpec
    {
        /// <summary>
        /// Whether to transcribe the audio track. Default false.
        /// </summary>
        public bool Enable { get; set; } = false;

        /// <summary>
        /// Transcription endpoint name. Null uses the default transcription endpoint.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Model override.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Spoken language as an ISO-639-1 code. Null lets the model detect it.
        /// </summary>
        public string? Language { get; set; } = null;

        /// <summary>
        /// Optional prompt to guide spelling of names and terms.
        /// </summary>
        public string? Prompt { get; set; } = null;
    }
}
