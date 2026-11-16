namespace Keyframe.McpServer.Tools.Inputs
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Serialization;

    /// <summary>
    /// Arguments of keyframe_audio_analyze.
    /// </summary>
    public class AudioAnalyzeInput
    {
        /// <summary>
        /// Audio (or a video, whose audio track is used) by Url (preferred) or base64 Data.
        /// </summary>
        public MediaSource? Audio { get; set; } = null;

        /// <summary>
        /// Background applied to every prompt.
        /// </summary>
        [JsonConverter(typeof(ContextListConverter))]
        public List<ContextItem> Context { get; set; } = new List<ContextItem>();

        /// <summary>
        /// Summary options. Null keeps the default (a summary of the transcript).
        /// </summary>
        public SummarizeSpec? Summarize { get; set; } = null;

        /// <summary>
        /// Fields to extract from the transcript.
        /// </summary>
        public List<FieldSpec> Fields { get; set; } = new List<FieldSpec>();

        /// <summary>
        /// Extraction mode. Default Combined.
        /// </summary>
        public ExtractModeEnum? Mode { get; set; } = null;

        /// <summary>
        /// Transcription options. Null keeps the default (transcription on).
        /// </summary>
        public TranscribeSpec? Transcribe { get; set; } = null;

        /// <summary>
        /// Transcription endpoint and model.
        /// </summary>
        public ModelSelection? TranscriptionModel { get; set; } = null;

        /// <summary>
        /// Text endpoint and model for the summary and fields.
        /// </summary>
        public ModelSelection? TextModel { get; set; } = null;

        /// <summary>
        /// Wait for the result. Default false (returns the job).
        /// </summary>
        public bool Wait { get; set; } = false;

        /// <summary>
        /// Wait timeout in milliseconds. Default 120000.
        /// </summary>
        public int? WaitTimeoutMs { get; set; } = null;
    }
}
