namespace Keyframe.Core.Inference
{
    using System;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Wraps a transcription client with a concurrency limit and a per-call timeout, and reports the time spent waiting for the limit as QueueMs.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class GatedTranscriptionClient : ITranscriptionClient
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

        #endregion

        #region Private-Members

        private readonly ITranscriptionClient _Inner;
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
        public GatedTranscriptionClient(ITranscriptionClient inner, int maxConcurrent, int timeoutMs)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _Inner = inner;
            _Gate = new SemaphoreSlim(Math.Max(1, maxConcurrent));
            _TimeoutMs = Math.Max(1000, timeoutMs);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, CancellationToken token = default)
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
                        TranscriptionResponse response = await _Inner.TranscribeAsync(request, linked.Token).ConfigureAwait(false);
                        response.QueueMs = queueMs;
                        return response;
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        return new TranscriptionResponse
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
