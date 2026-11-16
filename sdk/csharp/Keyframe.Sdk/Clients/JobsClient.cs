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
    /// Video job management (/v1.0/jobs).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class JobsClient
    {
        #region Public-Members

        /// <summary>
        /// Default polling interval in milliseconds used by WaitAsync. Default 1000. Minimum 100, maximum 60000.
        /// </summary>
        public int DefaultPollIntervalMs
        {
            get
            {
                return _DefaultPollIntervalMs;
            }
            set
            {
                _DefaultPollIntervalMs = Math.Clamp(value, 100, 60000);
            }
        }

        /// <summary>
        /// Default overall wait limit in milliseconds used by WaitAsync. Default 600000 (10 minutes).
        /// Minimum 1000, maximum 86400000 (24 hours).
        /// </summary>
        public int DefaultWaitTimeoutMs
        {
            get
            {
                return _DefaultWaitTimeoutMs;
            }
            set
            {
                _DefaultWaitTimeoutMs = Math.Clamp(value, 1000, 86400000);
            }
        }

        #endregion

        #region Private-Members

        private readonly KeyframeClient _Client;
        private int _DefaultPollIntervalMs = 1000;
        private int _DefaultWaitTimeoutMs = 600000;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the sub-client. Use the instance exposed on KeyframeClient.
        /// </summary>
        /// <param name="client">Parent client.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public JobsClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List jobs, newest first.
        /// </summary>
        /// <param name="filter">Filter and paging. Null lists the first page of all jobs.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page of jobs.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<EnumerationResult<JobResponse>> ListAsync(JobFilter? filter = null, CancellationToken token = default)
        {
            JobFilter f = filter ?? new JobFilter();
            Dictionary<string, string?> query = new Dictionary<string, string?>
            {
                { "status", f.Status?.ToString() },
                { "mediaKind", f.MediaKind?.ToString() },
                { "principalName", f.PrincipalName },
                { "fromUtc", f.FromUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                { "toUtc", f.ToUtc?.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) },
                { "pageNumber", f.PageNumber.ToString(CultureInfo.InvariantCulture) },
                { "pageSize", f.PageSize.ToString(CultureInfo.InvariantCulture) }
            };

            return _Client.SendAsync<EnumerationResult<JobResponse>>(HttpMethod.Get, "/v1.0/jobs" + KeyframeClient.BuildQuery(query), null, token);
        }

        /// <summary>
        /// Get one job's status and progress.
        /// </summary>
        /// <param name="id">Job identifier (prefix "kfj_").</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code (404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<JobResponse> GetAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<JobResponse>(HttpMethod.Get, "/v1.0/jobs/" + segment, null, token);
        }

        /// <summary>
        /// Get the result of a succeeded video job. Use GetAudioResultAsync for audio jobs.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Video analysis response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (409 while the job has not succeeded, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<VideoAnalysisResponse> GetResultAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<VideoAnalysisResponse>(HttpMethod.Get, "/v1.0/jobs/" + segment + "/result", null, token);
        }

        /// <summary>
        /// Get the result of a succeeded audio job.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Audio analysis response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (409 while the job has not succeeded, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<AudioAnalysisResponse> GetAudioResultAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<AudioAnalysisResponse>(HttpMethod.Get, "/v1.0/jobs/" + segment + "/result", null, token);
        }

        /// <summary>
        /// Cancel a queued or running job.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job after cancellation.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (409 when the job already finished, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<JobResponse> CancelAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            return _Client.SendAsync<JobResponse>(HttpMethod.Post, "/v1.0/jobs/" + segment + "/cancel", null, token);
        }

        /// <summary>
        /// Delete a finished job and its result.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (409 while the job is active, 404 when not found).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public async Task DeleteAsync(string id, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(id, nameof(id));
            await _Client.SendCheckedAsync(HttpMethod.Delete, "/v1.0/jobs/" + segment, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Poll a job until it reaches a terminal status (Succeeded, Failed, Canceled, or Interrupted).
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="pollIntervalMs">Polling interval in milliseconds. Null uses DefaultPollIntervalMs. Clamped 100 to 60000.</param>
        /// <param name="timeoutMs">Overall limit in milliseconds. Null uses DefaultWaitTimeoutMs. Clamped 1000 to 86400000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The job in its terminal state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        /// <exception cref="TimeoutException">Thrown when the job is still active after timeoutMs.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public async Task<JobResponse> WaitAsync(string id, int? pollIntervalMs = null, int? timeoutMs = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));
            int interval = Math.Clamp(pollIntervalMs ?? _DefaultPollIntervalMs, 100, 60000);
            int limit = Math.Clamp(timeoutMs ?? _DefaultWaitTimeoutMs, 1000, 86400000);
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(limit);

            while (true)
            {
                token.ThrowIfCancellationRequested();
                JobResponse job = await GetAsync(id, token).ConfigureAwait(false);
                if (job.IsTerminal) return job;
                if (DateTime.UtcNow >= deadline)
                    throw new TimeoutException("Job " + id + " is still " + job.Status + " after " + limit + "ms.");
                await Task.Delay(interval, token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
