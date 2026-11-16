namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Responses;
    /// <summary>
    /// Model endpoints (GET /v1.0/endpoints, POST /v1.0/endpoints/{name}/test).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class EndpointsClient
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
        public EndpointsClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// List configured model endpoints with API keys masked and last observed health.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Endpoints.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<List<EndpointResponse>> ListAsync(CancellationToken token = default)
        {
            return _Client.SendAsync<List<EndpointResponse>>(HttpMethod.Get, "/v1.0/endpoints", null, token);
        }

        /// <summary>
        /// Run a connectivity probe against one endpoint. Requires the admin key.
        /// </summary>
        /// <param name="name">Endpoint name.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Test result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<EndpointTestResponse> TestAsync(string name, CancellationToken token = default)
        {
            string segment = KeyframeClient.Segment(name, nameof(name));
            return _Client.SendAsync<EndpointTestResponse>(HttpMethod.Post, "/v1.0/endpoints/" + segment + "/test", null, token);
        }

        #endregion
    }
}
