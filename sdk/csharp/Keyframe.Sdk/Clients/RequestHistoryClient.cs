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
    /// Captured request history (/v1.0/api/request-history). Reading needs any key; deleting needs the admin key.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class RequestHistoryClient
    {
        #region Private-Members

        private const string BasePath = "/v1.0/api/request-history";
        private readonly KeyframeClient _Client;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the sub-client. Use the instance exposed on KeyframeClient.
        /// </summary>
        /// <param name="client">Parent client.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public RequestHistoryClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List captured requests, newest first. Headers and bodies are omitted; use GetAsync for them.
        /// </summary>
        /// <param name="filter">Filter and paging. Null lists the first page.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of entries.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<EnumerationResult<RequestHistoryEntry>> ListAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            RequestHistoryFilter f = filter ?? new RequestHistoryFilter();
            Dictionary<string, string?> query = Filters(f);
            query["pageNumber"] = f.PageNumber.ToString(CultureInfo.InvariantCulture);
            query["pageSize"] = f.PageSize.ToString(CultureInfo.InvariantCulture);
            return _Client.SendAsync<EnumerationResult<RequestHistoryEntry>>(HttpMethod.Get, BasePath + KeyframeClient.BuildQuery(query), null, token);
        }

        /// <summary>
        /// Bucketed success and failure counts. Defaults to the last 24 hours in 60 minute buckets.
        /// </summary>
        /// <param name="filter">Filter and BucketMinutes. Null uses server defaults.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<RequestHistorySummary> SummaryAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            RequestHistoryFilter f = filter ?? new RequestHistoryFilter();
            Dictionary<string, string?> query = Filters(f);
            query["bucketMinutes"] = f.BucketMinutes.ToString(CultureInfo.InvariantCulture);
            return _Client.SendAsync<RequestHistorySummary>(HttpMethod.Get, BasePath + "/summary" + KeyframeClient.BuildQuery(query), null, token);
        }

        /// <summary>
        /// Get one entry with redacted headers and possibly truncated bodies.
        /// </summary>
        /// <param name="id">Entry identifier (prefix "req_").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<RequestHistoryEntry> GetAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<RequestHistoryEntry>(HttpMethod.Get, BasePath + "/" + segment, null, token);
        }

        /// <summary>
        /// Delete one entry. Requires the admin key.
        /// </summary>
        /// <param name="id">Entry identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (403 without the admin key, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            await _Client.SendCheckedAsync(HttpMethod.Delete, BasePath + "/" + segment, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Delete every entry matching the filter. With no filter values set, deletes all entries. Requires the admin key.
        /// Paging and BucketMinutes are ignored.
        /// </summary>
        /// <param name="filter">Filter. Null deletes everything.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Deleted count.</returns>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (403 without the admin key).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<DeleteResult> DeleteManyAsync(RequestHistoryFilter? filter = null, CancellationToken token = default)
        {
            Dictionary<string, string?> query = Filters(filter ?? new RequestHistoryFilter());
            return _Client.SendAsync<DeleteResult>(HttpMethod.Delete, BasePath + KeyframeClient.BuildQuery(query), null, token);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string?> Filters(RequestHistoryFilter f)
        {
            return new Dictionary<string, string?>
            {
                { "method", f.Method },
                { "statusCode", f.StatusCode?.ToString(CultureInfo.InvariantCulture) },
                { "pathContains", f.PathContains },
                { "principalName", f.PrincipalName },
                { "fromUtc", f.FromUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                { "toUtc", f.ToUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) }
            };
        }

        #endregion
    }
}
