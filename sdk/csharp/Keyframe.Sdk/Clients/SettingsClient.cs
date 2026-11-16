namespace Keyframe.Sdk.Clients
{
    using System;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Responses;
    using Keyframe.Sdk.Settings;
    /// <summary>
    /// Settings and restart (admin key required).
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class SettingsClient
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
        public SettingsClient(KeyframeClient client)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Get settings with secrets masked as "****" plus the last four characters, and per-field metadata.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Settings response.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<SettingsResponse> GetAsync(CancellationToken token = default)
        {
            return _Client.SendAsync<SettingsResponse>(HttpMethod.Get, "/v1.0/settings", null, token);
        }

        /// <summary>
        /// Replace settings. Send the complete object; masked secrets sent back unchanged keep their stored values.
        /// Fields this SDK version does not know about are reset to server defaults, so keep the SDK in step with the server.
        /// </summary>
        /// <param name="settings">Complete settings object, usually from GetAsync with edits applied.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Saved settings, masked.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<SettingsResponse> UpdateAsync(KeyframeSettings settings, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            return _Client.SendAsync<SettingsResponse>(HttpMethod.Put, "/v1.0/settings", settings, token);
        }

        /// <summary>
        /// Ask the server to exit cleanly so its container runtime relaunches it.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Restart acknowledgement.</returns>
        /// <exception cref="KeyframeApiException">Thrown when the server returns a non-success status code.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds the client timeout.</exception>
        public Task<RestartResponse> RestartAsync(CancellationToken token = default)
        {
            return _Client.SendAsync<RestartResponse>(HttpMethod.Post, "/v1.0/restart", null, token);
        }

        #endregion
    }
}
