namespace Keyframe.Core.Analysis
{
    using System;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Calls a completion endpoint with retries for transient failures and repair attempts for unusable output,
    /// and records one telemetry entry per logical call.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class ModelInvoker
    {
        #region Public-Members

        /// <summary>
        /// Health lookup told about endpoints a call could not reach, or null. Default null.
        /// </summary>
        public IEndpointHealthLookup? Health { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly Func<InferenceSettings> _Settings;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Accessor returning the current inference settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public ModelInvoker(Func<InferenceSettings> settings)
        {
            ArgumentNullException.ThrowIfNull(settings);
            _Settings = settings;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Invoke a model.
        /// </summary>
        /// <param name="client">Client.</param>
        /// <param name="request">Request.</param>
        /// <param name="validate">Optional validator returning an error message for unusable output, or null when usable.</param>
        /// <param name="context">Analysis context that receives the telemetry entry.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Invocation result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when client, request, or context is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<InvocationResult> InvokeAsync(
            IInferenceClient client,
            InferenceRequest request,
            Func<string, string?>? validate,
            AnalysisContext context,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(context);

            InferenceSettings settings = _Settings();
            ModelCallTelemetry telemetry = new ModelCallTelemetry
            {
                Purpose = request.Purpose,
                Target = request.Target,
                Endpoint = client.EndpointName,
                Provider = client.Provider,
                Model = request.Model ?? client.Model,
                StartUtc = DateTime.UtcNow,
                InputImages = request.Images.Count,
                InputImageBytes = request.Images.Sum(i => (long)i.Data.Length),
                InputAudioBytes = request.Audio.Sum(a => (long)a.Data.Length),
                Attempts = 0
            };

            InvocationResult result = new InvocationResult { Telemetry = telemetry };
            Stopwatch sw = Stopwatch.StartNew();
            string originalPrompt = request.UserPrompt;

            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("inference " + request.Purpose.ToString(), ActivityKind.Client))
            {
                activity?.SetTag("keyframe.endpoint", client.EndpointName);
                activity?.SetTag("keyframe.provider", client.Provider.ToString());
                activity?.SetTag("keyframe.model", telemetry.Model);
                activity?.SetTag("keyframe.purpose", request.Purpose.ToString());
                activity?.SetTag("keyframe.input_images", request.Images.Count);
                if (request.Audio.Count > 0)
                {
                    activity?.SetTag("keyframe.input_audio_clips", request.Audio.Count);
                    activity?.SetTag("keyframe.input_audio_bytes", telemetry.InputAudioBytes);
                }

                int retries = 0;
                int repairs = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    telemetry.Attempts++;

                    DateTime attemptStart = DateTime.UtcNow;
                    Stopwatch attemptWatch = Stopwatch.StartNew();
                    InferenceResponse response = await client.CompleteAsync(request, token).ConfigureAwait(false);
                    attemptWatch.Stop();
                    Accumulate(telemetry, response);
                    if (response.Unreachable) Health?.ReportUnreachable(client.EndpointName, response.Error);
                    ModelCallAttempt attempt = ToAttempt(telemetry.Attempts, attemptStart, attemptWatch, response);
                    telemetry.AttemptDetails.Add(attempt);
                    KeyframeMetrics.RecordModelTiming(
                        request.Purpose.ToString().ToLowerInvariant(),
                        client.Provider.ToString().ToLowerInvariant(),
                        response.QueueMs,
                        response.TimeToFirstTokenMs,
                        response.GenerationMs,
                        response.TokensPerSecond);

                    if (!response.Success)
                    {
                        if (response.Retryable && retries < settings.MaxRetries)
                        {
                            retries++;
                            int delay = (int)Math.Min(60000, settings.RetryBackoffMs * Math.Pow(2, retries - 1));
                            if (delay > 0) await Task.Delay(delay, token).ConfigureAwait(false);
                            continue;
                        }

                        result.Success = false;
                        result.Error = "Endpoint '" + client.EndpointName + "' failed" +
                            (response.StatusCode.HasValue ? " with status " + response.StatusCode.Value : string.Empty) + ": " +
                            (response.Error ?? "unknown error") + ".";
                        break;
                    }

                    string text = response.Text ?? string.Empty;
                    string? invalid = validate?.Invoke(text);
                    if (invalid == null)
                    {
                        result.Success = true;
                        result.Text = text;
                        break;
                    }

                    attempt.Success = false;
                    attempt.Error = "Unusable output: " + invalid;

                    if (repairs >= settings.MaxRepairAttempts)
                    {
                        result.Success = false;
                        result.Text = text;
                        result.Error = "Model output was unusable after " + (repairs + 1) + " attempt(s): " + invalid;
                        break;
                    }

                    repairs++;
                    telemetry.RepairAttempts = repairs;
                    request.UserPrompt = PromptBuilder.Repair(originalPrompt, text, invalid);
                }

                request.UserPrompt = originalPrompt;
                sw.Stop();
                telemetry.DurationMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
                telemetry.Success = result.Success;
                telemetry.Error = result.Error;

                activity?.SetTag("keyframe.attempts", telemetry.Attempts);
                activity?.SetTag("keyframe.prompt_tokens", telemetry.PromptTokens);
                activity?.SetTag("keyframe.completion_tokens", telemetry.CompletionTokens);
                if (!result.Success) activity?.SetStatus(ActivityStatusCode.Error, result.Error);
            }

            KeyframeMetrics.RecordModelCall(
                request.Purpose.ToString().ToLowerInvariant(),
                client.Provider.ToString().ToLowerInvariant(),
                result.Success ? "success" : "failure",
                sw.Elapsed.TotalSeconds,
                telemetry.PromptTokens,
                telemetry.CompletionTokens);

            KeyframeMetrics.RecordModelInputAudio(client.Provider.ToString().ToLowerInvariant(), request.Purpose.ToString().ToLowerInvariant(), telemetry.InputAudioBytes);
            context.AddModelCall(telemetry);
            return result;
        }

