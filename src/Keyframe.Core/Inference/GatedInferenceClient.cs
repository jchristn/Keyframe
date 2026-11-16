namespace Keyframe.Core.Inference
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Wraps a completion client with a concurrency limit and a per-call timeout, and reports the time spent waiting for the limit as QueueMs. A timeout is reported as a
    /// retryable failure; caller cancellation still throws.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class GatedInferenceClient : IInferenceClient
    {
        #region Public-Members

        /// <inheritdoc />
        public string EndpointName
        {
            get
            {
                return _Inner.EndpointName;
            }
        }

        /// <inheritdoc />
        public ProviderTypeEnum Provider
        {
            get
            {
                return _Inner.Provider;
            }
        }

        /// <inheritdoc />
        public string? Model
        {
            get
            {
                return _Inner.Model;
            }
        }

        /// <summary>
        /// Wrapped client.
        /// </summary>
        public IInferenceClient Inner
        {
            get
            {
                return _Inner;
            }
        }

        #endregion

        #region Private-Members

        private readonly IInferenceClient _Inner;
        private readonly SemaphoreSlim _Gate;
        private readonly int _TimeoutMs;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="inner">Client to wrap.</param>
        /// <param name="maxConcurrent">Maximum concurrent calls.</param>
        /// <param name="timeoutMs">Per-call timeout in milliseconds.</param>
        /// <exception cref="ArgumentNullException">Thrown when inner is null.</exception>
        public GatedInferenceClient(IInferenceClient inner, int maxConcurrent, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _Inner = inner;
            _Gate = new SemaphoreSlim(Math.Max(1, maxConcurrent));
            _TimeoutMs = Math.Max(1000, timeoutMs);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<InferenceResponse> CompleteAsync(InferenceRequest request, CancellationToken token = default)
        {
            Stopwatch queue = Stopwatch.StartNew();
            await _Gate.WaitAsync(token).ConfigureAwait(false);
            queue.Stop();
            double queueMs = Math.Round(queue.Elapsed.TotalMilliseconds, 3);
            try
            {
                using (CancellationTokenSource timeout = new CancellationTokenSource(_TimeoutMs))
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
                {
                    try
                    {
                        InferenceResponse response = await _Inner.CompleteAsync(request, linked.Token).ConfigureAwait(false);
                        response.QueueMs = queueMs;
                        return response;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        return new InferenceResponse
                        {
                            Success = false,
                            Retryable = true,
                            StatusCode = 504,
                            Provider = _Inner.Provider,
                            Model = request.Model ?? _Inner.Model,
                            Error = "Endpoint '" + _Inner.EndpointName + "' timed out after " + _TimeoutMs + "ms.",
                            DurationMs = _TimeoutMs,
                            QueueMs = queueMs
                        };
                    }
                }
            }
            finally
            {
                _Gate.Release();
            }
        }

        #endregion
    }
}
