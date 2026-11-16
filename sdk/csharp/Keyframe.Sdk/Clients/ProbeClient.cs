namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;
    /// <summary>
    /// Deterministic media properties (POST /v1.0/probe). No model calls.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class ProbeClient
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
        public ProbeClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Probe media properties.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<MediaProperties> ProbeAsync(ProbeRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _Client.SendAsync<MediaProperties>(HttpMethod.Post, "/v1.0/probe", request, token);
        }

        /// <summary>
        /// Probe media properties of one source.
        /// </summary>
        /// <param name="media">Media source (Url or Data).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Media properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when media is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<MediaProperties> ProbeAsync(MediaSource media, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(media);
            return ProbeAsync(new ProbeRequest { Media = media }, token);
        }

        #endregion
    }
}
