namespace Keyframe.Core.Inference
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Builds and caches inference and transcription clients from settings. Every client is wrapped so calls to
    /// one endpoint never exceed its MaxConcurrentRequests. Clients are rebuilt when an endpoint's settings object
    /// is replaced, so live settings changes apply.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class InferenceClientFactory
    {
        #region Public-Members

        /// <summary>
        /// Optional factory for non-mock completion providers. Receives the endpoint settings and returns a client, or
        /// null when the provider is not supported. The PolyPrompt integration registers itself here.
        /// </summary>
        public Func<EndpointSettings, IInferenceClient?>? CompletionProviderFactory { get; set; } = null;

        /// <summary>
        /// Optional factory for non-mock transcription providers.
        /// </summary>
        public Func<EndpointSettings, ITranscriptionClient?>? TranscriptionProviderFactory { get; set; } = null;

        /// <summary>
        /// Optional check whether a non-mock completion provider is supported, used by <see cref="IsProviderAvailable"/>
        /// so that availability can be reported without building a client. Null falls back to calling
        /// <see cref="CompletionProviderFactory"/>.
        /// </summary>
        public Func<EndpointSettings, bool>? CompletionProviderSupported { get; set; } = null;

        /// <summary>
        /// Optional check whether a non-mock transcription provider is supported. Null falls back to calling
        /// <see cref="TranscriptionProviderFactory"/>.
        /// </summary>
        public Func<EndpointSettings, bool>? TranscriptionProviderSupported { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly Func<InferenceSettings> _Settings;
        private readonly ConcurrentDictionary<string, CachedCompletion> _Completion = new ConcurrentDictionary<string, CachedCompletion>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CachedTranscription> _Transcription = new ConcurrentDictionary<string, CachedTranscription>(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the factory.
        /// </summary>
        /// <param name="settings">Accessor returning the current inference settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public InferenceClientFactory(Func<InferenceSettings> settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Settings = settings;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the completion endpoint for a request.
        /// </summary>
        /// <param name="requested">Requested endpoint name, or null.</param>
        /// <param name="fallbacks">Default endpoint names tried in order when none is requested.</param>
        /// <returns>Endpoint settings.</returns>
        /// <exception cref="EndpointNotFoundException">Thrown when no active completion endpoint matches.</exception>
        public EndpointSettings ResolveCompletionEndpoint(string? requested, params string?[] fallbacks)
        {
            return ResolveEndpoint(requested, EndpointKindEnum.Completion, fallbacks);
        }

        /// <summary>
        /// Resolve the transcription endpoint for a request.
        /// </summary>
        /// <param name="requested">Requested endpoint name, or null.</param>
        /// <returns>Endpoint settings.</returns>
        /// <exception cref="EndpointNotFoundException">Thrown when no active transcription endpoint matches.</exception>
        public EndpointSettings ResolveTranscriptionEndpoint(string? requested)
        {
            return ResolveEndpoint(requested, EndpointKindEnum.Transcription, _Settings().DefaultTranscription);
        }

        /// <summary>
        /// Get the completion client for an endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>Concurrency-gated client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when the provider is not available.</exception>
        /// <exception cref="InferenceException">Thrown when the endpoint is misconfigured, for example a missing API key.</exception>
        public IInferenceClient GetCompletionClient(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            CachedCompletion cached = _Completion.AddOrUpdate(
                endpoint.Name,
                name => new CachedCompletion(endpoint, BuildCompletion(endpoint)),
                (name, existing) => ReferenceEquals(existing.Settings, endpoint) ? existing : new CachedCompletion(endpoint, BuildCompletion(endpoint)));
            return cached.Client;
        }

        /// <summary>
        /// Get the transcription client for an endpoint.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>Concurrency-gated client.</returns>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when the provider is not available.</exception>
        /// <exception cref="InferenceException">Thrown when the endpoint is misconfigured, for example a missing API key.</exception>
        public ITranscriptionClient GetTranscriptionClient(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            CachedTranscription cached = _Transcription.AddOrUpdate(
                endpoint.Name,
                name => new CachedTranscription(endpoint, BuildTranscription(endpoint)),
                (name, existing) => ReferenceEquals(existing.Settings, endpoint) ? existing : new CachedTranscription(endpoint, BuildTranscription(endpoint)));
            return cached.Client;
        }

        /// <summary>
        /// Whether a provider can currently be served by this factory.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <returns>True when a client can be built.</returns>
        public bool IsProviderAvailable(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (endpoint.Provider == ProviderTypeEnum.Mock) return true;
            if (endpoint.Kind == EndpointKindEnum.Completion)
            {
                if (CompletionProviderFactory == null) return false;
                if (CompletionProviderSupported != null) return CompletionProviderSupported(endpoint);
                return CompletionProviderFactory(endpoint) != null;
            }

            if (TranscriptionProviderFactory == null) return false;
            if (TranscriptionProviderSupported != null) return TranscriptionProviderSupported(endpoint);
            return TranscriptionProviderFactory(endpoint) != null;
        }

        /// <summary>
        /// Drop cached clients so the next call rebuilds them.
        /// </summary>
        public void Reset()
        {
            _Completion.Clear();
            _Transcription.Clear();
        }

        #endregion

        #region Private-Methods

        private EndpointSettings ResolveEndpoint(string? requested, EndpointKindEnum kind, params string?[] fallbacks)
        {
            InferenceSettings settings = _Settings();
            if (!String.IsNullOrWhiteSpace(requested))
            {
                EndpointSettings? named = settings.FindActive(requested);
                if (named == null) throw new EndpointNotFoundException("Endpoint '" + requested + "' is not configured or not active.");
                if (named.Kind != kind) throw new EndpointNotFoundException("Endpoint '" + requested + "' is a " + named.Kind + " endpoint, not " + kind + ".");
                return named;
            }

            List<string> tried = new List<string>();
            foreach (string? name in fallbacks)
            {
                if (String.IsNullOrWhiteSpace(name)) continue;
                tried.Add(name);
                EndpointSettings? candidate = settings.FindActive(name);
                if (candidate != null && candidate.Kind == kind) return candidate;
            }

            throw new EndpointNotFoundException("No active " + kind.ToString().ToLowerInvariant() + " endpoint is configured" +
                (tried.Count > 0 ? " (tried " + String.Join(", ", tried) + ")" : string.Empty) + ".");
        }

        private IInferenceClient BuildCompletion(EndpointSettings endpoint)
        {
            IInferenceClient? inner = null;
            if (endpoint.Provider == ProviderTypeEnum.Mock) inner = new MockInferenceClient(endpoint.Name, endpoint.Model);
            else if (CompletionProviderFactory != null) inner = Construct(endpoint, CompletionProviderFactory);

            if (inner == null)
                throw new EndpointNotFoundException("Provider " + endpoint.Provider + " for endpoint '" + endpoint.Name +
                    "' is not supported for completion.");

            return new GatedInferenceClient(inner, endpoint.MaxConcurrentRequests, endpoint.TimeoutMs);
        }

        private ITranscriptionClient BuildTranscription(EndpointSettings endpoint)
        {
            ITranscriptionClient? inner = null;
            if (endpoint.Provider == ProviderTypeEnum.Mock) inner = new MockTranscriptionClient(endpoint.Name, endpoint.Model);
            else if (TranscriptionProviderFactory != null) inner = Construct(endpoint, TranscriptionProviderFactory);

            if (inner == null)
                throw new EndpointNotFoundException("Provider " + endpoint.Provider + " for endpoint '" + endpoint.Name +
                    "' is not supported for transcription.");

            return new GatedTranscriptionClient(inner, endpoint.MaxConcurrentRequests, endpoint.TimeoutMs);
        }


        private static T? Construct<T>(EndpointSettings endpoint, Func<EndpointSettings, T?> factory) where T : class
        {
            try
            {
                return factory(endpoint);
            }
            catch (ArgumentException ex)
            {
                throw new InferenceException("Endpoint '" + endpoint.Name + "' is misconfigured: " + ex.Message, ex);
            }
            catch (NotSupportedException ex)
            {
                throw new InferenceException("Endpoint '" + endpoint.Name + "' is misconfigured: " + ex.Message, ex);
            }
        }

        #endregion
    }
}
