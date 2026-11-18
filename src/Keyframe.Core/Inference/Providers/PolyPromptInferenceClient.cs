namespace Keyframe.Core.Inference.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Settings;
    using global::PolyPrompt.Clients;
    using global::PolyPrompt.Models;
    using global::PolyPrompt.Options;

    /// <summary>
    /// Completion client backed by a PolyPrompt provider client. Translates an <see cref="InferenceRequest"/> into a
    /// multimodal chat (system prompt, then the user prompt followed by each image and its label), streams the reply,
    /// and copies token usage and timing (time to first token, generation time, tokens per second) into the response. Failures, including PolyPrompt validation errors, are reported in the response.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class PolyPromptInferenceClient : IInferenceClient, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Endpoint name from settings.
        /// </summary>
        public string EndpointName
        {
            get
            {
                return _Endpoint.Name;
            }
        }

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider
        {
            get
            {
                return _Endpoint.Provider;
            }
        }

        /// <summary>
        /// Default model.
        /// </summary>
        public string? Model
        {
            get
            {
                return _Endpoint.Model;
            }
        }

        #endregion

        #region Private-Members

        private readonly EndpointSettings _Endpoint;
        private readonly ProviderClientBase _Client;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the endpoint lacks a required URL or API key.</exception>
        /// <exception cref="NotSupportedException">Thrown when the provider is not supported.</exception>
        public PolyPromptInferenceClient(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            _Endpoint = endpoint;
            _Client = PolyPromptClientBuilder.Build(endpoint);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Run a completion.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public async Task<InferenceResponse> CompleteAsync(InferenceRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            token.ThrowIfCancellationRequested();

            string? model = String.IsNullOrWhiteSpace(request.Model) ? _Endpoint.Model : request.Model;
            InferenceResponse result = new InferenceResponse { Provider = _Endpoint.Provider, Model = model };
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                if (request.Images.Count > 0 && !_Client.Supports(Capability.ImageInput))
                    throw new CapabilityNotSupportedException(_Client.ProviderName, Capability.ImageInput, nameof(CompleteAsync));
                if (request.Audio.Count > 0 && !_Client.Supports(Capability.AudioInput))
                    throw new CapabilityNotSupportedException(_Client.ProviderName, Capability.AudioInput, nameof(CompleteAsync));

                // Voxtral (through vLLM and mistral-common) rejects a system prompt in any request that carries audio, so for
                // OpenAI-compatible servers the system prompt leads the user message instead. Every such server accepts this shape.
                bool foldSystem = request.Audio.Count > 0 && _Endpoint.Provider == ProviderTypeEnum.OpenAiCompatible;
                List<ChatMessage> messages = BuildMessages(request, foldSystem);
                CompletionOptions options = BuildOptions(request, model);
                ChatStreamingResponse response = await _Client.ChatStreamingAsync(messages, options, token).ConfigureAwait(false);

                double? firstTokenMs = null;
                double? lastTokenMs = null;
                StringBuilder streamed = new StringBuilder();
                if (response.Success)
                {
                    await foreach (ChatStreamingChunk chunk in response.Chunks.ConfigureAwait(false))
                    {
                        bool hasText = !String.IsNullOrEmpty(chunk.Text);
                        bool hasReasoning = !String.IsNullOrEmpty(chunk.ReasoningText);
                        if (hasText) streamed.Append(chunk.Text);
                        if (hasText || hasReasoning)
                        {
                            double now = sw.Elapsed.TotalMilliseconds;
                            if (!firstTokenMs.HasValue) firstTokenMs = now;
                            lastTokenMs = now;
                        }
                    }
                }

                string? text = !String.IsNullOrEmpty(response.Text) ? response.Text : (streamed.Length > 0 ? streamed.ToString() : null);
                bool completed = response.Success && response.State == StreamingState.Completed;

                result.Success = completed;
                result.Text = text;
                result.StatusCode = response.StatusCode;
                result.FinishReason = response.FinishReason;
                result.Model = String.IsNullOrEmpty(response.Model) ? model : response.Model;
                if (!completed)
                {
                    string? error = response.Error ?? response.StreamError;
                    result.Error = String.IsNullOrEmpty(error) ? "Provider returned an unsuccessful response." : error;
                    result.Retryable = response.Success ? true : PolyPromptClientBuilder.IsRetryable(response.StatusCode);
                    result.Unreachable = !response.Success && (!response.StatusCode.HasValue || response.StatusCode.Value == 0);
                }
                else if (String.IsNullOrEmpty(text))
                {
                    result.Success = false;
                    result.Error = "Provider returned an empty response" + (String.IsNullOrEmpty(response.FinishReason) ? "." : " (finish reason " + response.FinishReason + ").");
                    result.Retryable = true;
                }

                if (response.Usage != null)
                {
                    result.PromptTokens = response.Usage.PromptTokens;
                    result.CompletionTokens = response.Usage.CompletionTokens;
                    result.TotalTokens = response.Usage.TotalTokens ?? Sum(response.Usage.PromptTokens, response.Usage.CompletionTokens);
                    result.CachedTokens = response.Usage.CachedPromptTokens;
                    result.ReasoningTokens = response.Usage.ReasoningTokens;
                }

                if (firstTokenMs.HasValue && lastTokenMs.HasValue)
                {
                    result.TimeToFirstTokenMs = Math.Round(firstTokenMs.Value, 3);
                    result.GenerationMs = Math.Round(lastTokenMs.Value - firstTokenMs.Value, 3);
                    if (result.CompletionTokens.HasValue && result.CompletionTokens.Value > 0 && result.GenerationMs.Value > 0)
                        result.TokensPerSecond = Math.Round(result.CompletionTokens.Value / (result.GenerationMs.Value / 1000.0), 3);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CapabilityNotSupportedException ex)
            {
                result.Success = false;
                result.Error = "Endpoint '" + _Endpoint.Name + "' cannot take this input: " + ex.Message;
            }
            catch (ArgumentException ex)
            {
                result.Success = false;
                result.Error = "Request rejected before sending: " + ex.Message;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                result.Retryable = true;
                result.Unreachable = ex is not OperationCanceledException;
            }

            result.DurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 3);
            return result;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) _Client.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private static List<ChatMessage> BuildMessages(InferenceRequest request, bool foldSystem)
        {
            List<ChatMessage> messages = new List<ChatMessage>();
            bool hasSystem = !String.IsNullOrWhiteSpace(request.SystemPrompt);
            if (hasSystem && !foldSystem) messages.Add(ChatMessage.System(request.SystemPrompt));

            if (request.Images.Count == 0 && request.Audio.Count == 0)
            {
                messages.Add(ChatMessage.User(String.IsNullOrEmpty(request.UserPrompt) ? " " : request.UserPrompt));
                return messages;
            }

            List<ContentPart> parts = new List<ContentPart>();
            if (hasSystem && foldSystem) parts.Add(ContentPart.FromText(request.SystemPrompt.Trim() + "\n\n" + (request.UserPrompt ?? String.Empty)));
            else if (!String.IsNullOrWhiteSpace(request.UserPrompt)) parts.Add(ContentPart.FromText(request.UserPrompt));
            foreach (InferenceImage image in request.Images)
            {
                if (!String.IsNullOrWhiteSpace(image.Label)) parts.Add(ContentPart.FromText(image.Label));
                parts.Add(ContentPart.Image(image.Data, image.MediaType));
            }

            foreach (InferenceAudio audio in request.Audio)
            {
                if (!String.IsNullOrWhiteSpace(audio.Label)) parts.Add(ContentPart.FromText(audio.Label));
                parts.Add(ContentPart.Audio(audio.Data, audio.MediaType));
            }

            messages.Add(ChatMessage.User(parts));
            return messages;
        }

        private CompletionOptions BuildOptions(InferenceRequest request, string? model)
        {
            CompletionOptions options;
            if (request.ResponseFormat == ResponseFormatEnum.JsonObject && _Client is ResponsesApiClientBase)
            {
                // OpenAI, Azure OpenAI, and xAI accept a JSON object format. Other providers rely on the prompt and lenient parsing.
                options = new ResponsesApiCompletionOptions { TextFormat = JsonObjectFormat() };
            }
            else
            {
                options = new CompletionOptions();
            }

            options.Model = model;
            // A request's token budget is a floor, never below the endpoint's MaxOutputTokens: reasoning models spend
            // output tokens thinking before they answer, and a tight budget derived from a word limit leaves them none.
            options.MaxTokens = Math.Max(request.MaxOutputTokens ?? 0, _Endpoint.MaxOutputTokens);
            double? temperature = request.Temperature ?? _Endpoint.Temperature;
            if (temperature.HasValue) options.Temperature = temperature.Value;
            return options;
        }

        private static Dictionary<string, object> JsonObjectFormat()
        {
            return new Dictionary<string, object> { { "type", "json_object" } };
        }

        private static long? Sum(int? a, int? b)
        {
            if (!a.HasValue && !b.HasValue) return null;
            return (long)(a ?? 0) + (b ?? 0);
        }

        #endregion
    }
}
