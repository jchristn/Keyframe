namespace Keyframe.Core.Inference.Providers
{
    using System;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Settings;
    using global::PolyPrompt.Clients;

    /// <summary>
    /// Builds PolyPrompt provider clients from Keyframe endpoint settings and registers the PolyPrompt integration
    /// with an <see cref="InferenceClientFactory"/>.
    /// Thread safety: stateless; safe for concurrent use.
    /// </summary>
    public static class PolyPromptClientBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Register the PolyPrompt completion and transcription providers with a factory.
        /// </summary>
        /// <param name="factory">Factory.</param>
        /// <exception cref="ArgumentNullException">Thrown when factory is null.</exception>
        public static void Register(InferenceClientFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            factory.CompletionProviderSupported = SupportsCompletion;
            factory.TranscriptionProviderSupported = SupportsTranscription;
            factory.CompletionProviderFactory = endpoint => SupportsCompletion(endpoint) ? new PolyPromptInferenceClient(endpoint) : null;
            factory.TranscriptionProviderFactory = endpoint => SupportsTranscription(endpoint) ? new PolyPromptTranscriptionClient(endpoint) : null;
        }

        /// <summary>
        /// Whether a provider can serve completion endpoints through PolyPrompt.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>True when supported.</returns>
        public static bool SupportsCompletion(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            switch (endpoint.Provider)
            {
                case ProviderTypeEnum.Ollama:
                case ProviderTypeEnum.OllamaCloud:
                case ProviderTypeEnum.OpenAi:
                case ProviderTypeEnum.OpenAiCompatible:
                case ProviderTypeEnum.AzureOpenAi:
                case ProviderTypeEnum.Anthropic:
                case ProviderTypeEnum.Gemini:
                case ProviderTypeEnum.XAi:
                case ProviderTypeEnum.Cohere:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a provider can serve transcription endpoints through PolyPrompt.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>True when supported.</returns>
        public static bool SupportsTranscription(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            switch (endpoint.Provider)
            {
                case ProviderTypeEnum.OpenAi:
                case ProviderTypeEnum.OpenAiCompatible:
                case ProviderTypeEnum.AzureOpenAi:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Whether a provider accepts images. True for the mock provider and every supported completion provider; a
        /// specific model can still be text only.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>True when images are accepted.</returns>
        public static bool SupportsImages(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            return endpoint.Provider == ProviderTypeEnum.Mock || SupportsCompletion(endpoint);
        }

        /// <summary>
        /// Whether a provider accepts audio input on chat: Gemini, OpenAI-compatible servers (including llama-server, vLLM,
        /// and OpenAI's Chat Completions audio models), and the mock provider. A specific model can still lack audio.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>True when audio input is accepted.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        public static bool SupportsAudioInput(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (endpoint.Kind != EndpointKindEnum.Completion) return false;
            return endpoint.Provider == ProviderTypeEnum.Mock
                || endpoint.Provider == ProviderTypeEnum.Gemini
                || endpoint.Provider == ProviderTypeEnum.OpenAiCompatible;
        }

        /// <summary>
        /// Whether a provider is sent a native JSON object response format: OpenAI, Azure OpenAI, xAI, and the mock provider.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>True when structured output is requested natively.</returns>
        public static bool SupportsStructuredOutput(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            return endpoint.Provider == ProviderTypeEnum.Mock
                || endpoint.Provider == ProviderTypeEnum.OpenAi
                || endpoint.Provider == ProviderTypeEnum.AzureOpenAi
                || endpoint.Provider == ProviderTypeEnum.XAi;
        }

        /// <summary>
        /// Build the PolyPrompt client for an endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>Client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the endpoint lacks a required URL or API key.</exception>
        /// <exception cref="NotSupportedException">Thrown when the provider is not supported.</exception>
        public static ProviderClientBase Build(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ClientOptions options = new ClientOptions
            {
                TimeoutMs = endpoint.TimeoutMs,
                CallDetailCapture = global::PolyPrompt.Models.CallDetailCapture.None,
                RedactCredentials = true,
                RedactMediaInCallDetails = true,
                MaxInlineMediaBytes = endpoint.MaxInlineMediaBytes
            };

            string? url = String.IsNullOrWhiteSpace(endpoint.Endpoint) ? null : endpoint.Endpoint.Trim();
            string? key = String.IsNullOrWhiteSpace(endpoint.ApiKey) ? null : endpoint.ApiKey.Trim();

            switch (endpoint.Provider)
            {
                case ProviderTypeEnum.Ollama:
                    return url == null ? new OllamaClient(apiKey: key, options: options) : new OllamaClient(url, key, options);
                case ProviderTypeEnum.OllamaCloud:
                    return new OllamaCloudClient(RequireKey(endpoint, key), url, options);
                case ProviderTypeEnum.OpenAi:
                    return new OpenAiClient(RequireKey(endpoint, key), url, options);
                case ProviderTypeEnum.OpenAiCompatible:
                    return new OpenAiCompatibleClient(RequireUrl(endpoint, url), key, options);
                case ProviderTypeEnum.AzureOpenAi:
                    return new AzureOpenAiClient(RequireUrl(endpoint, url), RequireKey(endpoint, key), options);
                case ProviderTypeEnum.Anthropic:
                    return new AnthropicClient(RequireKey(endpoint, key), url, options);
                case ProviderTypeEnum.Gemini:
                    return new GeminiClient(RequireKey(endpoint, key), url, options);
                case ProviderTypeEnum.XAi:
                    return new XAiClient(RequireKey(endpoint, key), url, options);
                case ProviderTypeEnum.Cohere:
                    return new CohereClient(RequireKey(endpoint, key), url, options);
                default:
                    throw new NotSupportedException("Provider " + endpoint.Provider + " is not supported by the PolyPrompt integration.");
            }
        }

        /// <summary>
        /// Whether a failed call may succeed on retry: rate limits, server errors, timeouts, and failures without a
        /// status code (connection errors).
        /// </summary>
        /// <param name="statusCode">HTTP status code, or null.</param>
        /// <returns>True when retryable.</returns>
        public static bool IsRetryable(int? statusCode)
        {
            if (!statusCode.HasValue) return true;
            int code = statusCode.Value;
            return code == 408 || code == 409 || code == 425 || code == 429 || code >= 500;
        }

        #endregion

        #region Private-Methods

        private static string RequireKey(EndpointSettings endpoint, string? key)
        {
            if (key == null) throw new ArgumentException("Endpoint '" + endpoint.Name + "' (" + endpoint.Provider + ") requires an ApiKey.");
            return key;
        }

        private static string RequireUrl(EndpointSettings endpoint, string? url)
        {
            if (url == null) throw new ArgumentException("Endpoint '" + endpoint.Name + "' (" + endpoint.Provider + ") requires an Endpoint URL.");
            return url;
        }

        #endregion
    }
}
