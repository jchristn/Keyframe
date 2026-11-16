namespace Keyframe.Core.Inference.Providers
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Settings;
    using global::PolyPrompt.Clients;
    using global::PolyPrompt.Models;
    using global::PolyPrompt.Options;

    /// <summary>
    /// Completion client backed by a PolyPrompt provider client. Translates an <see cref="InferenceRequest"/> into a
    /// multimodal chat (system prompt, then the user prompt followed by each image and its label) and copies token
    /// usage into the response. Failures, including PolyPrompt validation errors, are reported in the response.
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

                List<ChatMessage> messages = BuildMessages(request);
                CompletionOptions options = BuildOptions(request, model);
                ChatResponse response = await _Client.ChatAsync(messages, options, token).ConfigureAwait(false);

                result.Success = response.Success;
                result.Text = response.Text;
                result.StatusCode = response.StatusCode;
                result.FinishReason = response.FinishReason;
                result.Model = String.IsNullOrEmpty(response.Model) ? model : response.Model;
                if (!response.Success)
                {
                    result.Error = String.IsNullOrEmpty(response.Error) ? "Provider returned an unsuccessful response." : response.Error;
                    result.Retryable = PolyPromptClientBuilder.IsRetryable(response.StatusCode);
                }
                else if (String.IsNullOrEmpty(response.Text))
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

        private static List<ChatMessage> BuildMessages(InferenceRequest request)
        {
            List<ChatMessage> messages = new List<ChatMessage>();
            if (!String.IsNullOrWhiteSpace(request.SystemPrompt)) messages.Add(ChatMessage.System(request.SystemPrompt));

            if (request.Images.Count == 0)
            {
                messages.Add(ChatMessage.User(String.IsNullOrEmpty(request.UserPrompt) ? " " : request.UserPrompt));
                return messages;
            }

            List<ContentPart> parts = new List<ContentPart>();
            if (!String.IsNullOrWhiteSpace(request.UserPrompt)) parts.Add(ContentPart.FromText(request.UserPrompt));
            foreach (InferenceImage image in request.Images)
            {
                if (!String.IsNullOrWhiteSpace(image.Label)) parts.Add(ContentPart.FromText(image.Label));
                parts.Add(ContentPart.Image(image.Data, image.MediaType));
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
            options.MaxTokens = request.MaxOutputTokens ?? _Endpoint.MaxOutputTokens;
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
