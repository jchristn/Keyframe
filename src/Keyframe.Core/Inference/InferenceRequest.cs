namespace Keyframe.Core.Inference
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;

    /// <summary>
    /// A provider-neutral completion request with optional images.
    /// </summary>
    public class InferenceRequest
    {
        /// <summary>
        /// Why the call is made.
        /// </summary>
        public ModelCallPurposeEnum Purpose { get; set; } = ModelCallPurposeEnum.Summarize;

        /// <summary>
        /// Field key or scene index the call serves, when applicable.
        /// </summary>
        public string? Target { get; set; } = null;

        /// <summary>
        /// System prompt.
        /// </summary>
        public string SystemPrompt { get; set; } = string.Empty;

        /// <summary>
        /// User prompt text. Images follow the text in order.
        /// </summary>
        public string UserPrompt { get; set; } = string.Empty;

        /// <summary>
        /// Images, in order. Never null.
        /// </summary>
        public List<InferenceImage> Images { get; set; } = new List<InferenceImage>();

        /// <summary>
        /// Requested response format.
        /// </summary>
        public ResponseFormatEnum ResponseFormat { get; set; } = ResponseFormatEnum.Text;

        /// <summary>
        /// JSON Schema text for JsonObject responses, used for structured output where the provider supports it.
        /// </summary>
        public string? JsonSchema { get; set; } = null;

        /// <summary>
        /// Model override. Null uses the endpoint model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Maximum output tokens. Null uses the endpoint setting.
        /// </summary>
        public int? MaxOutputTokens { get; set; } = null;

        /// <summary>
        /// Temperature. Null uses the endpoint setting.
        /// </summary>
        public double? Temperature { get; set; } = null;
    }
}
