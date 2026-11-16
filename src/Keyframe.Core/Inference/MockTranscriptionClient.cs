namespace Keyframe.Core.Inference
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Deterministic offline transcription client. Emits one segment per three seconds of the duration hint.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class MockTranscriptionClient : ITranscriptionClient
    {
        #region Public-Members

        /// <inheritdoc />
        public string EndpointName { get; }

        /// <inheritdoc />
        public ProviderTypeEnum Provider
        {
            get
            {
                return ProviderTypeEnum.Mock;
            }
        }

        /// <inheritdoc />
        public string? Model { get; }

        /// <summary>
        /// Segment length in milliseconds. Default 3000. Minimum 100, maximum 600000.
        /// </summary>
        public long SegmentMs
        {
            get
            {
                return _SegmentMs;
            }
            set
            {
                _SegmentMs = Math.Clamp(value, 100L, 600000L);
            }
        }

        /// <summary>
        /// Calls made so far.
        /// </summary>
        public int CallCount
        {
            get
            {
                return Volatile.Read(ref _CallCount);
            }
        }

        #endregion

        #region Private-Members

        private long _SegmentMs = 3000;
        private int _CallCount = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the mock client.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <param name="model">Model name reported in responses.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpointName is null or empty.</exception>
        public MockTranscriptionClient(string endpointName, string? model = "mock-whisper")
        {
            if (String.IsNullOrWhiteSpace(endpointName)) throw new ArgumentNullException(nameof(endpointName));
            EndpointName = endpointName;
            Model = model;
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            token.ThrowIfCancellationRequested();
            Stopwatch sw = Stopwatch.StartNew();
            Interlocked.Increment(ref _CallCount);

            long duration = request.DurationMs ?? 10000L;
            TranscriptionResponse response = new TranscriptionResponse
            {
                Success = true,
                Provider = ProviderTypeEnum.Mock,
                Model = request.Model ?? Model,
                Language = request.Language ?? "en",
                StatusCode = 200
            };

            StringBuilder text = new StringBuilder();
            int index = 0;
            for (long start = 0; start < duration; start += _SegmentMs)
            {
                index++;
                string segment = "Mock speech segment " + index + ".";
                response.Segments.Add(new TranscriptionSegment
                {
                    StartMs = start,
                    EndMs = Math.Min(duration, start + _SegmentMs),
                    Text = segment
                });

                if (text.Length > 0) text.Append(' ');
                text.Append(segment);
            }

            response.Text = text.ToString();
            response.DurationMs = sw.Elapsed.TotalMilliseconds;
            return Task.FromResult(response);
        }

        #endregion
    }
}
