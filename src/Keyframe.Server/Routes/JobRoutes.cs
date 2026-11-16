namespace Keyframe.Server.Routes
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Job listing, status, result, cancellation, and deletion.
    /// </summary>
    public class JobRoutes
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
        public JobRoutes(ServerContext context)
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
            routes.Add(HttpMethod.GET, "/v1.0/jobs", RouteAuthEnum.Key, ListAsync,
                RouteRegistrar.Meta("listJobs", "List jobs", "Jobs", "Newest first. Filters: status, mediaKind (Video or Audio), principalName, fromUtc, toUtc. Paging: pageNumber (from 1), pageSize (1 to 1000, default 25).")
                    .WithParameter(OpenApiParameterMetadata.Query("status", "Queued, Running, Succeeded, Failed, Canceled, or Interrupted.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("mediaKind", "Video or Audio.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("principalName", "Submitting key name.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive lower bound (ISO 8601).", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive upper bound (ISO 8601).", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number, from 1.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithResponse(200, RouteRegistrar.Json("Page of jobs.", typeof(EnumerationResult<JobResponse>)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid filter.")));

            routes.Add(HttpMethod.GET, "/v1.0/jobs/{id}", RouteAuthEnum.Key, ReadAsync,
                RouteRegistrar.Meta("getJob", "Get job status", "Jobs", "Status and progress of one job.")
                    .WithResponse(200, RouteRegistrar.Json("Job.", typeof(JobResponse)))
                    .WithResponse(404, RouteRegistrar.Error("Job not found.")));

            routes.Add(HttpMethod.GET, "/v1.0/jobs/{id}/result", RouteAuthEnum.Key, ResultAsync,
                RouteRegistrar.Meta("getJobResult", "Get job result", "Jobs", "The VideoAnalysisResponse (video job) or AudioAnalysisResponse (audio job) of a succeeded job. Returns 409 while the job is not finished or did not succeed.")
                    .WithResponse(200, RouteRegistrar.Json("Result.", typeof(VideoAnalysisResponse)))
                    .WithResponse(404, RouteRegistrar.Error("Job not found."))
                    .WithResponse(409, RouteRegistrar.Error("The job has no result yet.")));

            routes.Add(HttpMethod.POST, "/v1.0/jobs/{id}/cancel", RouteAuthEnum.Key, CancelAsync,
                RouteRegistrar.Meta("cancelJob", "Cancel a job", "Jobs", "Cancels a queued or running job. Returns the job after cancellation.")
                    .WithResponse(200, RouteRegistrar.Json("Job.", typeof(JobResponse)))
                    .WithResponse(404, RouteRegistrar.Error("Job not found."))
                    .WithResponse(409, RouteRegistrar.Error("The job already finished.")));

            routes.Add(HttpMethod.DELETE, "/v1.0/jobs/{id}", RouteAuthEnum.Key, DeleteAsync,
                RouteRegistrar.Meta("deleteJob", "Delete a job", "Jobs", "Deletes a finished job and its result. Returns 409 for a queued or running job; cancel it first.")
                    .WithResponse(204, OpenApiResponseMetadata.NoContent())
                    .WithResponse(404, RouteRegistrar.Error("Job not found."))
                    .WithResponse(409, RouteRegistrar.Error("The job is still active.")));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            JobFilter filter = new JobFilter
            {
                Status = RouteHelpers.QueryEnum<JobStatusEnum>(context, "status"),
                MediaKind = RouteHelpers.QueryEnum<MediaKindEnum>(context, "mediaKind"),
                PrincipalName = RouteHelpers.Query(context, "principalName"),
                FromUtc = RouteHelpers.QueryDate(context, "fromUtc"),
                ToUtc = RouteHelpers.QueryDate(context, "toUtc"),
                PageNumber = RouteHelpers.QueryInt(context, "pageNumber") ?? 1,
                PageSize = RouteHelpers.QueryInt(context, "pageSize") ?? 25
            };

            EnumerationResult<Job> page = await _Context.Database.Jobs.EnumerateAsync(filter, context.Token).ConfigureAwait(false);
            EnumerationResult<JobResponse> result = new EnumerationResult<JobResponse>
            {
                Items = page.Items.Select(JobResponse.FromJob).ToList(),
                PageNumber = page.PageNumber,
                PageSize = page.PageSize,
                TotalCount = page.TotalCount,
                TotalPages = page.TotalPages
            };

            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            Job? job = await FindAsync(context).ConfigureAwait(false);
            if (job == null) return;
            await RouteHelpers.JsonAsync(context, 200, JobResponse.FromJob(job)).ConfigureAwait(false);
        }

        private async Task ResultAsync(HttpContextBase context)
        {
            Job? job = await FindAsync(context).ConfigureAwait(false);
            if (job == null) return;

            if (job.Status != JobStatusEnum.Succeeded)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.Conflict, 409, "Job " + job.Id + " is " + job.Status + " and has no result." + (job.Error != null ? " Error: " + job.Error : String.Empty)).ConfigureAwait(false);
                return;
            }

            JobResult? result = await _Context.Database.Jobs.ReadResultAsync(job.Id, context.Token).ConfigureAwait(false);
            if (result == null)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "The result for job " + job.Id + " is no longer stored.").ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = Keyframe.Core.Constants.JsonContentType;
            await context.Response.Send(result.ResultJson, context.Token).ConfigureAwait(false);
        }

        private async Task CancelAsync(HttpContextBase context)
        {
            string id = RouteHelpers.Param(context, "id") ?? String.Empty;
            Job? job = await _Context.Jobs.CancelAsync(id, context.Token).ConfigureAwait(false);
            if (job == null)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Job " + id + " not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, JobResponse.FromJob(job)).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            Job? job = await FindAsync(context).ConfigureAwait(false);
            if (job == null) return;

            if (!job.IsTerminal)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.Conflict, 409, "Job " + job.Id + " is " + job.Status + ". Cancel it before deleting.").ConfigureAwait(false);
                return;
            }

            await _Context.Database.Jobs.DeleteAsync(job.Id, context.Token).ConfigureAwait(false);
            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task<Job?> FindAsync(HttpContextBase context)
        {
            string id = RouteHelpers.Param(context, "id") ?? String.Empty;
            Job? job = String.IsNullOrWhiteSpace(id) ? null : await _Context.Database.Jobs.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (job == null) await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Job " + id + " not found.").ConfigureAwait(false);
            return job;
        }

        #endregion
    }
}
