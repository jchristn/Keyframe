namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Responses;
    /// <summary>
    /// Server health (anonymous route GET /v1.0/health).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class HealthClient
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
        public HealthClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get server health: version, uptime, ffmpeg availability, job counts, and endpoint health.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Health.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<HealthResponse> GetAsync(CancellationToken token = default)
        {
            return _Client.SendAsync<HealthResponse>(HttpMethod.Get, "/v1.0/health", null, token);
        }

        /// <summary>
        /// Check whether the server answers HEAD / with 200. Never throws for HTTP errors or connection failures.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the server is reachable.</returns>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public async Task<bool> IsAliveAsync(CancellationToken token = default)
        {
            try
            {
                RawResponse raw = await _Client.SendRawAsync(HttpMethod.Head, "/", null, token).ConfigureAwait(false);
                return raw.StatusCode == 200;
            }
            catch (HttpRequestException)
            {
                return false;
            }
            catch (TimeoutException)
            {
                return false;
            }
        }

        #endregion
    }
}
