namespace Keyframe.Sdk.Responses
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// A configured model endpoint with secrets masked, plus its health.
    /// </summary>
    public class EndpointResponse
    {
        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// Completion or transcription.
        /// </summary>
        public EndpointKindEnum Kind { get; set; } = EndpointKindEnum.Completion;

        /// <summary>
        /// Base URL, when configured.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Whether an API key is configured.
        /// </summary>
        public bool ApiKeySet { get; set; } = false;

        /// <summary>
        /// Last four characters of the API key, when set.
        /// </summary>
        public string? ApiKeyLast4 { get; set; } = null;

        /// <summary>
        /// Default model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Maximum concurrent requests.
        /// </summary>
        public int MaxConcurrentRequests { get; set; } = 0;

        /// <summary>
        /// Per-call timeout in milliseconds.
        /// </summary>
        public int TimeoutMs { get; set; } = 0;

        /// <summary>
        /// Whether the endpoint is active.
        /// </summary>
        public bool Active { get; set; } = true;

        /// <summary>
        /// Whether this is a default for vision, text, transcription, or listening.
        /// </summary>
        public bool IsDefault { get; set; } = false;

        /// <summary>
        /// Whether the provider can be used by this build of Keyframe.
        /// </summary>
        public bool ProviderAvailable { get; set; } = false;

        /// <summary>
        /// Whether the provider accepts images.
        /// </summary>
        public bool SupportsImages { get; set; } = false;

        /// <summary>
        /// Whether the provider is sent a native JSON response format for extraction.
        /// </summary>
        public bool SupportsStructuredOutput { get; set; } = false;

        /// <summary>
        /// Whether the provider can serve transcription endpoints.
        /// </summary>
        public bool SupportsTranscription { get; set; } = false;

        /// <summary>
        /// Whether the provider accepts audio input on chat, so the endpoint can be a listening endpoint
        /// (Gemini, OpenAiCompatible, Mock).
        /// </summary>
        public bool SupportsAudioInput { get; set; } = false;

        /// <summary>
        /// Last observed health.
        /// </summary>
        public bool Healthy { get; set; } = false;

        /// <summary>
        /// Explanation when unhealthy or unavailable.
        /// </summary>
        public string? HealthMessage { get; set; } = null;

        /// <summary>
        /// UTC time health was last checked.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; } = null;
    }
}
