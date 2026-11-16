namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Responses;
    /// <summary>
    /// Resolve the presented key (GET /v1.0/whoami).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class WhoAmIClient
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
        public WhoAmIClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Resolve the configured key: principal name and whether it is the admin key.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Principal.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<WhoAmIResponse> GetAsync(CancellationToken token = default)
        {
            return _Client.SendAsync<WhoAmIResponse>(HttpMethod.Get, "/v1.0/whoami", null, token);
        }

        #endregion
    }
}
