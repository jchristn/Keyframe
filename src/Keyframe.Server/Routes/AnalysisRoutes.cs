namespace Keyframe.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Media;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Image analysis (synchronous), video analysis (background job, optionally waited on), and probe.
    /// </summary>
    public class AnalysisRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public AnalysisRoutes(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Add(HttpMethod.POST, "/v1.0/image", RouteAuthEnum.Key, ImageAsync,
                RouteRegistrar.Meta("analyzeImage", "Analyze an image", "Analysis",
                    "Summarize an image and extract typed fields. Supply the image by Url or base64 Data. Every Context item is included in every prompt. "
                    + "Returns 200 for a completed analysis, including partial results (check Success, Errors, and Fields[].Valid).")
                    .WithRequestBody(RouteRegistrar.Body(typeof(ImageAnalysisRequest), "Image analysis request."))
                    .WithResponse(200, RouteRegistrar.Json("Analysis result with Results, Fields, Properties, and Telemetry.", typeof(ImageAnalysisResponse)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid request or unknown endpoint."))
                    .WithResponse(422, RouteRegistrar.Error("The media could not be downloaded, decoded, or is not an image."))
                    .WithResponse(502, RouteRegistrar.Error("The model endpoint is unreachable.")));

            routes.Add(HttpMethod.POST, "/v1.0/video", RouteAuthEnum.Key, VideoAsync,
                RouteRegistrar.Meta("analyzeVideo", "Analyze a video", "Analysis",
                    "Queue a video analysis job: scenes, transcript, per-scene and whole-video summaries and fields. Returns 202 with the job. "
                    + "When Wait is true and the job finishes within WaitTimeoutMs (capped by Jobs.MaxWaitTimeoutMs), returns 200 with the full result; "
                    + "a job that finished without succeeding returns 200 with the job so the caller can read its Error.")
                    .WithRequestBody(RouteRegistrar.Body(typeof(VideoAnalysisRequest), "Video analysis request."))
                    .WithResponse(200, RouteRegistrar.Json("Finished result (Wait) with Scenes, Transcript, and Telemetry.", typeof(VideoAnalysisResponse)))
                    .WithResponse(202, RouteRegistrar.Json("Job accepted. Poll /v1.0/jobs/{id}.", typeof(JobResponse)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid request or unknown endpoint."))
                    .WithResponse(429, RouteRegistrar.Error("The job queue is full.")));

            routes.Add(HttpMethod.POST, "/v1.0/probe", RouteAuthEnum.Key, ProbeAsync,
                RouteRegistrar.Meta("probeMedia", "Probe media properties", "Analysis",
                    "Deterministic media properties from ffprobe (dimensions, duration, codecs, tags). No model calls.")
                    .WithRequestBody(RouteRegistrar.Body(typeof(ProbeRequest), "Probe request."))
                    .WithResponse(200, RouteRegistrar.Json("Media properties.", typeof(MediaProperties)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid request."))
                    .WithResponse(422, RouteRegistrar.Error("The media could not be downloaded or decoded.")));
        }

        #endregion

        #region Private-Methods

        private async Task ImageAsync(HttpContextBase context)
        {
            ImageAnalysisRequest request = RouteHelpers.Body<ImageAnalysisRequest>(context);
            ImageAnalyzer analyzer = new ImageAnalyzer(_Context.Analysis);
            ImageAnalysisResponse response = await analyzer.AnalyzeAsync(request, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, response).ConfigureAwait(false);
        }

        private async Task VideoAsync(HttpContextBase context)
        {
            VideoAnalysisRequest request = RouteHelpers.Body<VideoAnalysisRequest>(context);
            new VideoAnalyzer(_Context.Analysis).Preflight(request);

            RequestContext ctx = RouteHelpers.Context(context);
            Job job = await _Context.Jobs.SubmitAsync(request, ctx.PrincipalName, context.Token).ConfigureAwait(false);

            if (request.Wait)
            {
                int timeout = Math.Min(request.WaitTimeoutMs, _Context.Settings.Jobs.MaxWaitTimeoutMs);
                Job? finished = await _Context.Jobs.WaitAsync(job.Id, timeout, context.Token).ConfigureAwait(false);
                if (finished != null)
                {
                    if (finished.Status == JobStatusEnum.Succeeded)
                    {
                        JobResult? result = await _Context.Database.Jobs.ReadResultAsync(finished.Id, context.Token).ConfigureAwait(false);
                        if (result != null)
                        {
                            context.Response.StatusCode = 200;
                            context.Response.ContentType = Keyframe.Core.Constants.JsonContentType;
                            context.Response.Headers["x-keyframe-job-id"] = finished.Id;
                            await context.Response.Send(result.ResultJson, context.Token).ConfigureAwait(false);
                            return;
                        }
                    }

                    await RouteHelpers.JsonAsync(context, 200, JobResponse.FromJob(finished)).ConfigureAwait(false);
                    return;
                }
            }

            context.Response.Headers["Location"] = "/v1.0/jobs/" + job.Id;
            await RouteHelpers.JsonAsync(context, 202, JobResponse.FromJob(job)).ConfigureAwait(false);
        }

        private async Task ProbeAsync(HttpContextBase context)
        {
            ProbeRequest request = RouteHelpers.Body<ProbeRequest>(context);
            List<string> errors = RequestValidator.Validate(request);
            if (errors.Count > 0) throw new RequestValidationException(errors);

            using (MediaWorkspace workspace = new MediaWorkspace(_Context.Settings.Media.TempDirectory))
            {
                ResolvedMedia resolved = await _Context.Analysis.Resolver.ResolveAsync(request.Media!, MediaKindEnum.Unknown, workspace, context.Token).ConfigureAwait(false);
                MediaProperties properties = await _Context.Analysis.Prober.ProbeAsync(resolved.Path, context.Token).ConfigureAwait(false);
                properties.SizeBytes = resolved.SizeBytes;
                properties.Sha256 = resolved.Sha256;
                properties.ContentType = resolved.ContentType;
                await RouteHelpers.JsonAsync(context, 200, properties).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