        #endregion

        #region Private-Methods

        private static void Accumulate(ModelCallTelemetry telemetry, InferenceResponse response)
        {
            telemetry.StatusCode = response.StatusCode;
            telemetry.FinishReason = response.FinishReason;
            if (!String.IsNullOrEmpty(response.Model)) telemetry.Model = response.Model;
            if (response.TimeToFirstTokenMs.HasValue) telemetry.TimeToFirstTokenMs = response.TimeToFirstTokenMs;
            if (response.GenerationMs.HasValue) telemetry.GenerationMs = response.GenerationMs;
            if (response.TokensPerSecond.HasValue) telemetry.TokensPerSecond = response.TokensPerSecond;
            telemetry.QueueMs = Math.Round(telemetry.QueueMs + response.QueueMs, 3);
            telemetry.PromptTokens = Add(telemetry.PromptTokens, response.PromptTokens);
            telemetry.CompletionTokens = Add(telemetry.CompletionTokens, response.CompletionTokens);
            telemetry.TotalTokens = Add(telemetry.TotalTokens, response.TotalTokens);
            telemetry.CachedTokens = Add(telemetry.CachedTokens, response.CachedTokens);
            telemetry.ReasoningTokens = Add(telemetry.ReasoningTokens, response.ReasoningTokens);
        }

        private static ModelCallAttempt ToAttempt(int number, DateTime startUtc, Stopwatch watch, InferenceResponse response)
        {
            return new ModelCallAttempt
            {
                Attempt = number,
                StartUtc = startUtc,
                Success = response.Success,
                Error = response.Success ? null : response.Error,
                StatusCode = response.StatusCode,
                DurationMs = Math.Round(watch.Elapsed.TotalMilliseconds, 3),
                QueueMs = response.QueueMs,
                TimeToFirstTokenMs = response.TimeToFirstTokenMs,
                GenerationMs = response.GenerationMs,
                TokensPerSecond = response.TokensPerSecond,
                PromptTokens = response.PromptTokens,
                CompletionTokens = response.CompletionTokens,
                TotalTokens = response.TotalTokens,
                CachedTokens = response.CachedTokens,
                ReasoningTokens = response.ReasoningTokens,
                FinishReason = response.FinishReason
            };
        }

        private static long? Add(long? a, long? b)
        {
            if (!a.HasValue) return b;
            if (!b.HasValue) return a;
            return a.Value + b.Value;
        }

        #endregion
    }
}
