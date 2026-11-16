namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;

    /// <summary>
    /// Analytics (/v1.0/analytics): summaries with percentiles and gap-filled buckets, stored analyses, and their
    /// model call attempts. Every route needs the admin key.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AnalyticsClient
    {
        #region Private-Members

        private const string BasePath = "/v1.0/analytics";
        private readonly KeyframeClient _Client;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the sub-client. Use the instance exposed on KeyframeClient.
        /// </summary>
        /// <param name="client">Parent client.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public AnalyticsClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Summarize analyses: totals, per media kind, gap-filled time buckets, and breakdowns by model, stage, and call purpose.
        /// Defaults to the last 24 hours; the bucket defaults to minute up to 2 hours, hour up to 7 days, otherwise day.
        /// </summary>
        /// <param name="filter">Filter. Null uses the server defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (400 invalid filter or too many buckets, 403 without the admin key).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<AnalyticsSummary> GetSummaryAsync(AnalyticsSummaryFilter? filter = null, CancellationToken token = default)
        {
            AnalyticsSummaryFilter f = filter ?? new AnalyticsSummaryFilter();
            Dictionary<string, string?> query = new Dictionary<string, string?>
            {
                { "fromUtc", Date(f.FromUtc) },
                { "toUtc", Date(f.ToUtc) },
                { "bucket", f.Bucket?.ToString().ToLowerInvariant() },
                { "mediaKind", f.MediaKind?.ToString() },
                { "endpoint", f.Endpoint },
                { "model", f.Model }
            };

            return _Client.SendAsync<AnalyticsSummary>(HttpMethod.Get, BasePath + "/summary" + KeyframeClient.BuildQuery(query), null, token);
        }

        /// <summary>
        /// List stored analyses, newest first. Listed records have Calls set to null; use GetAnalysisAsync for the model calls.
        /// </summary>
        /// <param name="filter">Filter and paging. Null lists the first 50.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of analyses.</returns>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (400 invalid filter, 403 without the admin key).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<EnumerationResult<AnalysisRecord>> ListAnalysesAsync(AnalyticsFilter? filter = null, CancellationToken token = default)
        {
            AnalyticsFilter f = filter ?? new AnalyticsFilter();
            Dictionary<string, string?> query = new Dictionary<string, string?>
            {
                { "mediaKind", f.MediaKind?.ToString() },
                { "success", f.Success.HasValue ? (f.Success.Value ? "true" : "false") : null },
                { "fromUtc", Date(f.FromUtc) },
                { "toUtc", Date(f.ToUtc) },
                { "maxResults", f.MaxResults.ToString(CultureInfo.InvariantCulture) },
                { "skip", f.Skip.ToString(CultureInfo.InvariantCulture) }
            };

            return _Client.SendAsync<EnumerationResult<AnalysisRecord>>(HttpMethod.Get, BasePath + "/analyses" + KeyframeClient.BuildQuery(query), null, token);
        }

        /// <summary>
        /// Get one stored analysis with every model call attempt in Calls.
        /// </summary>
        /// <param name="id">Analysis identifier (prefix "kfa_").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Analysis record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (403 without the admin key, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<AnalysisRecord> GetAnalysisAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<AnalysisRecord>(HttpMethod.Get, BasePath + "/analyses/" + segment, null, token);
        }

        /// <summary>
        /// Delete one stored analysis and its model calls.
        /// </summary>
        /// <param name="id">Analysis identifier (prefix "kfa_").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deleted count (1).</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (403 without the admin key, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<DeleteResult> DeleteAnalysisAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<DeleteResult>(HttpMethod.Delete, BasePath + "/analyses/" + segment, null, token);
        }

        /// <summary>
        /// Delete stored analyses (and their model calls) that completed before olderThanUtc. Null deletes every analysis.
        /// </summary>
        /// <param name="olderThanUtc">Exclusive cutoff on completion time. Null deletes all.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deleted count.</returns>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (400 invalid cutoff, 403 without the admin key).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<DeleteResult> DeleteAnalysesAsync(DateTime? olderThanUtc = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = new Dictionary<string, string?>
            {
                { "olderThanUtc", Date(olderThanUtc) }
            };

            return _Client.SendAsync<DeleteResult>(HttpMethod.Delete, BasePath + "/analyses" + KeyframeClient.BuildQuery(query), null, token);
        }

        #endregion

        #region Private-Methods

        private static string? Date(DateTime? value)
        {
            return value?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
