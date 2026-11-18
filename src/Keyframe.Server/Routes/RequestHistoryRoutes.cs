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
    /// Request history at /v1.0/api/request-history. Reads need a key; deletes need the admin key. When capture is
    /// disabled the routes still answer, with empty results.
    /// </summary>
    public class RequestHistoryRoutes
    {
        #region Private-Members

        private const string BasePath = "/v1.0/api/request-history";

        private readonly ServerContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public RequestHistoryRoutes(ServerContext context)
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
            routes.Add(HttpMethod.GET, BasePath, RouteAuthEnum.Key, ListAsync,
                Filters(RouteRegistrar.Meta("listRequestHistory", "List captured requests", "Request History", "Newest first. Headers and bodies are omitted; read one entry for them."))
                    .WithParameter(OpenApiParameterMetadata.Query("pageNumber", "Page number, from 1.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithParameter(OpenApiParameterMetadata.Query("pageSize", "Page size, 1 to 1000.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithResponse(200, RouteRegistrar.Json("Page of entries.", typeof(EnumerationResult<RequestHistoryEntry>)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid filter.")));

            routes.Add(HttpMethod.GET, BasePath + "/summary", RouteAuthEnum.Key, SummaryAsync,
                Filters(RouteRegistrar.Meta("summarizeRequestHistory", "Summarize captured requests", "Request History", "Bucketed success and failure counts with average duration. Every bucket in range is present, including empty ones. Defaults to the last 24 hours in 60 minute buckets."))
                    .WithParameter(OpenApiParameterMetadata.Query("bucketMinutes", "Bucket width in minutes, 1 to 10080.", false, OpenApiSchemaMetadata.Integer("int32")))
                    .WithResponse(200, RouteRegistrar.Json("Summary.", typeof(RequestHistorySummary)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid filter.")));

            routes.Add(HttpMethod.GET, BasePath + "/{id}", RouteAuthEnum.Key, ReadAsync,
                RouteRegistrar.Meta("getRequestHistoryEntry", "Get a captured request", "Request History", "One entry with redacted headers and (possibly truncated) bodies.")
                    .WithResponse(200, RouteRegistrar.Json("Entry.", typeof(RequestHistoryEntry)))
                    .WithResponse(404, RouteRegistrar.Error("Entry not found.")));

            routes.Add(HttpMethod.DELETE, BasePath + "/{id}", RouteAuthEnum.Admin, DeleteAsync,
                RouteRegistrar.Meta("deleteRequestHistoryEntry", "Delete a captured request", "Request History", "Deletes one entry and its headers.")
                    .WithResponse(204, OpenApiResponseMetadata.NoContent())
                    .WithResponse(404, RouteRegistrar.Error("Entry not found.")));

            routes.Add(HttpMethod.DELETE, BasePath, RouteAuthEnum.Admin, DeleteManyAsync,
                Filters(RouteRegistrar.Meta("deleteRequestHistory", "Delete captured requests", "Request History", "Deletes every entry matching the filter. With no filter, deletes all entries."))
                    .WithResponse(200, RouteRegistrar.Json("Deleted count.", typeof(DeleteResult)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid filter.")));
        }

        #endregion

        #region Private-Methods

        private static OpenApiRouteMetadata Filters(OpenApiRouteMetadata metadata)
        {
            return metadata
                .WithParameter(OpenApiParameterMetadata.Query("method", "HTTP method.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("statusCode", "Exact status code.", false, OpenApiSchemaMetadata.Integer("int32")))
                .WithParameter(OpenApiParameterMetadata.Query("pathContains", "Substring of the path.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("principalName", "Access key name.", false, OpenApiSchemaMetadata.String()))
                .WithParameter(OpenApiParameterMetadata.Query("fromUtc", "Inclusive lower bound (ISO 8601).", false, OpenApiSchemaMetadata.String("date-time")))
                .WithParameter(OpenApiParameterMetadata.Query("toUtc", "Exclusive upper bound (ISO 8601).", false, OpenApiSchemaMetadata.String("date-time")));
        }

        private static RequestHistoryFilter Filter(HttpContextBase context)
        {
            return new RequestHistoryFilter
            {
                Method = RouteHelpers.Query(context, "method"),
                StatusCode = RouteHelpers.QueryInt(context, "statusCode"),
                PathContains = RouteHelpers.Query(context, "pathContains"),
                PrincipalName = RouteHelpers.Query(context, "principalName"),
                FromUtc = RouteHelpers.QueryDate(context, "fromUtc"),
                ToUtc = RouteHelpers.QueryDate(context, "toUtc"),
                PageNumber = RouteHelpers.QueryInt(context, "pageNumber") ?? 1,
                PageSize = RouteHelpers.QueryInt(context, "pageSize") ?? 25,
                BucketMinutes = RouteHelpers.QueryInt(context, "bucketMinutes") ?? 60
            };
        }

        private async Task ListAsync(HttpContextBase context)
        {
            RequestHistoryFilter filter = Filter(context);
            if (!_Context.Settings.RequestHistory.Enable)
            {
                await RouteHelpers.JsonAsync(context, 200, new EnumerationResult<RequestHistoryEntry> { PageNumber = filter.PageNumber, PageSize = filter.PageSize }).ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, await _Context.Database.RequestHistory.EnumerateAsync(filter, context.Token).ConfigureAwait(false)).ConfigureAwait(false);
        }

        private async Task SummaryAsync(HttpContextBase context)
        {
            RequestHistoryFilter filter = Filter(context);
            RequestHistorySummary summary = await _Context.Database.RequestHistory.SummarizeAsync(filter, context.Token).ConfigureAwait(false);
            if (!_Context.Settings.RequestHistory.Enable)
            {
                foreach (RequestHistorySummaryBucket bucket in summary.Buckets)
                {
                    bucket.SuccessCount = 0;
                    bucket.FailureCount = 0;
                    bucket.AverageDurationMs = 0;
                }

                summary.TotalCount = 0;
                summary.TotalSuccess = 0;
                summary.TotalFailure = 0;
                summary.AverageDurationMs = 0;
            }

            await RouteHelpers.JsonAsync(context, 200, summary).ConfigureAwait(false);
        }

        private async Task ReadAsync(HttpContextBase context)
        {
            string id = RouteHelpers.Param(context, "id") ?? String.Empty;
            RequestHistoryEntry? entry = await _Context.Database.RequestHistory.ReadAsync(id, context.Token).ConfigureAwait(false);
            if (entry == null)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Request history entry " + id + " not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, entry).ConfigureAwait(false);
        }

        private async Task DeleteAsync(HttpContextBase context)
        {
            string id = RouteHelpers.Param(context, "id") ?? String.Empty;
            bool deleted = await _Context.Database.RequestHistory.DeleteAsync(id, context.Token).ConfigureAwait(false);
            if (!deleted)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Request history entry " + id + " not found.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.NoContentAsync(context).ConfigureAwait(false);
        }

        private async Task DeleteManyAsync(HttpContextBase context)
        {
            int deleted = await _Context.Database.RequestHistory.DeleteManyAsync(Filter(context), context.Token).ConfigureAwait(false);
            await RouteHelpers.JsonAsync(context, 200, new DeleteResult { DeletedCount = deleted }).ConfigureAwait(false);
        }

        #endregion
    }
}
