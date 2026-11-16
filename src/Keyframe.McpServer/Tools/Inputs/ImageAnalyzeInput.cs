namespace Keyframe.McpServer.Tools.Inputs
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Serialization;

    /// <summary>
    /// Arguments of keyframe_image_analyze.
    /// </summary>
    public class ImageAnalyzeInput
    {
        /// <summary>
        /// Image by Url (preferred) or base64 Data.
        /// </summary>
        public MediaSource? Image { get; set; } = null;

        /// <summary>
        /// Background applied to every prompt: a string, an array of strings, or {Label, Text} objects.
        /// </summary>
        [JsonConverter(typeof(ContextListConverter))]
        public List<ContextItem> Context { get; set; } = new List<ContextItem>();

        /// <summary>
        /// Summary options. Null enables a default summary when no Fields are given.
        /// </summary>
        public SummarizeSpec? Summarize { get; set; } = null;

        /// <summary>
        /// Fields to extract.
        /// </summary>
        public List<FieldSpec> Fields { get; set; } = new List<FieldSpec>();

        /// <summary>
        /// Extraction mode. Default Combined.
        /// </summary>
        public ExtractModeEnum? Mode { get; set; } = null;

        /// <summary>
        /// Endpoint and model selection.
        /// </summary>
        public ModelSelection? Model { get; set; } = null;
    }
}
