namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Media;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Analyzes audio: resolve and probe, transcribe the audio track in chunks, then summarize and extract fields
    /// from the transcript with the text model. Accepts audio files and video files (only the audio track is used).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AudioAnalyzer
    {
        #region Private-Members

        private readonly AnalysisServices _Services;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="services">Analysis services.</param>
        /// <exception cref="ArgumentNullException">Thrown when services is null.</exception>
        public AudioAnalyzer(AnalysisServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            _Services = services;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate a request and resolve every endpoint it needs, without touching media. Used to reject a job
        /// before it is queued.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when a requested or default endpoint is unavailable.</exception>
        public void Preflight(AudioAnalysisRequest request)
        {
            List<string> validation = RequestValidator.Validate(request);
            if (validation.Count > 0) throw new RequestValidationException(validation);

            KeyframeSettings settings = _Services.Settings();
            AudioFunctionsSpec functions = request.Functions;
            if (functions.Summarize.Enable || functions.Extract.Enable)
                _Services.Clients.GetCompletionClient(ResolveTextEndpoint(request, settings));
            if (functions.Transcribe.Enable)
                _Services.Clients.GetTranscriptionClient(_Services.Clients.ResolveTranscriptionEndpoint(EffectiveTranscribe(request).Endpoint));
        }

        /// <summary>
        /// Analyze audio.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="progress">Optional progress callback.</param>
        /// <param name="origin">Request origin for telemetry and analytics, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response. Function failures are reported in the response rather than thrown.</returns>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when a requested or default endpoint is unavailable.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the media cannot be downloaded or decoded.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the media has no audio stream or exceeds limits.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<AudioAnalysisResponse> AnalyzeAsync(AudioAnalysisRequest request, Action<JobProgress>? progress = null, AnalysisOrigin? origin = null, CancellationToken token = default)
        {
            List<string> validation = RequestValidator.Validate(request);
            if (validation.Count > 0) throw new RequestValidationException(validation);

            KeyframeSettings settings = _Services.Settings();
            AudioFunctionsSpec functions = request.Functions;
            TranscribeSpec transcribe = EffectiveTranscribe(request);
            bool textWork = functions.Summarize.Enable || functions.Extract.Enable;

            AnalysisContext context = new AnalysisContext(MediaKindEnum.Audio) { Progress = progress, Origin = origin };

            IInferenceClient? text = null;
            if (textWork)
            {
                EndpointSettings textEndpoint = ResolveTextEndpoint(request, settings);
                text = _Services.Clients.GetCompletionClient(textEndpoint);
                context.TextEndpoint = textEndpoint.Name;
                context.TextModel = request.TextModel?.Name ?? textEndpoint.Model;
            }

            ITranscriptionClient? stt = null;
            if (functions.Transcribe.Enable)
            {
                EndpointSettings sttEndpoint = _Services.Clients.ResolveTranscriptionEndpoint(transcribe.Endpoint);
                stt = _Services.Clients.GetTranscriptionClient(sttEndpoint);
                context.TranscriptionEndpoint = sttEndpoint.Name;
                context.TranscriptionModel = transcribe.Model ?? sttEndpoint.Model;
            }

            AudioAnalysisResponse response = new AudioAnalysisResponse { MediaKind = MediaKindEnum.Audio, RequestId = origin?.RequestId };
            Stopwatch sw = Stopwatch.StartNew();
            string outcome = "failure";
            string? exception = null;
            bool canceled = false;

            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("analysis:audio", ActivityKind.Internal))
            using (MediaWorkspace workspace = new MediaWorkspace(settings.Media.TempDirectory))
            {
                activity?.SetTag("keyframe.analysis_id", response.Id);

                try
                {
                    context.ReportProgress("Resolve", 0, 1, 1);
                    ResolvedMedia resolved;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Resolve))
                    {
                        resolved = await _Services.Resolver.ResolveAsync(request.Audio!, MediaKindEnum.Unknown, workspace, token).ConfigureAwait(false);
                    }

                    context.MediaBytes = resolved.SizeBytes;
                    if (resolved.MediaKind == MediaKindEnum.Image)
                        throw new UnsupportedMediaException("Expected audio or video media but received an image (" + resolved.ContentType + ").");

                    context.ReportProgress("Probe", 0, 1, 5);
                    MediaProperties properties;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Probe))
                    {
                        properties = await _Services.Prober.ProbeAsync(resolved.Path, token).ConfigureAwait(false);
                    }

                    context.MediaDurationMs = properties.DurationMs;
                    if (!properties.HasAudio)
                        throw new UnsupportedMediaException("The media has no audio stream (detected " + properties.MediaKind.ToString().ToLowerInvariant() + ").");
                    if (!properties.DurationMs.HasValue || properties.DurationMs.Value < 1)
                        throw new UnsupportedMediaException("The audio duration could not be determined.");
                    if (properties.DurationMs.Value > settings.Media.MaxAudioDurationMs)
                        throw new UnsupportedMediaException("The audio is " + properties.DurationMs.Value + "ms long, over the limit of " + settings.Media.MaxAudioDurationMs + "ms.");

                    bool fromVideo = properties.MediaKind == MediaKindEnum.Video;
                    if (fromVideo) context.AddWarning("The media is a video; only its audio track was analyzed.");

                    properties.SizeBytes = resolved.SizeBytes;
                    properties.Sha256 = resolved.Sha256;
                    properties.ContentType = resolved.ContentType;
                    if (request.IncludeProperties) response.Properties = properties;

                    long duration = properties.DurationMs.Value;
                    if (stt != null)
                    {
                        response.Transcript = await _Services.Transcription.TranscribeAsync(stt, resolved.Path, duration, transcribe, workspace, settings, context, 10, 70, token).ConfigureAwait(false);
                    }

                    if (text != null)
                    {
                        context.ReportProgress("Reduce", 0, 1, 75);
                        string transcriptText = response.Transcript?.Text ?? String.Empty;
                        if (String.IsNullOrWhiteSpace(transcriptText))
                        {
                            if (functions.Summarize.Enable) response.Results[functions.Summarize.Key] = null;
                            context.AddWarning("Summarize and Extract skipped: the transcript is empty.");
                        }
                        else
                        {
                            MediaSubject subject = BuildSubject(request, duration, fromVideo, transcriptText, settings);
                            await RunTextFunctionsAsync(text, request.TextModel?.Name, subject, functions, response, context, token).ConfigureAwait(false);
                        }
                    }

                    context.ReportProgress("Complete", 1, 1, 100);
                    response.Success = !context.HasErrors();
                    outcome = response.Success ? (response.Fields.TrueForAll(f => f.Valid) ? "success" : "partial") : "failure";
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    throw;
                }
                catch (Exception ex)
                {
                    exception = ex.Message;
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    KeyframeMetrics.RecordAnalysis("audio", "failure", sw.Elapsed.TotalSeconds);
                    throw;
                }
                finally
                {
                    context.CompleteInto(response);
                    _Services.NotifyCompleted(context.BuildCompletion(response, exception, canceled, 0, response.Transcript));
                }
            }

            KeyframeMetrics.RecordAnalysis("audio", outcome, sw.Elapsed.TotalSeconds);
            return response;
        }

        #endregion

        #region Private-Methods

        private EndpointSettings ResolveTextEndpoint(AudioAnalysisRequest request, KeyframeSettings settings)
        {
            return _Services.Clients.ResolveCompletionEndpoint(request.TextModel?.Endpoint, settings.Inference.DefaultText, settings.Inference.DefaultVision);
        }

        private static TranscribeSpec EffectiveTranscribe(AudioAnalysisRequest request)
        {
            TranscribeSpec spec = request.Functions.Transcribe;
            return new TranscribeSpec
            {
                Enable = spec.Enable,
                Endpoint = request.TranscriptionModel?.Endpoint ?? spec.Endpoint,
                Model = request.TranscriptionModel?.Name ?? spec.Model,
                Language = spec.Language,
                Prompt = spec.Prompt
            };
        }

        private static MediaSubject BuildSubject(AudioAnalysisRequest request, long durationMs, bool fromVideo, string transcript, KeyframeSettings settings)
        {
            string source = fromVideo ? "the audio track of a video" : "an audio recording";
            return new MediaSubject
            {
                Noun = "the recording",
                Description = "No images are attached. The evidence below is a transcript of " + source + " that is " + TimeFormat.Format(durationMs) + " long.",
                CallerContext = ContextFormatter.Render(request.Context),
                Transcript = PromptBuilder.Truncate(transcript, settings.Inference.MaxTranscriptCharacters)
            };
        }

        private async Task RunTextFunctionsAsync(
            IInferenceClient client,
            string? model,
            MediaSubject subject,
            AudioFunctionsSpec functions,
            AudioAnalysisResponse response,
            AnalysisContext context,
            CancellationToken token)
        {
            Task<SummaryOutcome>? summaryTask = null;
            Task<ExtractionOutcome>? extractTask = null;

            if (functions.Summarize.Enable)
            {
                summaryTask = Task.Run(async () =>
                {
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Summarize))
                    {
                        SummaryOutcome o = await _Services.Functions.SummarizeAsync(client, model, subject, functions.Summarize, ModelCallPurposeEnum.Summarize, null, context, token).ConfigureAwait(false);
                        if (!o.Success) stage.Fail(o.Error);
                        return o;
                    }
                }, token);
            }

            if (functions.Extract.Enable)
            {
                extractTask = Task.Run(async () =>
                {
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Extract))
                    {
                        ExtractionOutcome o = await _Services.Functions.ExtractAsync(client, model, subject, functions.Extract, ModelCallPurposeEnum.Extract, null, context, token).ConfigureAwait(false);
                        if (!o.Success) stage.Fail(String.Join(" ", o.Errors));
                        return o;
                    }
                }, token);
            }

            if (summaryTask != null)
            {
                SummaryOutcome summary = await summaryTask.ConfigureAwait(false);
                response.Results[functions.Summarize.Key] = summary.Success ? summary.Text : null;
                if (!summary.Success) context.AddError("Summarize: " + summary.Error);
            }

            if (extractTask != null)
            {
                ExtractionOutcome extraction = await extractTask.ConfigureAwait(false);
                foreach (FieldResult field in extraction.Fields)
                {
                    response.Results[field.Key] = field.Value;
                    response.Fields.Add(field);
                    foreach (string warning in field.Warnings) context.AddWarning("Field '" + field.Key + "': " + warning);
                }

                foreach (string error in extraction.Errors) context.AddError("Extract: " + error);
            }
        }

        #endregion
    }
}
