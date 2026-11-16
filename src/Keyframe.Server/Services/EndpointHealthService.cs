namespace Keyframe.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Inference.Providers;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Tracks model endpoint health. Completion endpoints are probed with a one-token prompt; endpoints whose provider
    /// is not available in this build are reported unhealthy without a call. Probes run at startup and every
    /// five minutes, and on demand through the test route.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class EndpointHealthService : IDisposable
    {
        #region Private-Members

        private const string Header = "[EndpointHealth] ";
        private static readonly TimeSpan _Interval = TimeSpan.FromMinutes(5);

        private readonly AnalysisServices _Services;
        private readonly Func<KeyframeSettings> _Settings;
        private readonly LoggingModule _Logging;
        private readonly ConcurrentDictionary<string, EndpointHealthState> _States = new ConcurrentDictionary<string, EndpointHealthState>(StringComparer.OrdinalIgnoreCase);
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Loop = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Analysis services.</param>
        /// <param name="settings">Live settings accessor.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public EndpointHealthService(AnalysisServices services, Func<KeyframeSettings> settings, LoggingModule logging)
        {
            _Services = services ?? throw new ArgumentNullException(nameof(services));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start periodic probing.
        /// </summary>
        public void Start()
        {
            KeyframeMetrics.EndpointHealthProvider = Snapshot;
            if (_Loop != null) return;
            _Loop = Task.Run(() => LoopAsync(_Stopping.Token));
        }

        /// <summary>
        /// Stop probing.
        /// </summary>
        public void Stop()
        {
            _Stopping.Cancel();
            KeyframeMetrics.EndpointHealthProvider = null;
        }

        /// <summary>
        /// Health keyed by endpoint name for active endpoints.
        /// </summary>
        /// <returns>Snapshot.</returns>
        public IReadOnlyDictionary<string, bool> Snapshot()
        {
            Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (EndpointSettings endpoint in _Settings().Inference.Endpoints.Where(e => e.Active))
            {
                EndpointHealthState? state;
                result[endpoint.Name] = _States.TryGetValue(endpoint.Name, out state) && state.Healthy;
            }

            return result;
        }

        /// <summary>
        /// Describe every configured endpoint with secrets masked.
        /// </summary>
        /// <returns>Endpoints.</returns>
        public List<EndpointResponse> Describe()
        {
            KeyframeSettings settings = _Settings();
            List<EndpointResponse> list = new List<EndpointResponse>();
            foreach (EndpointSettings endpoint in settings.Inference.Endpoints)
            {
                EndpointHealthState? state;
                _States.TryGetValue(endpoint.Name, out state);
                bool isDefault = String.Equals(endpoint.Name, settings.Inference.DefaultVision, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(endpoint.Name, settings.Inference.DefaultText, StringComparison.OrdinalIgnoreCase)
                    || String.Equals(endpoint.Name, settings.Inference.DefaultTranscription, StringComparison.OrdinalIgnoreCase);

                list.Add(new EndpointResponse
                {
                    Name = endpoint.Name,
                    Provider = endpoint.Provider,
                    Kind = endpoint.Kind,
                    Endpoint = endpoint.Endpoint,
                    ApiKeySet = !String.IsNullOrEmpty(endpoint.ApiKey),
                    ApiKeyLast4 = String.IsNullOrEmpty(endpoint.ApiKey) || endpoint.ApiKey.Length <= 4 ? null : endpoint.ApiKey.Substring(endpoint.ApiKey.Length - 4),
                    Model = endpoint.Model,
                    MaxConcurrentRequests = endpoint.MaxConcurrentRequests,
                    TimeoutMs = endpoint.TimeoutMs,
                    Active = endpoint.Active,
                    IsDefault = isDefault,
                    ProviderAvailable = _Services.Clients.IsProviderAvailable(endpoint),
                    SupportsImages = PolyPromptClientBuilder.SupportsImages(endpoint),
                    SupportsStructuredOutput = PolyPromptClientBuilder.SupportsStructuredOutput(endpoint),
                    SupportsTranscription = endpoint.Provider == ProviderTypeEnum.Mock || PolyPromptClientBuilder.SupportsTranscription(endpoint),
                    Healthy = state != null && state.Healthy,
                    HealthMessage = state?.Message ?? (endpoint.Active ? "Not checked yet." : "Inactive."),
                    LastCheckedUtc = state?.CheckedUtc
                });
            }

            return list;
        }

        /// <summary>
        /// Probe one endpoint now.
        /// </summary>
        /// <param name="name">Endpoint name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result, or null when the endpoint is not configured.</returns>
        public async Task<EndpointTestResponse?> TestAsync(string name, CancellationToken token = default)
        {
            EndpointSettings? endpoint = _Settings().Inference.Endpoints.FirstOrDefault(e => String.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
            if (endpoint == null) return null;
            return await ProbeAsync(endpoint, token).ConfigureAwait(false);
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
            if (disposing)
            {
                _Stopping.Cancel();
                _Stopping.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (EndpointSettings endpoint in _Settings().Inference.Endpoints.Where(e => e.Active).ToList())
                {
                    if (token.IsCancellationRequested) return;
                    try
                    {
                        await ProbeAsync(endpoint, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        _Logging.Debug(Header + endpoint.Name + " probe error: " + ex.Message);
                    }
                }

                try
                {
                    await Task.Delay(_Interval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }

        private async Task<EndpointTestResponse> ProbeAsync(EndpointSettings endpoint, CancellationToken token)
        {
            EndpointTestResponse result = new EndpointTestResponse { Name = endpoint.Name, Model = endpoint.Model };
            Stopwatch sw = Stopwatch.StartNew();

            if (!_Services.Clients.IsProviderAvailable(endpoint))
            {
                result.Error = "Provider " + endpoint.Provider + " does not support " + endpoint.Kind.ToString().ToLowerInvariant() + " endpoints.";
            }
            else if (endpoint.Kind == EndpointKindEnum.Transcription)
            {
                // Transcription endpoints are not probed with audio; a client that builds counts as healthy.
                try
                {
                    _Services.Clients.GetTranscriptionClient(endpoint);
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
            }
            else
            {
                try
                {
                    IInferenceClient client = _Services.Clients.GetCompletionClient(endpoint);
                    InferenceResponse response = await client.CompleteAsync(new InferenceRequest
                    {
                        Purpose = ModelCallPurposeEnum.Summarize,
                        Target = "health",
                        SystemPrompt = "You are a health check. Reply with the single word OK.",
                        UserPrompt = "Reply with OK.",
                        MaxOutputTokens = 16
                    }, token).ConfigureAwait(false);

                    // A reachable endpoint that answers 2xx is healthy even when a reasoning model spends the small
                    // token budget thinking and returns no text.
                    bool reachable = response.StatusCode.HasValue && response.StatusCode.Value >= 200 && response.StatusCode.Value < 300;
                    result.Success = response.Success || reachable;
                    result.Model = response.Model ?? endpoint.Model;
                    result.Error = result.Success ? null : response.Error;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                }
            }

            result.DurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
            _States[endpoint.Name] = new EndpointHealthState { Healthy = result.Success, Message = result.Success ? "OK" : result.Error, CheckedUtc = DateTime.UtcNow };
            return result;
        }

        #endregion
    }
}
