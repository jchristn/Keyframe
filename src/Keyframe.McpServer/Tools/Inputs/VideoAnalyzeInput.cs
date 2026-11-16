namespace Keyframe.McpServer.Tools.Inputs
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Serialization;

    /// <summary>
    /// Arguments of keyframe_video_analyze.
    /// </summary>
    public class VideoAnalyzeInput
    {
        /// <summary>
        /// Video by Url (preferred) or base64 Data.
        /// </summary>
        public MediaSource? Video { get; set; } = null;

        /// <summary>
        /// Background applied to every prompt.
        /// </summary>
        [JsonConverter(typeof(ContextListConverter))]
        public List<ContextItem> Context { get; set; } = new List<ContextItem>();

        /// <summary>
        /// Whole-video summary options.
        /// </summary>
        public SummarizeSpec? Summarize { get; set; } = null;

        /// <summary>
        /// Whole-video fields.
        /// </summary>
        public List<FieldSpec> Fields { get; set; } = new List<FieldSpec>();

        /// <summary>
        /// Extraction mode. Default Combined.
        /// </summary>
        public ExtractModeEnum? Mode { get; set; } = null;

        /// <summary>
        /// Scene breakdown options.
        /// </summary>
        public ScenesSpec? Scenes { get; set; } = null;

        /// <summary>
        /// Transcription options.
        /// </summary>
        public TranscribeSpec? Transcribe { get; set; } = null;

        /// <summary>
        /// Vision endpoint and model.
        /// </summary>
        public ModelSelection? Model { get; set; } = null;

        /// <summary>
        /// Text-only endpoint for whole-video functions.
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
