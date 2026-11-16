namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;

    /// <summary>
    /// Video analysis (POST /v1.0/video) and submit-and-wait.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class VideoClient
    {
        #region Private-Members

        private readonly KeyframeClient _Client;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create the sub-client. Use the instance exposed on KeyframeClient.
        /// </summary>
        /// <param name="client">Parent client.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public VideoClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Submit a video. Returns the queued job (202) or, when request.Wait is true and the job finished in time,
        /// the full result or the finished job (200).
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Submit outcome holding either Job or Result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown on a non-success status code (400 invalid, 429 queue full).</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public async Task<VideoSubmitResult> AnalyzeAsync(VideoAnalysisRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            const string path = "/v1.0/video";
            RawResponse raw = await _Client.SendCheckedAsync(HttpMethod.Post, path, request, token).ConfigureAwait(false);

            VideoSubmitResult result = new VideoSubmitResult { StatusCode = raw.StatusCode };
            VideoBodyShape shape = KeyframeClient.Deserialize<VideoBodyShape>(HttpMethod.Post, path, raw);
            if (raw.StatusCode == 202 || !String.IsNullOrEmpty(shape.Status))
            {
                result.Job = KeyframeClient.Deserialize<JobResponse>(HttpMethod.Post, path, raw);
                result.JobId = result.Job.Id;
            }
            else
            {
                result.Result = KeyframeClient.Deserialize<VideoAnalysisResponse>(HttpMethod.Post, path, raw);
                result.JobId = raw.JobId ?? String.Empty;
            }

            return result;
        }

        /// <summary>
        /// Submit a video, poll the job until it is terminal, then fetch and return the result.
        /// request.Wait is honored: when the server returns the result directly, no polling happens.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="pollIntervalMs">Polling interval in milliseconds. Null uses Jobs.DefaultPollIntervalMs. Clamped 100 to 60000.</param>
        /// <param name="timeoutMs">Overall wait limit in milliseconds. Null uses Jobs.DefaultWaitTimeoutMs. Clamped 1000 to 86400000.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Video analysis response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="KeyframeJobException">Thrown when the job finishes Failed, Canceled, or Interrupted.</exception>
        /// <exception cref="TimeoutException">Thrown when the job is still active after timeoutMs.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public async Task<VideoAnalysisResponse> AnalyzeAndWaitAsync(VideoAnalysisRequest request, int? pollIntervalMs = null, int? timeoutMs = null, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            VideoSubmitResult submitted = await AnalyzeAsync(request, token).ConfigureAwait(false);
            if (submitted.Result != null) return submitted.Result;
            if (submitted.Job == null) throw new InvalidOperationException("The server returned neither a job nor a result.");

            JobResponse job = submitted.Job.IsTerminal
                ? submitted.Job
                : await _Client.Jobs.WaitAsync(submitted.JobId, pollIntervalMs, timeoutMs, token).ConfigureAwait(false);

            if (job.Status != JobStatusEnum.Succeeded) throw new KeyframeJobException(job);
            return await _Client.Jobs.GetResultAsync(job.Id, token).ConfigureAwait(false);
        }

        #endregion
    }
}
