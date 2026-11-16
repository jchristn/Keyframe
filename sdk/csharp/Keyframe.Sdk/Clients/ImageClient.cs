namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;
    /// <summary>
    /// Image analysis (POST /v1.0/image).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class ImageClient
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
        public ImageClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Analyze one image synchronously. A completed analysis returns even when some fields failed;
        /// check Success, Errors, and Fields[].Valid.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Analysis response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when request is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<ImageAnalysisResponse> AnalyzeAsync(ImageAnalysisRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return _Client.SendAsync<ImageAnalysisResponse>(HttpMethod.Post, "/v1.0/image", request, token);
        }

        #endregion
    }
}
