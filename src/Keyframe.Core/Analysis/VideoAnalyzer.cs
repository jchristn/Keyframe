namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Linq;
    using System.Text;
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
    /// Analyzes one video: resolve and probe, optionally transcribe, detect scenes, extract keyframes, analyze each
    /// scene, then answer whole-video functions from sampled frames plus scene summaries and the transcript.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class VideoAnalyzer
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
        public VideoAnalyzer(AnalysisServices services)
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
        public void Preflight(VideoAnalysisRequest request)
        {
            List<string> validation = RequestValidator.Validate(request);
            if (validation.Count > 0) throw new RequestValidationException(validation);

            KeyframeSettings settings = _Services.Settings();
            VideoFunctionsSpec functions = request.Functions;
            bool videoLevel = functions.Summarize.Enable || functions.Extract.Enable;
            bool sceneWork = functions.Scenes.Enable && (functions.Scenes.Summarize.Enable || functions.Scenes.Extract.Enable);
            bool textOnly = request.TextModel != null;

            if (sceneWork || (videoLevel && !textOnly))
                _Services.Clients.GetCompletionClient(_Services.Clients.ResolveCompletionEndpoint(request.Model?.Endpoint, settings.Inference.DefaultVision));
            if (videoLevel && textOnly)
                _Services.Clients.GetCompletionClient(_Services.Clients.ResolveCompletionEndpoint(request.TextModel!.Endpoint, settings.Inference.DefaultText, settings.Inference.DefaultVision));
            if (functions.Transcribe.Enable)
                _Services.Clients.GetTranscriptionClient(_Services.Clients.ResolveTranscriptionEndpoint(functions.Transcribe.Endpoint));
        }

        /// <summary>
        /// Analyze a video.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="progress">Optional progress callback.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response. Function failures are reported in the response rather than thrown.</returns>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when a requested or default endpoint is unavailable.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the video cannot be downloaded or decoded.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the media is not a video or exceeds limits.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public Task<VideoAnalysisResponse> AnalyzeAsync(VideoAnalysisRequest request, Action<JobProgress>? progress = null, CancellationToken token = default)
        {
            return AnalyzeAsync(request, progress, null, token);
        }

        /// <summary>
        /// Analyze a video, recording where the request came from.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="progress">Optional progress callback.</param>
        /// <param name="origin">Request origin for telemetry and analytics, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response. Function failures are reported in the response rather than thrown.</returns>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when a requested or default endpoint is unavailable.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the video cannot be downloaded or decoded.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the media is not a video or exceeds limits.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<VideoAnalysisResponse> AnalyzeAsync(VideoAnalysisRequest request, Action<JobProgress>? progress, AnalysisOrigin? origin, CancellationToken token = default)
        {
            List<string> validation = RequestValidator.Validate(request);
            if (validation.Count > 0) throw new RequestValidationException(validation);

            KeyframeSettings settings = _Services.Settings();
            VideoFunctionsSpec functions = request.Functions;
            bool videoLevel = functions.Summarize.Enable || functions.Extract.Enable;
            bool sceneWork = functions.Scenes.Enable && (functions.Scenes.Summarize.Enable || functions.Scenes.Extract.Enable);
            bool textOnly = request.TextModel != null;

            AnalysisContext context = new AnalysisContext(MediaKindEnum.Video) { Progress = progress, Origin = origin };

            IInferenceClient? vision = null;
            if (sceneWork || (videoLevel && !textOnly))
            {
                EndpointSettings visionEndpoint = _Services.Clients.ResolveCompletionEndpoint(request.Model?.Endpoint, settings.Inference.DefaultVision);
                vision = _Services.Clients.GetCompletionClient(visionEndpoint);
                context.VisionEndpoint = visionEndpoint.Name;
                context.VisionModel = request.Model?.Name ?? visionEndpoint.Model;
            }

            IInferenceClient? text = null;
            if (videoLevel && textOnly)
            {
                EndpointSettings textEndpoint = _Services.Clients.ResolveCompletionEndpoint(request.TextModel!.Endpoint, settings.Inference.DefaultText, settings.Inference.DefaultVision);
                text = _Services.Clients.GetCompletionClient(textEndpoint);
                context.TextEndpoint = textEndpoint.Name;
                context.TextModel = request.TextModel!.Name ?? textEndpoint.Model;
            }

            ITranscriptionClient? stt = null;
            if (functions.Transcribe.Enable)
            {
                EndpointSettings sttEndpoint = _Services.Clients.ResolveTranscriptionEndpoint(functions.Transcribe.Endpoint);
                stt = _Services.Clients.GetTranscriptionClient(sttEndpoint);
                context.TranscriptionEndpoint = sttEndpoint.Name;
                context.TranscriptionModel = functions.Transcribe.Model ?? sttEndpoint.Model;
            }

            VideoAnalysisResponse response = new VideoAnalysisResponse { MediaKind = MediaKindEnum.Video, RequestId = origin?.RequestId };
            Stopwatch sw = Stopwatch.StartNew();
            string outcome = "failure";
            string? exception = null;
            bool canceled = false;

            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("analysis:video", ActivityKind.Internal))
            using (MediaWorkspace workspace = new MediaWorkspace(settings.Media.TempDirectory))
            {
                activity?.SetTag("keyframe.analysis_id", response.Id);

                try
                {
                    context.ReportProgress("Resolve", 0, 1, 1);
                    ResolvedMedia resolved;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Resolve))
                    {
                        resolved = await _Services.Resolver.ResolveAsync(request.Video!, MediaKindEnum.Video, workspace, token).ConfigureAwait(false);
                    }

                    context.ReportProgress("Probe", 0, 1, 5);
                    MediaProperties properties;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Probe))
                    {
                        properties = await _Services.Prober.ProbeAsync(resolved.Path, token).ConfigureAwait(false);
                    }

                    context.MediaBytes = resolved.SizeBytes;
                    context.MediaDurationMs = properties.DurationMs;
                    if (properties.MediaKind != MediaKindEnum.Video)
                        throw new UnsupportedMediaException("Expected a video but the media is " + properties.MediaKind.ToString().ToLowerInvariant() + ".");
                    if (!properties.DurationMs.HasValue || properties.DurationMs.Value < 1)
                        throw new UnsupportedMediaException("The video duration could not be determined.");
                    if (properties.DurationMs.Value > settings.Media.MaxVideoDurationMs)
                        throw new UnsupportedMediaException("The video is " + properties.DurationMs.Value + "ms long, over the limit of " + settings.Media.MaxVideoDurationMs + "ms.");

                    properties.SizeBytes = resolved.SizeBytes;
                    properties.Sha256 = resolved.Sha256;
                    properties.ContentType = resolved.ContentType;
                    if (request.IncludeProperties) response.Properties = properties;

                    long duration = properties.DurationMs.Value;
                    long maxTimestamp = Math.Max(0, duration - 100);

                    if (stt != null)
                    {
                        if (properties.HasAudio)
                        {
                            response.Transcript = await _Services.Transcription.TranscribeAsync(stt, resolved.Path, duration, functions.Transcribe, workspace, settings, context, 10, 25, token).ConfigureAwait(false);
                        }
                        else
                        {
                            context.AddWarning("Transcription skipped: the video has no audio stream.");
                        }
                    }

                    List<SceneBoundary> boundaries = new List<SceneBoundary>();
                    if (functions.Scenes.Enable)
                    {
                        context.ReportProgress("SceneDetection", 0, 1, 30);
                        using (StageTimer stage = context.BeginStage(AnalysisStageEnum.SceneDetection))
                        {
                            boundaries = await _Services.Scenes.DetectAsync(resolved.Path, duration, functions.Scenes.Detection, token).ConfigureAwait(false);
                        }
                    }

                    Dictionary<long, NormalizedImage> frames = new Dictionary<long, NormalizedImage>();
                    List<List<long>> sceneStamps = boundaries
                        .Select(b => sceneWork ? FrameSampler.Evenly(b.StartMs, b.EndMs, functions.Scenes.FramesPerScene, maxTimestamp) : new List<long>())
                        .ToList();

                    List<long> videoStamps = new List<long>();
                    if (videoLevel && !textOnly)
                    {
                        List<long> all = sceneStamps.SelectMany(s => s).Distinct().OrderBy(t => t).ToList();
                        videoStamps = all.Count > 0 ? PickEvenly(all, request.SampleFrames) : FrameSampler.Evenly(0, duration, request.SampleFrames, maxTimestamp);
                    }

                    List<long> needed = sceneStamps.SelectMany(s => s).Concat(videoStamps).Distinct().OrderBy(t => t).ToList();
                    if (needed.Count > 0)
                    {
                        context.ReportProgress("FrameExtraction", 0, needed.Count, 35);
                        using (StageTimer stage = context.BeginStage(AnalysisStageEnum.FrameExtraction))
                        {
                            string dir = workspace.CreateSubdirectory("frames");
                            List<NormalizedImage> extracted = await _Services.Frames.ExtractAsync(
                                resolved.Path, needed, dir, settings.Media.ImageMaxEdge, settings.Media.JpegQuality, settings.Media.MaxParallelFfmpeg, token).ConfigureAwait(false);
                            foreach (NormalizedImage frame in extracted) frames[frame.TimestampMs ?? 0] = frame;
                        }
                    }

                    Dictionary<long, string> thumbnails = new Dictionary<long, string>();
                    if (request.IncludeKeyframeData && functions.Scenes.Enable)
                        thumbnails = await BuildThumbnailsAsync(sceneStamps.SelectMany(s => s).Distinct().ToList(), frames, workspace, settings, token).ConfigureAwait(false);

                    if (functions.Scenes.Enable)
                    {
                        response.Scenes = await AnalyzeScenesAsync(vision, request, boundaries, sceneStamps, frames, thumbnails, response.Transcript, context, token).ConfigureAwait(false);
                    }

                    if (videoLevel)
                    {
                        context.ReportProgress("Reduce", 0, 1, 92);
                        IInferenceClient client = textOnly ? text! : vision!;
                        string? model = textOnly ? request.TextModel!.Name : request.Model?.Name;
                        MediaSubject subject = BuildVideoSubject(request, properties, response, videoStamps, frames, settings);
                        await RunVideoFunctionsAsync(client, model, subject, functions, response, context, token).ConfigureAwait(false);
                    }

                    context.ReportProgress("Complete", 1, 1, 100);
                    response.Success = !context.HasErrors();
                    bool allValid = response.Fields.TrueForAll(f => f.Valid) && response.Scenes.TrueForAll(s => s.Fields.TrueForAll(f => f.Valid));
                    outcome = response.Success ? (allValid ? "success" : "partial") : "failure";
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
                    KeyframeMetrics.RecordAnalysis("video", "failure", sw.Elapsed.TotalSeconds);
                    throw;
                }
                finally
                {
                    context.CompleteInto(response);
                    _Services.NotifyCompleted(context.BuildCompletion(response, exception, canceled, response.Scenes.Count, response.Transcript));
                }
            }

            KeyframeMetrics.RecordAnalysis("video", outcome, sw.Elapsed.TotalSeconds);
            return response;
        }

        #endregion

        #region Private-Methods

        private async Task<List<SceneResult>> AnalyzeScenesAsync(
            IInferenceClient? vision,
            VideoAnalysisRequest request,
            List<SceneBoundary> boundaries,
            List<List<long>> sceneStamps,
            Dictionary<long, NormalizedImage> frames,
            Dictionary<long, string> thumbnails,
            TranscriptResult? transcript,
            AnalysisContext context,
            CancellationToken token)
        {
            ScenesSpec spec = request.Functions.Scenes;
            List<SceneResult> scenes = new List<SceneResult>();
            for (int i = 0; i < boundaries.Count; i++)
            {
                SceneResult scene = new SceneResult
                {
                    Index = i,
                    StartMs = boundaries[i].StartMs,
                    EndMs = boundaries[i].EndMs,
                    DurationMs = boundaries[i].DurationMs,
                    TranscriptText = transcript == null ? null : TranscriptSlice(transcript, boundaries[i].StartMs, boundaries[i].EndMs)
                };

                foreach (long t in sceneStamps[i])
                {
                    if (!frames.TryGetValue(t, out NormalizedImage? frame)) continue;
                    scene.Keyframes.Add(new KeyframeInfo
                    {
                        TimestampMs = t,
                        Sha256 = frame.Sha256,
                        Width = frame.Width,
                        Height = frame.Height,
                        Data = thumbnails.TryGetValue(t, out string? thumb) ? thumb : null
                    });
                }

                scenes.Add(scene);
            }

            if (vision == null || (!spec.Summarize.Enable && !spec.Extract.Enable)) return scenes;

            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Scenes))
            {
                int completed = 0;
                context.ReportProgress("Scenes", 0, scenes.Count, 40);

                List<Task> tasks = scenes.Select(scene => Task.Run(async () =>
                {
                    MediaSubject subject = new MediaSubject
                    {
                        Noun = "the scene",
                        Description = "These are " + scene.Keyframes.Count + " frames, in order, from one scene of a video, covering " +
                            TimeFormat.Format(scene.StartMs) + " to " + TimeFormat.Format(scene.EndMs) + ".",
                        CallerContext = ContextFormatter.Render(request.Context),
                        Transcript = scene.TranscriptText,
                        Images = scene.Keyframes
                            .Where(k => frames.ContainsKey(k.TimestampMs))
                            .Select(k => new InferenceImage
                            {
                                Data = frames[k.TimestampMs].Data,
                                MediaType = "image/jpeg",
                                Sha256 = k.Sha256,
                                Label = "Frame at " + TimeFormat.Format(k.TimestampMs)
                            })
                            .ToList()
                    };

                    string target = "scene:" + scene.Index;
                    Task<SummaryOutcome>? summaryTask = spec.Summarize.Enable
                        ? _Services.Functions.SummarizeAsync(vision, request.Model?.Name, subject, spec.Summarize, ModelCallPurposeEnum.SceneSummarize, target, context, token)
                        : null;
                    Task<ExtractionOutcome>? extractTask = spec.Extract.Enable
                        ? _Services.Functions.ExtractAsync(vision, request.Model?.Name, subject, spec.Extract, ModelCallPurposeEnum.SceneExtract, target, context, token)
                        : null;

                    if (summaryTask != null)
                    {
                        SummaryOutcome summary = await summaryTask.ConfigureAwait(false);
                        scene.Results[spec.Summarize.Key] = summary.Success ? summary.Text : null;
                        if (!summary.Success) scene.Errors.Add("Summarize: " + summary.Error);
                    }

                    if (extractTask != null)
                    {
                        ExtractionOutcome extraction = await extractTask.ConfigureAwait(false);
                        foreach (FieldResult field in extraction.Fields)
                        {
                            scene.Results[field.Key] = field.Value;
                            scene.Fields.Add(field);
                        }

                        foreach (string error in extraction.Errors) scene.Errors.Add("Extract: " + error);
                    }

                    foreach (string error in scene.Errors) context.AddError("Scene " + scene.Index + ": " + error);

                    int done = Interlocked.Increment(ref completed);
                    context.ReportProgress("Scenes", done, scenes.Count, 40 + 50.0 * done / scenes.Count);
                }, token)).ToList();

                await Task.WhenAll(tasks).ConfigureAwait(false);
                if (scenes.Any(s => s.Errors.Count > 0)) stage.Fail("scene errors");
            }

            return scenes;
        }

        private MediaSubject BuildVideoSubject(
            VideoAnalysisRequest request,
            MediaProperties properties,
            VideoAnalysisResponse response,
            List<long> videoStamps,
            Dictionary<long, NormalizedImage> frames,
            KeyframeSettings settings)
        {
            List<InferenceImage> images = videoStamps
                .Where(t => frames.ContainsKey(t))
                .Select(t => new InferenceImage
                {
                    Data = frames[t].Data,
                    MediaType = "image/jpeg",
                    Sha256 = frames[t].Sha256,
                    Label = "Frame at " + TimeFormat.Format(t)
                })
                .ToList();

            string duration = TimeFormat.Format(properties.DurationMs ?? 0);
            string description = images.Count > 0
                ? "These are " + images.Count + " frames sampled in order from a video that is " + duration + " long."
                : "No frames are attached. The evidence below describes a video that is " + duration + " long.";

            StringBuilder notes = new StringBuilder();
            string summaryKey = request.Functions.Scenes.Summarize.Key;
            foreach (SceneResult scene in response.Scenes)
            {
                if (!scene.Results.TryGetValue(summaryKey, out object? value) || value is not string summary || String.IsNullOrWhiteSpace(summary)) continue;
                notes.Append("Scene ").Append(scene.Index + 1).Append(" (").Append(TimeFormat.Format(scene.StartMs)).Append(" to ")
                     .Append(TimeFormat.Format(scene.EndMs)).Append("): ").Append(summary.Trim()).Append('\n');
            }

            return new MediaSubject
            {
                Noun = "the video",
                Description = description,
                CallerContext = ContextFormatter.Render(request.Context),
                Transcript = response.Transcript == null ? null : PromptBuilder.Truncate(response.Transcript.Text, settings.Inference.MaxTranscriptCharacters),
                Notes = notes.Length > 0 ? "Scene summaries:\n" + notes.ToString() : null,
                Images = images
            };
        }

        private async Task RunVideoFunctionsAsync(
            IInferenceClient client,
            string? model,
            MediaSubject subject,
            VideoFunctionsSpec functions,
            VideoAnalysisResponse response,
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

        private async Task<Dictionary<long, string>> BuildThumbnailsAsync(
            List<long> stamps,
            Dictionary<long, NormalizedImage> frames,
            MediaWorkspace workspace,
            KeyframeSettings settings,
            CancellationToken token)
        {
            Dictionary<long, string> thumbnails = new Dictionary<long, string>();
            string dir = workspace.CreateSubdirectory("thumbnails");
            foreach (long t in stamps)
            {
                if (!frames.TryGetValue(t, out NormalizedImage? frame)) continue;
                NormalizedImage thumb = await _Services.Normalizer.NormalizeAsync(
                    frame.Path, Path.Combine(dir, "thumb_" + t + ".jpg"), settings.Media.ThumbnailMaxEdge, settings.Media.JpegQuality, token).ConfigureAwait(false);
                thumbnails[t] = Convert.ToBase64String(thumb.Data);
            }

            return thumbnails;
        }

        private static string TranscriptSlice(TranscriptResult transcript, long startMs, long endMs)
        {
            IEnumerable<string> parts = transcript.Segments
                .Where(s => s.EndMs > startMs && s.StartMs < endMs)
                .Select(s => s.Text.Trim())
                .Where(s => s.Length > 0);
            return String.Join(" ", parts);
        }

        private static List<long> PickEvenly(List<long> sorted, int count)
        {
            if (sorted.Count <= count) return sorted;
            List<long> picked = new List<long>();
            for (int i = 0; i < count; i++)
            {
                int index = (int)Math.Round((i + 0.5) * sorted.Count / count - 0.5);
                long value = sorted[Math.Clamp(index, 0, sorted.Count - 1)];
                if (picked.Count == 0 || picked[picked.Count - 1] != value) picked.Add(value);
            }

            return picked;
        }

        #endregion
    }
}
