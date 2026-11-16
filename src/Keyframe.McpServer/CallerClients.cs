namespace Keyframe.McpServer
{
    using System;
    using System.Collections.Concurrent;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.McpServer.Settings;
    using Keyframe.Sdk;
    using Keyframe.Sdk.Responses;
    using Voltaic.Core;

    /// <summary>
    /// Resolves the Keyframe SDK client for the caller of the current MCP request. MCP clients authenticate with their
    /// own Keyframe key (access or admin), which is checked against the Keyframe server and then used for every REST
    /// call the tool makes, so a key has the same rights over MCP as over REST. Requests without an authenticated
    /// caller (the TCP transport, or authentication disabled) use the fallback key from settings.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class CallerClients : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Claim name that carries the caller's Keyframe key through the ambient request context.
        /// </summary>
        public const string KeyClaim = "keyframe-key";

        /// <summary>
        /// How long a successful key check is cached, in milliseconds. Default 60000. Minimum 0, maximum 3600000.
        /// </summary>
        public int ValidationCacheMs
        {
            get
            {
                return _ValidationCacheMs;
            }
            set
            {
                _ValidationCacheMs = Math.Clamp(value, 0, 3600000);
            }
        }

        #endregion

        #region Private-Members

        private const int MaxCachedClients = 256;

        private readonly KeyframeConnectionSettings _Settings;
        private readonly KeyframeClient _Fallback;
        private readonly ConcurrentDictionary<string, KeyframeClient> _Clients = new ConcurrentDictionary<string, KeyframeClient>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, CallerValidation> _Validated = new ConcurrentDictionary<string, CallerValidation>(StringComparer.Ordinal);
        private int _ValidationCacheMs = 60000;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Keyframe connection settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public CallerClients(KeyframeConnectionSettings settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Fallback = new KeyframeClient(settings.Endpoint, settings.AccessKey) { TimeoutMs = settings.TimeoutMs };
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// The client for the current caller: the caller's own key when the request was authenticated, otherwise the
        /// fallback key from settings.
        /// </summary>
        /// <returns>Client.</returns>
        public KeyframeClient Current()
        {
            RpcCallContext? context = RpcCallContext.Current;
            string? key = null;
            if (context != null && context.Claims != null) context.Claims.TryGetValue(KeyClaim, out key);
            if (String.IsNullOrEmpty(key)) return _Fallback;
            return ForKey(key);
        }

        /// <summary>
        /// Check a presented key against the Keyframe server (GET /v1.0/whoami). Successful checks are cached for
        /// <see cref="ValidationCacheMs"/>.
        /// </summary>
        /// <param name="key">Presented key.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The principal name when the key is valid, otherwise null.</returns>
        public async Task<string?> ValidateAsync(string? key, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(key)) return null;
            string hash = Hash(key);

            CallerValidation? cached;
            if (_Validated.TryGetValue(hash, out cached) && cached.ExpiresUtc > DateTime.UtcNow) return cached.Principal;

            try
            {
                WhoAmIResponse who = await ForKey(key).WhoAmI.GetAsync(token).ConfigureAwait(false);
                if (!who.IsAuthenticated) return null;
                string principal = String.IsNullOrEmpty(who.PrincipalName) ? (who.IsAdmin ? "admin" : "caller") : who.PrincipalName;
                if (_ValidationCacheMs > 0)
                    _Validated[hash] = new CallerValidation { Principal = principal, ExpiresUtc = DateTime.UtcNow.AddMilliseconds(_ValidationCacheMs) };
                return principal;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                foreach (KeyframeClient client in _Clients.Values) client.Dispose();
                _Clients.Clear();
                _Fallback.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private KeyframeClient ForKey(string key)
        {
            string hash = Hash(key);
            KeyframeClient? existing;
            if (_Clients.TryGetValue(hash, out existing)) return existing;

            if (_Clients.Count >= MaxCachedClients)
            {
                foreach (string stale in _Clients.Keys)
                {
                    KeyframeClient? removed;
                    if (_Clients.TryRemove(stale, out removed)) removed.Dispose();
                    if (_Clients.Count < MaxCachedClients / 2) break;
                }
            }

            KeyframeClient created = new KeyframeClient(_Settings.Endpoint, key) { TimeoutMs = _Settings.TimeoutMs };
            KeyframeClient winner = _Clients.GetOrAdd(hash, created);
            if (!ReferenceEquals(winner, created)) created.Dispose();
            return winner;
        }

        private static string Hash(string key)
        {
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        }

        #endregion
    }
}
