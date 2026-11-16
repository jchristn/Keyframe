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
    /// Analyzes one image: resolve, probe, normalize, then summarize and extract in parallel.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class ImageAnalyzer
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
        public ImageAnalyzer(AnalysisServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            _Services = services;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Analyze an image.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response. Function failures are reported in the response rather than thrown.</returns>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when the requested or default endpoint is unavailable.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the image cannot be downloaded or decoded.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the media is not an image.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public Task<ImageAnalysisResponse> AnalyzeAsync(ImageAnalysisRequest request, CancellationToken token = default)
        {
            return AnalyzeAsync(request, null, token);
        }

        /// <summary>
        /// Analyze an image, recording where the request came from.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="origin">Request origin for telemetry and analytics, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response. Function failures are reported in the response rather than thrown.</returns>
        /// <exception cref="RequestValidationException">Thrown when the request is invalid.</exception>
        /// <exception cref="EndpointNotFoundException">Thrown when the requested or default endpoint is unavailable.</exception>
        /// <exception cref="MediaResolutionException">Thrown when the image cannot be downloaded or decoded.</exception>
        /// <exception cref="UnsupportedMediaException">Thrown when the media is not an image.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<ImageAnalysisResponse> AnalyzeAsync(ImageAnalysisRequest request, AnalysisOrigin? origin, CancellationToken token = default)
        {
            List<string> errors = RequestValidator.Validate(request);
            if (errors.Count > 0) throw new RequestValidationException(errors);

            KeyframeSettings settings = _Services.Settings();
            EndpointSettings endpoint = _Services.Clients.ResolveCompletionEndpoint(request.Model?.Endpoint, settings.Inference.DefaultVision);
            IInferenceClient client = _Services.Clients.GetCompletionClient(endpoint);
            string? model = request.Model?.Name;

            AnalysisContext context = new AnalysisContext(MediaKindEnum.Image)
            {
                Origin = origin,
                VisionEndpoint = endpoint.Name,
                VisionModel = model ?? endpoint.Model
            };
            ImageAnalysisResponse response = new ImageAnalysisResponse { MediaKind = MediaKindEnum.Image, RequestId = origin?.RequestId };
            Stopwatch sw = Stopwatch.StartNew();
            string outcome = "failure";
            string? exception = null;
            bool canceled = false;

            using (Activity? activity = KeyframeTelemetry.Source.StartActivity("analysis:image", ActivityKind.Internal))
            using (MediaWorkspace workspace = new MediaWorkspace(settings.Media.TempDirectory))
            {
                activity?.SetTag("keyframe.analysis_id", response.Id);
                activity?.SetTag("keyframe.endpoint", endpoint.Name);

                try
                {
                    ResolvedMedia resolved;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Resolve))
                    {
                        resolved = await _Services.Resolver.ResolveAsync(request.Image!, MediaKindEnum.Image, workspace, token).ConfigureAwait(false);
                    }

                    MediaProperties properties;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Probe))
                    {
                        properties = await _Services.Prober.ProbeAsync(resolved.Path, token).ConfigureAwait(false);
                    }

                    context.MediaBytes = resolved.SizeBytes;
                    if (properties.MediaKind != MediaKindEnum.Image)
                        throw new UnsupportedMediaException("Expected an image but the media is " + properties.MediaKind.ToString().ToLowerInvariant() + ".");

                    properties.SizeBytes = resolved.SizeBytes;
                    properties.Sha256 = resolved.Sha256;
                    properties.ContentType = resolved.ContentType;
                    if (request.IncludeProperties) response.Properties = properties;

                    NormalizedImage normalized;
                    using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Normalize))
                    {
                        normalized = await _Services.Normalizer.NormalizeAsync(
                            resolved.Path,
                            workspace.PathFor("normalized.jpg"),
                            settings.Media.ImageMaxEdge,
                            settings.Media.JpegQuality,
                            token).ConfigureAwait(false);
                    }

                    MediaSubject subject = new MediaSubject
                    {
                        Noun = "the image",
                        Description = "One image is attached (" + (properties.Width ?? normalized.Width) + "x" + (properties.Height ?? normalized.Height) + " pixels).",
                        CallerContext = ContextFormatter.Render(request.Context),
                        Images = new List<InferenceImage>
                        {
                            new InferenceImage { Data = normalized.Data, MediaType = normalized.MediaType, Sha256 = normalized.Sha256 }
                        }
                    };

                    Task<SummaryOutcome>? summaryTask = null;
                    Task<ExtractionOutcome>? extractTask = null;
                    FunctionsSpec functions = request.Functions;

                    if (functions.Summarize.Enable)
                        summaryTask = RunSummaryAsync(client, model, subject, functions.Summarize, context, token);
                    if (functions.Extract.Enable)
                        extractTask = RunExtractAsync(client, model, subject, functions.Extract, context, token);

                    if (summaryTask != null)
                    {
                        SummaryOutcome summary = await summaryTask.ConfigureAwait(false);
                        if (summary.Success) response.Results[functions.Summarize.Key] = summary.Text;
                        else
                        {
                            response.Results[functions.Summarize.Key] = null;
                            context.AddError("Summarize: " + summary.Error);
                        }
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
                    KeyframeMetrics.RecordAnalysis("image", "failure", sw.Elapsed.TotalSeconds);
                    throw;
                }
                finally
                {
                    context.CompleteInto(response);
                    _Services.NotifyCompleted(context.BuildCompletion(response, exception, canceled, 0, null));
                }
            }

            KeyframeMetrics.RecordAnalysis("image", outcome, sw.Elapsed.TotalSeconds);
            return response;
        }

        #endregion

        #region Private-Methods

        private async Task<SummaryOutcome> RunSummaryAsync(IInferenceClient client, string? model, MediaSubject subject, SummarizeSpec spec, AnalysisContext context, CancellationToken token)
        {
            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Summarize))
            {
                SummaryOutcome outcome = await _Services.Functions.SummarizeAsync(client, model, subject, spec, ModelCallPurposeEnum.Summarize, null, context, token).ConfigureAwait(false);
                if (!outcome.Success) stage.Fail(outcome.Error);
                return outcome;
            }
        }

        private async Task<ExtractionOutcome> RunExtractAsync(IInferenceClient client, string? model, MediaSubject subject, ExtractSpec spec, AnalysisContext context, CancellationToken token)
        {
            using (StageTimer stage = context.BeginStage(AnalysisStageEnum.Extract))
            {
                ExtractionOutcome outcome = await _Services.Functions.ExtractAsync(client, model, subject, spec, ModelCallPurposeEnum.Extract, null, context, token).ConfigureAwait(false);
                if (!outcome.Success) stage.Fail(String.Join(" ", outcome.Errors));
                return outcome;
            }
        }

        #endregion
    }
}
