namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
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
    /// Analyzes audio: resolve and probe, transcribe the audio track in chunks, then summarize and extract fields. When
    /// listening resolves, the listening model hears the audio itself (the whole recording or evenly spaced excerpts) with
    /// the transcript as a hint; otherwise the text model works from the transcript. Accepts audio files and video files
    /// (only the audio track is used).
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
        /// <exception cref="RequestValidationException">Thrown when the request is invalid, names a listening endpoint that
        /// cannot take audio, asks for a format the listening endpoint refuses, or has nothing to analyze (Transcribe off and
        /// no usable listening endpoint).</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when a requested or default endpoint is unavailable.</exception>
        public void Preflight(AudioAnalysisRequest request)
        {
            List<string> validation = RequestValidator.Validate(request);
            if (validation.Count > 0) throw new RequestValidationException(validation);

            KeyframeSettings settings = _Services.Settings();
            AudioFunctionsSpec functions = request.Functions;
            ListenPlan plan = ResolveListening(request, settings);
            if (plan.Listens) _Services.Clients.GetCompletionClient(plan.Endpoint!);
            // A named TextModel is always checked, so a misspelled endpoint is rejected whether or not listening is healthy.
            if ((!plan.Listens && AnyModelFunction(functions)) || ReducesFromSections(request) || !String.IsNullOrWhiteSpace(request.TextModel?.Endpoint))
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
            bool modelWork = AnyModelFunction(functions);

            // Listening is resolved when the job runs, so a job that waited in the queue uses current health.
            ListenPlan plan = ResolveListening(request, settings);

            AnalysisContext context = new AnalysisContext(MediaKindEnum.Audio) { Progress = progress, Origin = origin };

            IInferenceClient? listen = null;
            if (plan.Listens)
            {
                listen = _Services.Clients.GetCompletionClient(plan.Endpoint!);
                context.VisionEndpoint = plan.Endpoint!.Name;
                context.VisionModel = plan.Model ?? plan.Endpoint.Model;
            }

            IInferenceClient? text = null;
            if ((modelWork && listen == null) || ReducesFromSections(request))
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
                    if (functions.Measure.Enable)
                    {
                        context.ReportProgress("Measure", 0, 1, 8);
                        using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Measure))
                        {
                            try
                            {
                                response.Measurements = await _Services.Measurer.MeasureAsync(resolved.Path, duration, properties.AudioChannels ?? 0, settings.Media, token).ConfigureAwait(false);
                            }
                            catch (MediaProcessingException ex)
                            {
                                stage.Fail(ex.Message);
                                context.AddError("Measure: " + ex.Message);
                            }
                        }
                    }

                    if (stt != null)
                    {
                        response.Transcript = await _Services.Transcription.TranscribeAsync(stt, resolved.Path, duration, transcribe, workspace, settings, context, 10, 70, token).ConfigureAwait(false);
                    }

                    ListeningInfo listening = new ListeningInfo { Rendition = plan.Rendition, DurationMs = duration, Reason = plan.Reason };
                    response.Listening = listening;
                    string transcriptText = response.Transcript?.Text ?? String.Empty;
                    SectionsSpec sections = functions.Sections;
                    bool recordingWork = functions.Summarize.Enable || functions.Extract.Enable;
                    bool reduce = ReducesFromSections(request);

                    if (sections.Enable)
                    {
                        context.ReportProgress("SectionDetection", 0, 1, 71);
                        List<SceneBoundary> bounds;
                        using (StageTimer stage = context.BeginStage(AnalysisStageEnum.SectionDetection))
                        {
                            bounds = await _Services.Sections.DetectAsync(resolved.Path, duration, sections.Detection, token).ConfigureAwait(false);
                        }

                        response.Sections = bounds.Select((b, i) => new SectionResult
                        {
                            Index = i,
                            StartMs = b.StartMs,
                            EndMs = b.EndMs,
                            DurationMs = b.DurationMs,
                            TranscriptText = response.Transcript == null ? null : TranscriptSlicer.Slice(response.Transcript, b.StartMs, b.EndMs)
                        }).ToList();
                    }

                    if (listen != null)
                    {
                        listening.Listened = true;
                        listening.Endpoint = plan.Endpoint!.Name;
                        listening.Model = plan.Model ?? plan.Endpoint.Model;
                        listening.Mode = ListeningModeEnum.Sections;

                        ListenSelection? selection = null;
                        if (recordingWork && !reduce)
                        {
                            context.ReportProgress("Listen", 0, 1, 72);
                            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.ListenExtraction))
                            {
                                selection = await SelectRecordingClipsAsync(resolved.Path, duration, request.Listen, plan, settings, workspace, context, token).ConfigureAwait(false);
                            }

                            RecordClips(listening, selection.Clips, ListenClipScopeEnum.Recording, null);
                            listening.Mode = selection.Mode;
                        }

                        if (response.Sections != null && SectionWork(sections))
                            await RunSectionsAsync(listen, plan, request, response, resolved.Path, workspace, settings, context, token).ConfigureAwait(false);

                        if (selection != null)
                        {
                            context.ReportProgress("Reduce", 0, 1, 90);
                            MediaSubject subject = BuildListeningSubject(request, duration, fromVideo, transcriptText, selection, settings);
                            subject.Notes = MeasurementNotes(response.Measurements);
                            await RunTextFunctionsAsync(listen, plan.Model, subject, functions, response, context, token).ConfigureAwait(false);
                        }
                        else if (reduce)
                        {
                            context.ReportProgress("Reduce", 0, 1, 90);
                            await RunReduceAsync(text!, request, response, duration, fromVideo, transcriptText, settings, context, token).ConfigureAwait(false);
                        }

                        listening.HeardMs = HeardMs(listening.Clips);
                    }
                    else if (modelWork)
                    {
                        if (plan.IsFallback) context.AddWarning(FallbackWarning(plan));
                        if (!functions.Transcribe.Enable)
                        {
                            if (functions.Summarize.Enable) response.Results[functions.Summarize.Key] = null;
                            context.AddError("Nothing to analyze: no usable audio-capable endpoint and Transcribe is off.");
                        }
                        else if (String.IsNullOrWhiteSpace(transcriptText))
                        {
                            if (functions.Summarize.Enable) response.Results[functions.Summarize.Key] = null;
                            context.AddWarning("Summarize and Extract skipped: the transcript is empty.");
                        }
                        else
                        {
                            if (response.Sections != null && SectionWork(sections))
                                await RunSectionsAsync(text!, null, request, response, resolved.Path, workspace, settings, context, token).ConfigureAwait(false);

                            context.ReportProgress("Reduce", 0, 1, 90);
                            if (reduce)
                            {
                                await RunReduceAsync(text!, request, response, duration, fromVideo, transcriptText, settings, context, token).ConfigureAwait(false);
                            }
                            else if (recordingWork)
                            {
                                MediaSubject subject = BuildSubject(request, duration, fromVideo, transcriptText, settings);
                                subject.Notes = MeasurementNotes(response.Measurements);
                                await RunTextFunctionsAsync(text!, request.TextModel?.Name, subject, functions, response, context, token).ConfigureAwait(false);
                            }
                        }
                    }

                    KeyframeMetrics.RecordListen("audio", listening.Mode.ToString().ToLowerInvariant(), listening.Reason.HasValue ? listening.Reason.Value.ToString() : "none");

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

        /// <summary>
        /// Render measurements as a one-line note for prompts, or null when there are none. Measurements are evidence
        /// (placed in the notes tag), never instructions.
        /// </summary>
        /// <param name="m">Measurements, or null.</param>
        /// <returns>Note text, or null.</returns>
        public static string? MeasurementNotes(AudioMeasurements? m)
        {
            if (m == null) return null;
            List<string> parts = new List<string>();
            if (m.IntegratedLufs.HasValue) parts.Add("integrated loudness " + Fmt(m.IntegratedLufs.Value) + " LUFS");
            if (m.LoudnessRangeLu.HasValue) parts.Add("loudness range " + Fmt(m.LoudnessRangeLu.Value) + " LU");
            if (m.TruePeakDbtp.HasValue) parts.Add("true peak " + Fmt(m.TruePeakDbtp.Value) + " dBTP");
            if (m.CrestFactorDb.HasValue) parts.Add("crest factor " + Fmt(m.CrestFactorDb.Value) + " dB");
            parts.Add(Math.Round(m.SilenceRatio * 100).ToString(System.Globalization.CultureInfo.InvariantCulture) + "% silence");
            parts.Add(m.Channels == 1 ? "mono" : m.EffectivelyMono ? "stereo that is effectively mono" : m.Channels == 2 ? "stereo" : m.Channels + " channels");
            if (m.ClippingLikely) parts.Add("clipping likely");
            return "Measured by signal analysis (exact values, not opinions): " + String.Join(", ", parts) + ".";
        }

        #endregion

        #region Private-Methods

        private ListenPlan ResolveListening(AudioAnalysisRequest request, KeyframeSettings settings)
        {
            AudioFunctionsSpec functions = request.Functions;
            ListenPlan plan = ListenPlanner.Resolve(request.Listen, AnyModelFunction(functions), settings, _Services.Health);
            if (plan.Rejection != null) throw new RequestValidationException(new List<string> { plan.Rejection });
            if (!plan.Listens && AnyModelFunction(functions) && !functions.Transcribe.Enable)
                throw new RequestValidationException(new List<string> { "Nothing to analyze: no usable audio-capable endpoint and Transcribe is off." });
            return plan;
        }

        private static string Fmt(double value)
        {
            return value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static bool AnyModelFunction(AudioFunctionsSpec functions)
        {
            return functions.Summarize.Enable || functions.Extract.Enable || SectionWork(functions.Sections);
        }

        private static bool SectionWork(SectionsSpec sections)
        {
            return sections.Enable && (sections.Summarize.Enable || sections.Extract.Enable);
        }

        private static bool ReducesFromSections(AudioAnalysisRequest request)
        {
            AudioFunctionsSpec functions = request.Functions;
            return request.TextModel != null && SectionWork(functions.Sections) && (functions.Summarize.Enable || functions.Extract.Enable);
        }

        private async Task RunSectionsAsync(
            IInferenceClient client,
            ListenPlan? plan,
            AudioAnalysisRequest request,
            AudioAnalysisResponse response,
            string path,
            MediaWorkspace workspace,
            KeyframeSettings settings,
            AnalysisContext context,
            CancellationToken token)
        {
            SectionsSpec spec = request.Functions.Sections;
            List<SectionResult> sections = response.Sections!;
            bool listening = plan != null && plan.Listens;
            string? model = listening ? plan!.Model : request.TextModel?.Name;
            long budgetMs = listening ? ListenPlanner.BudgetMs(plan!.Endpoint!, settings.Media) : 0;

            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Sections))
            using (SemaphoreSlim renders = new SemaphoreSlim(Math.Max(1, settings.Media.MaxParallelFfmpeg)))
            {
                int completed = 0;
                context.ReportProgress("Sections", 0, sections.Count, 76);

                List<Task> tasks = sections.Select(section => Task.Run(async () =>
                {
                    string range = TimeFormat.Format(section.StartMs) + " to " + TimeFormat.Format(section.EndMs);
                    MediaSubject subject;
                    if (listening)
                    {
                        InferenceAudio clip;
                        await renders.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            string label = "Section " + (section.Index + 1) + " (" + TimeFormat.Clock(section.StartMs) + " to " + TimeFormat.Clock(section.EndMs) + ")";
                            clip = await _Services.Listen.RenderFittedAsync(path, section.StartMs, section.EndMs, budgetMs, plan!.Endpoint!.MaxInlineMediaBytes, plan.Rendition, workspace, label, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            renders.Release();
                        }

                        lock (response.Listening)
                        {
                            RecordClips(response.Listening, new List<InferenceAudio> { clip }, ListenClipScopeEnum.Section, section.Index);
                        }

                        bool whole = clip.StartMs <= section.StartMs && clip.EndMs >= section.EndMs;
                        subject = new MediaSubject
                        {
                            Noun = "the section",
                            Description = "No images are attached. " + (whole
                                ? "One section of an audio recording, covering " + range + ", is attached as audio."
                                : "The middle " + TimeFormat.Span(clip.EndMs - clip.StartMs) + " of one section of an audio recording, covering " + range + ", is attached as audio.") +
                                (String.IsNullOrWhiteSpace(section.TranscriptText)
                                    ? " No usable transcript is available for it, so rely on the audio alone."
                                    : " A machine transcript of the section follows as a hint. It can be wrong; when it disagrees with the audio, trust the audio."),
                            CallerContext = ContextFormatter.Render(request.Context),
                            Transcript = String.IsNullOrWhiteSpace(section.TranscriptText) ? null : section.TranscriptText,
                            Audio = new List<InferenceAudio> { clip }
                        };
                    }
                    else
                    {
                        if (String.IsNullOrWhiteSpace(section.TranscriptText))
                        {
                            if (spec.Summarize.Enable) section.Results[spec.Summarize.Key] = null;
                            lock (context) { context.AddWarning("Section " + section.Index + ": skipped, no transcript text in " + range + "."); }
                            return;
                        }

                        subject = new MediaSubject
                        {
                            Noun = "the section",
                            Description = "No audio is attached. The evidence below is a transcript of one section of an audio recording, covering " + range + ".",
                            CallerContext = ContextFormatter.Render(request.Context),
                            Transcript = section.TranscriptText
                        };
                    }

                    string target = "section:" + section.Index;
                    Task<SummaryOutcome>? summaryTask = spec.Summarize.Enable
                        ? _Services.Functions.SummarizeAsync(client, model, subject, spec.Summarize, ModelCallPurposeEnum.SectionSummarize, target, context, token)
                        : null;
                    Task<ExtractionOutcome>? extractTask = spec.Extract.Enable
                        ? _Services.Functions.ExtractAsync(client, model, subject, spec.Extract, ModelCallPurposeEnum.SectionExtract, target, context, token)
                        : null;

                    if (summaryTask != null)
                    {
                        SummaryOutcome summary = await summaryTask.ConfigureAwait(false);
                        section.Results[spec.Summarize.Key] = summary.Success ? summary.Text : null;
                        if (!summary.Success) section.Errors.Add("Summarize: " + summary.Error);
                    }

                    if (extractTask != null)
                    {
                        ExtractionOutcome extraction = await extractTask.ConfigureAwait(false);
                        foreach (FieldResult field in extraction.Fields)
                        {
                            section.Results[field.Key] = field.Value;
                            section.Fields.Add(field);
                        }

                        foreach (string error in extraction.Errors) section.Errors.Add("Extract: " + error);
                    }

                    foreach (string error in section.Errors) context.AddError("Section " + section.Index + ": " + error);
                    int done = Interlocked.Increment(ref completed);
                    context.ReportProgress("Sections", done, sections.Count, 76 + 14.0 * done / sections.Count);
                }, token)).ToList();

                await Task.WhenAll(tasks).ConfigureAwait(false);
                if (sections.Any(s => s.Errors.Count > 0)) stage.Fail("section errors");
            }

            if (listening)
            {
                lock (response.Listening)
                {
                    response.Listening.Clips = response.Listening.Clips.OrderBy(c => c.Scope).ThenBy(c => c.Index ?? -1).ThenBy(c => c.StartMs).ToList();
                }
            }
        }

        private async Task RunReduceAsync(
            IInferenceClient text,
            AudioAnalysisRequest request,
            AudioAnalysisResponse response,
            long durationMs,
            bool fromVideo,
            string transcript,
            KeyframeSettings settings,
            AnalysisContext context,
            CancellationToken token)
        {
            SectionsSpec spec = request.Functions.Sections;
            System.Text.StringBuilder notes = new System.Text.StringBuilder();
            foreach (SectionResult section in response.Sections ?? new List<SectionResult>())
            {
                List<string> parts = new List<string>();
                if (section.Results.TryGetValue(spec.Summarize.Key, out object? value) && value is string summary && !String.IsNullOrWhiteSpace(summary)) parts.Add(summary.Trim());
                foreach (FieldResult field in section.Fields.Where(f => f.Valid))
                    parts.Add(field.Key + " = " + Keyframe.Core.Serialization.KeyframeSerializer.Serialize(field.Value, false));
                if (parts.Count == 0) continue;
                notes.Append("Section ").Append(section.Index + 1).Append(" (").Append(TimeFormat.Format(section.StartMs)).Append(" to ")
                     .Append(TimeFormat.Format(section.EndMs)).Append("): ").Append(String.Join("; ", parts)).Append('\n');
            }

            string source = fromVideo ? "the audio track of a video" : "an audio recording";
            string? measured = MeasurementNotes(response.Measurements);
            MediaSubject subject = new MediaSubject
            {
                Noun = "the recording",
                Description = "No audio is attached. The evidence below describes the " + (response.Sections?.Count ?? 0) + " sections, in order, of " + source +
                    " that is " + TimeFormat.Span(durationMs) + " long.",
                CallerContext = ContextFormatter.Render(request.Context),
                Transcript = String.IsNullOrWhiteSpace(transcript) ? null : PromptBuilder.Truncate(transcript, settings.Inference.MaxTranscriptCharacters),
                Notes = (notes.Length > 0 ? "Section results:\n" + notes.ToString() : "No section produced a result.") + (measured != null ? "\n" + measured : String.Empty)
            };

            await RunTextFunctionsAsync(text, request.TextModel?.Name, subject, request.Functions, response, context, token).ConfigureAwait(false);
        }

        private static string FallbackWarning(ListenPlan plan)
        {
            string name = plan.Endpoint?.Name ?? "(none)";
            switch (plan.Reason)
            {
                case ListenSkipReasonEnum.EndpointUnhealthy:
                    return "The listening endpoint '" + name + "' is unhealthy; results come from the transcript only.";
                case ListenSkipReasonEnum.EndpointLacksAudioInput:
                    return "The listening endpoint '" + name + "' (" + plan.Endpoint?.Provider + ") cannot take audio input; results come from the transcript only.";
                case ListenSkipReasonEnum.NoAudioTrack:
                    return "The media has no audio track to listen to; results come from the transcript only.";
                default:
                    return "No listening endpoint is configured (Listen.Model or Inference.DefaultListening); results come from the transcript only.";
            }
        }

        private async Task<ListenSelection> SelectRecordingClipsAsync(
            string path,
            long durationMs,
            ListenSpec listen,
            ListenPlan plan,
            KeyframeSettings settings,
            MediaWorkspace workspace,
            AnalysisContext context,
            CancellationToken token)
        {
            EndpointSettings endpoint = plan.Endpoint!;
            AudioRenditionSpec rendition = plan.Rendition;
            long budgetMs = ListenPlanner.BudgetMs(endpoint, settings.Media);
            long maxWire = endpoint.MaxInlineMediaBytes;

            if (durationMs <= budgetMs && RenditionResolver.WireBytes(RenditionResolver.EstimateBytes(rendition, durationMs)) <= maxWire)
            {
                InferenceAudio full = await _Services.Listen.RenderAsync(path, new ClipWindow(0, durationMs), rendition, workspace,
                    "Full recording (" + TimeFormat.Span(durationMs) + ")", token).ConfigureAwait(false);
                if (RenditionResolver.WireBytes(full.Data.Length) <= maxWire)
                    return new ListenSelection { Mode = ListeningModeEnum.Full, Clips = new List<InferenceAudio> { full } };
            }

            int count = listen.SampleClips;
            long clipMs = listen.ClipSeconds * 1000L;
            if (count * clipMs > budgetMs)
            {
                clipMs = Math.Max(1000L, budgetMs / count);
                context.AddWarning("Excerpts were shortened to " + TimeFormat.Span(clipMs) + " each to fit the listen budget of " + TimeFormat.Span(budgetMs) + ".");
            }

            long estimate = RenditionResolver.WireBytes(RenditionResolver.EstimateBytes(rendition, clipMs)) * count;
            if (estimate > maxWire)
            {
                clipMs = Math.Max(1000L, (long)(clipMs * (maxWire * 0.95 / estimate)));
                context.AddWarning("Excerpts were shortened to " + TimeFormat.Span(clipMs) + " each to fit the endpoint's inline media limit.");
            }

            List<InferenceAudio> clips = await RenderWindowsAsync(path, ClipSampler.Evenly(durationMs, count, clipMs), rendition, workspace, token).ConfigureAwait(false);
            long wire = clips.Sum(c => RenditionResolver.WireBytes(c.Data.Length));
            if (wire > maxWire)
            {
                clipMs = Math.Max(1000L, (long)(clipMs * (maxWire * 0.9 / wire)));
                context.AddWarning("Excerpts were shortened to " + TimeFormat.Span(clipMs) + " each to fit the endpoint's inline media limit.");
                clips = await RenderWindowsAsync(path, ClipSampler.Evenly(durationMs, count, clipMs), rendition, workspace, token).ConfigureAwait(false);
            }

            ListenSelection selection = new ListenSelection { Mode = ListeningModeEnum.Excerpts, Clips = clips };
            if (clips.Count == 1 && clips[0].StartMs == 0 && clips[0].EndMs >= durationMs)
            {
                selection.Mode = ListeningModeEnum.Full;
                return selection;
            }

            context.AddWarning("Listened to " + clips.Count + (clips.Count == 1 ? " excerpt (" : " excerpts (") + TimeFormat.Span(clips.Sum(c => c.EndMs - c.StartMs)) +
                ") of a " + TimeFormat.Span(durationMs) + " recording.");
            return selection;
        }

        private async Task<List<InferenceAudio>> RenderWindowsAsync(string path, List<ClipWindow> windows, AudioRenditionSpec rendition, MediaWorkspace workspace, CancellationToken token)
        {
            List<InferenceAudio> clips = new List<InferenceAudio>();
            foreach (ClipWindow window in windows)
            {
                token.ThrowIfCancellationRequested();
                string label = "Excerpt at " + TimeFormat.Clock(window.StartMs) + " (" + TimeFormat.Span(window.DurationMs) + ")";
                clips.Add(await _Services.Listen.RenderAsync(path, window, rendition, workspace, label, token).ConfigureAwait(false));
            }

            return clips;
        }

        private static void RecordClips(ListeningInfo listening, List<InferenceAudio> clips, ListenClipScopeEnum scope, int? index)
        {
            foreach (InferenceAudio clip in clips)
            {
                listening.Clips.Add(new ListenClip { Scope = scope, Index = index, StartMs = clip.StartMs, EndMs = clip.EndMs, SizeBytes = clip.Data.Length });
            }
        }

        private static long HeardMs(List<ListenClip> clips)
        {
            long heard = 0;
            long coveredTo = -1;
            foreach (ListenClip clip in clips.OrderBy(c => c.StartMs))
            {
                long start = Math.Max(clip.StartMs, coveredTo);
                if (clip.EndMs > start) heard += clip.EndMs - start;
                coveredTo = Math.Max(coveredTo, clip.EndMs);
            }

            return heard;
        }

        private static MediaSubject BuildListeningSubject(AudioAnalysisRequest request, long durationMs, bool fromVideo, string transcript, ListenSelection selection, KeyframeSettings settings)
        {
            string source = fromVideo ? "the audio track of a video" : "an audio recording";
            string heard = selection.Mode == ListeningModeEnum.Full
                ? "The whole of " + source + " (" + TimeFormat.Span(durationMs) + " long) is attached as audio."
                : selection.Clips.Count + " evenly spaced excerpts of " + source + " that is " + TimeFormat.Span(durationMs) + " long are attached as audio, each labeled with its offset. " +
                  "Base your answers on what you hear in them and say when something cannot be judged from excerpts.";
            string hint = String.IsNullOrWhiteSpace(transcript)
                ? " No usable transcript is available, so rely on the audio alone."
                : " A machine transcript follows as a hint. It can be wrong, especially for sung vocals; when it disagrees with the audio, trust the audio.";
            return new MediaSubject
            {
                Noun = "the recording",
                Description = "No images are attached. " + heard + hint,
                CallerContext = ContextFormatter.Render(request.Context),
                Transcript = String.IsNullOrWhiteSpace(transcript) ? null : PromptBuilder.Truncate(transcript, settings.Inference.MaxTranscriptCharacters),
                Audio = selection.Clips
            };
        }

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
