namespace Keyframe.Core.Inference.Providers
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Settings;
    using global::PolyPrompt.Clients;
    using global::PolyPrompt.Models;
    using KfTranscriptionRequest = Keyframe.Core.Inference.TranscriptionRequest;
    using KfTranscriptionResponse = Keyframe.Core.Inference.TranscriptionResponse;
    using KfTranscriptionSegment = Keyframe.Core.Inference.TranscriptionSegment;
    using PpTranscriptionRequest = global::PolyPrompt.Models.TranscriptionRequest;
    using PpTranscriptionResponse = global::PolyPrompt.Models.TranscriptionResponse;
    using PpTranscriptionSegment = global::PolyPrompt.Models.TranscriptionSegment;

    /// <summary>
    /// Transcription client backed by a PolyPrompt provider client (OpenAI, Azure OpenAI, or an OpenAI-compatible
    /// speech-to-text server). Requests timed segments (verbose JSON) except for OpenAI's gpt-4o transcription models,
    /// which return text only; the caller then treats the chunk as one segment.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class PolyPromptTranscriptionClient : ITranscriptionClient, IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Segments whose no-speech probability is above this value and whose average log probability is below
        /// <see cref="SilenceLogProbabilityThreshold"/> are treated as silence and dropped. Default 0.6. Minimum 0, maximum 1.
        /// </summary>
        public double NoSpeechThreshold
        {
            get
            {
                return _NoSpeechThreshold;
            }
            set
            {
                _NoSpeechThreshold = Math.Clamp(value, 0.0, 1.0);
            }
        }

        /// <summary>
        /// Average log probability below which a likely-silent segment is dropped. Default -0.7. Minimum -10, maximum 0.
        /// </summary>
        public double SilenceLogProbabilityThreshold
        {
            get
            {
                return _SilenceLogProbabilityThreshold;
            }
            set
            {
                _SilenceLogProbabilityThreshold = Math.Clamp(value, -10.0, 0.0);
            }
        }

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
        private double _NoSpeechThreshold = 0.6;
        private double _SilenceLogProbabilityThreshold = -0.7;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="endpoint">Endpoint settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpoint is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the endpoint lacks a required URL or API key.</exception>
        /// <exception cref="NotSupportedException">Thrown when the provider cannot transcribe.</exception>
        public PolyPromptTranscriptionClient(EndpointSettings endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            if (!PolyPromptClientBuilder.SupportsTranscription(endpoint))
                throw new NotSupportedException("Provider " + endpoint.Provider + " does not support transcription.");
            _Endpoint = endpoint;
            _Client = PolyPromptClientBuilder.Build(endpoint);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Transcribe audio.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public async Task<KfTranscriptionResponse> TranscribeAsync(KfTranscriptionRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            token.ThrowIfCancellationRequested();

            string? model = String.IsNullOrWhiteSpace(request.Model) ? _Endpoint.Model : request.Model;
            KfTranscriptionResponse result = new KfTranscriptionResponse { Provider = _Endpoint.Provider, Model = model };
            Stopwatch sw = Stopwatch.StartNew();

            try
            {
                bool segmented = !IsTextOnlyModel(model);
                PpTranscriptionRequest wire = new PpTranscriptionRequest
                {
                    Audio = request.Audio,
                    MediaType = request.MediaType,
                    FileName = request.FileName,
                    Language = String.IsNullOrWhiteSpace(request.Language) ? null : request.Language,
                    Prompt = String.IsNullOrWhiteSpace(request.Prompt) ? null : request.Prompt,
                    Format = segmented ? TranscriptionFormat.VerboseJson : TranscriptionFormat.Json
                };
                if (segmented) wire.TimestampGranularities.Add(TranscriptionTimestampGranularity.Segment);

                TranscriptionOptions options = new TranscriptionOptions { Model = model };
                PpTranscriptionResponse response = await _Client.TranscribeAsync(wire, options, token).ConfigureAwait(false);

                result.Success = response.Success;
                result.StatusCode = response.StatusCode;
                result.Model = String.IsNullOrEmpty(response.Model) ? model : response.Model;
                result.Text = response.Text ?? String.Empty;
                result.Language = response.Language;
                if (!response.Success)
                {
                    result.Error = String.IsNullOrEmpty(response.Error) ? "Provider returned an unsuccessful response." : response.Error;
                    result.Retryable = PolyPromptClientBuilder.IsRetryable(response.StatusCode);
                }

                int dropped = 0;
                foreach (PpTranscriptionSegment segment in response.Segments)
                {
                    if (String.IsNullOrWhiteSpace(segment.Text)) continue;
                    if (IsLikelySilence(segment))
                    {
                        dropped++;
                        continue;
                    }

                    result.Segments.Add(new KfTranscriptionSegment
                    {
                        StartMs = (long)Math.Round(segment.StartSeconds * 1000.0),
                        EndMs = (long)Math.Round(segment.EndSeconds * 1000.0),
                        Text = segment.Text.Trim()
                    });
                }

                // Whisper invents short phrases ("You", "Thank you.") over silence and noise; drop them from the text too.
                if (dropped > 0) result.Text = String.Join(" ", result.Segments.Select(seg => seg.Text));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CapabilityNotSupportedException ex)
            {
                result.Success = false;
                result.Error = "Endpoint '" + _Endpoint.Name + "' cannot transcribe: " + ex.Message;
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

        private bool IsLikelySilence(PpTranscriptionSegment segment)
        {
            if (!segment.NoSpeechProbability.HasValue) return false;
            if (segment.NoSpeechProbability.Value <= _NoSpeechThreshold) return false;
            return !segment.AverageLogProbability.HasValue || segment.AverageLogProbability.Value < _SilenceLogProbabilityThreshold;
        }

        private bool IsTextOnlyModel(string? model)
        {
            if (_Endpoint.Provider == ProviderTypeEnum.OpenAiCompatible) return false;
            if (String.IsNullOrEmpty(model)) return false;
            return model.StartsWith("gpt-4o", StringComparison.OrdinalIgnoreCase);
        }

        #endregion
    }
}
