namespace Keyframe.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Analytics at /v1.0/analytics: summaries with percentiles and gap-filled buckets, stored analyses, and their
    /// model call attempts. Every route needs the admin key.
    /// </summary>
    public class AnalyticsRoutes
    {
        #region Private-Members

        private const string BasePath = "/v1.0/analytics";

        private readonly ServerContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public AnalyticsRoutes(ServerContext context)
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
            routes.Add(HttpMethod.GET, BasePath + "/summary", RouteAuthEnum.Admin, SummaryAsync,
                RouteRegistrar.Meta("getAnalyticsSummary", "Summarize analytics", "Analytics",
                    "Totals, per media kind, gap-filled time buckets, and breakdowns by model, stage, and call purpose, with count, min, max, average, "
                    + "p50, p90, p95, p99, standard deviation, and sum for every timing. Defaults to the last 24 hours; the bucket defaults to minute "
                    + "up to 2 hours, hour up to 7 days, otherwise day.")
                    .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive lower bound on completion time (ISO 8601). Default 24 hours before toUtc.", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive upper bound (ISO 8601). Default now.", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithParameter(OpenApiParameterMetadata.Query("bucket", "minute, hour, or day.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("mediaKind", "Image, Video, or Audio.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("endpoint", "Endpoint name.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("model", "Model name.", false, OpenApiSchemaMetadata.String()))
                    .WithResponse(200, RouteRegistrar.Json("Summary.", typeof(AnalyticsSummary)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid filter or too many buckets.")));

            routes.Add(HttpMethod.GET, BasePath + "/analyses", RouteAuthEnum.Admin, ListAsync,
                RouteRegistrar.Meta("listAnalyses", "List analyses", "Analytics", "Stored analyses, newest first, without model calls.")
                    .WithParameter(OpenApiParameterMetadata.Query("mediaKind", "Image, Video, or Audio.", false, OpenApiSchemaMetadata.String()))
                    .WithParameter(OpenApiParameterMetadata.Query("success", "true or false.", false, OpenApiSchemaMetadata.Boolean()))
                    .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive lower bound on completion time (ISO 8601).", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive upper bound (ISO 8601).", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithParameter(OpenApiParameterMetadata.Query("maxResults", "Page size, 1 to 1000. Default 50.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithParameter(OpenApiParameterMetadata.Query("skip", "Records to skip. Default 0.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithResponse(200, RouteRegistrar.Json("Page of analyses.", typeof(EnumerationResult<AnalysisRecord>)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid filter.")));

            routes.Add(HttpMethod.GET, BasePath + "/analyses/{id}", RouteAuthEnum.Admin, ReadAsync,
                RouteRegistrar.Meta("getAnalysis", "Get an analysis", "Analytics", "One stored analysis with every model call attempt in Calls.")
                    .WithResponse(200, RouteRegistrar.Json("Analysis.", typeof(AnalysisRecord)))
                    .WithResponse(404, RouteRegistrar.Error("Analysis not found.")));

            routes.Add(HttpMethod.DELETE, BasePath + "/analyses/{id}", RouteAuthEnum.Admin, DeleteAsync,
                RouteRegistrar.Meta("deleteAnalysis", "Delete an analysis", "Analytics", "Deletes one analysis and its model calls.")
                    .WithResponse(200, RouteRegistrar.Json("Deleted count (1).", typeof(DeleteResult)))
                    .WithResponse(404, RouteRegistrar.Error("Analysis not found.")));

            routes.Add(HttpMethod.DELETE, BasePath + "/analyses", RouteAuthEnum.Admin, DeleteManyAsync,
                RouteRegistrar.Meta("deleteAnalyses", "Delete analyses", "Analytics", "Deletes analyses (and their model calls) that completed before olderThanUtc. Without olderThanUtc, deletes all.")
                    .WithParameter(OpenApiParameterMetadata.Query("olderThanUtc", "Exclusive cutoff on completion time (ISO 8601). Default now.", false, OpenApiSchemaMetadata.String("date-time")))
                    .WithResponse(200, RouteRegistrar.Json("Deleted count.", typeof(DeleteResult)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid cutoff.")));
        }

        #endregion

        #region Private-Methods

        private async Task SummaryAsync(HttpContextBase context)
        {
            AnalyticsSummaryFilter filter = new AnalyticsSummaryFilter
            {
                FromUtc = RouteHelpers.QueryDate(context, "fromUtc"),
                ToUtc = RouteHelpers.QueryDate(context, "toUtc"),
                Bucket = RouteHelpers.QueryEnum<AnalyticsBucketEnum>(context, "bucket"),
                MediaKind = RouteHelpers.QueryEnum<MediaKindEnum>(context, "mediaKind"),
                Endpoint = RouteHelpers.Query(context, "endpoint"),
                Model = RouteHelpers.Query(context, "model")
            };

            AnalyticsSummary summary = await _Context.Database.Analytics.SummarizeAsync(filter, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, summary).ConfigureAwait(false);
        }

        private async Task ListAsync(HttpContextBase context)
        {
            int? pageSize = RouteHelpers.QueryInt(context, "pageSize");
            int? pageNumber = RouteHelpers.QueryInt(context, "pageNumber");
            AnalyticsFilter filter = new AnalyticsFilter
            {
                MediaKind = RouteHelpers.QueryEnum<MediaKindEnum>(context, "mediaKind"),
                Success = RouteHelpers.QueryBool(context, "success"),
                FromUtc = RouteHelpers.QueryDate(context, "fromUtc"),
                ToUtc = RouteHelpers.QueryDate(context, "toUtc"),
                MaxResults = RouteHelpers.QueryInt(context, "maxResults") ?? pageSize ?? 50
            };

            int? skip = RouteHelpers.QueryInt(context, "skip");
            if (skip.HasValue) filter.Skip = skip.Value;
            else if (pageNumber.HasValue) filter.Skip = (Math.Max(1, pageNumber.Value) - 1) * filter.MaxResults;

            EnumerationResult<AnalysisRecord> page = await _Context.Database.Analytics.EnumerateAsync(filter, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, page).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            string id = RouteHelpers.Param(context, "id") ?? String.Empty;
            AnalysisRecord? record = await _Context.Database.Analytics.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (record == null)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Analysis " + id + " not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, record).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            string id = RouteHelpers.Param(context, "id") ?? String.Empty;
            bool deleted = await _Context.Database.Analytics.DeleteAsync(id, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Analysis " + id + " not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, new DeleteResult { DeletedCount = 1 }).ConfigureAwait(false);
        }

        private async Task DeleteManyAsync(HttpContextBase context)
        {
            DateTime cutoff = RouteHelpers.QueryDate(context, "olderThanUtc") ?? DateTime.UtcNow.AddSeconds(1);
            int deleted = await _Context.Database.Analytics.PruneAsync(cutoff, context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, new DeleteResult { DeletedCount = deleted }).ConfigureAwait(false);
        }

        #endregion
    }
}
