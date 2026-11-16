namespace Keyframe.Server.Services
{
    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;
    using System.Security.Cryptography;
    using System.Text;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Classes;

    /// <summary>
    /// Resolves the caller from an Authorization bearer token or x-api-key header. Keys are compared in constant time
    /// and never logged. Failed attempts are counted per source IP in a sliding one-minute window.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AuthenticationService
    {
        #region Public-Members

        /// <summary>
        /// Principal name recorded for the admin key.
        /// </summary>
        public const string AdminPrincipal = "admin";

        /// <summary>
        /// Principal name recorded when authentication is disabled.
        /// </summary>
        public const string AnonymousPrincipal = "anonymous";

        #endregion

        #region Private-Members

        private readonly Func<KeyframeSettings> _Settings;
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _Failures = new ConcurrentDictionary<string, Queue<DateTime>>(StringComparer.Ordinal);

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Live settings accessor.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public AuthenticationService(Func<KeyframeSettings> settings)
        {
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract the presented key from headers.
        /// </summary>
        /// <param name="authorization">Authorization header value.</param>
        /// <param name="apiKey">x-api-key header value.</param>
        /// <returns>The key, or null when none was presented.</returns>
        public static string? ExtractKey(string? authorization, string? apiKey)
        {
            if (!String.IsNullOrWhiteSpace(authorization))
            {
                string trimmed = authorization.Trim();
                if (trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    string token = trimmed.Substring(7).Trim();
                    if (token.Length > 0) return token;
                }
            }

            if (!String.IsNullOrWhiteSpace(apiKey)) return apiKey.Trim();
            return null;
        }

        /// <summary>
        /// Resolve a presented key into the request context.
        /// </summary>
        /// <param name="key">Presented key, or null.</param>
        /// <param name="context">Context to populate.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public void Resolve(string? key, RequestContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            KeyframeSettings settings = _Settings();

            if (!settings.Auth.RequireAuthentication)
            {
                context.IsAuthenticated = true;
                context.IsAdmin = true;
                context.PrincipalName = AnonymousPrincipal;
                return;
            }

            context.IsAuthenticated = false;
            context.IsAdmin = false;
            context.PrincipalName = null;
            if (String.IsNullOrEmpty(key)) return;

            if (FixedEquals(key, settings.Auth.AdminApiKey))
            {
                context.IsAuthenticated = true;
                context.IsAdmin = true;
                context.PrincipalName = AdminPrincipal;
                return;
            }

            string? matched = null;
            foreach (AccessKeySettings candidate in settings.Auth.AccessKeys)
            {
                // Every key is compared so timing does not reveal which one matched.
                bool equal = FixedEquals(key, candidate.Key);
                if (equal && candidate.Active && matched == null) matched = candidate.Name;
            }

            if (matched != null)
            {
                context.IsAuthenticated = true;
                context.PrincipalName = matched;
            }
        }

        /// <summary>
        /// Whether a source IP has exceeded the failed-attempt limit.
        /// </summary>
        /// <param name="sourceIp">Source IP.</param>
        /// <returns>True when rate limited.</returns>
        public bool IsRateLimited(string? sourceIp)
        {
            Queue<DateTime>? queue;
            if (!_Failures.TryGetValue(sourceIp ?? String.Empty, out queue)) return false;
            lock (queue)
            {
                Trim(queue);
                return queue.Count >= _Settings().Auth.MaxFailedAttemptsPerMinute;
            }
        }

        /// <summary>
        /// Record a failed attempt for a source IP.
        /// </summary>
        /// <param name="sourceIp">Source IP.</param>
        public void RecordFailure(string? sourceIp)
        {
            Queue<DateTime> queue = _Failures.GetOrAdd(sourceIp ?? String.Empty, ip => new Queue<DateTime>());
            lock (queue)
            {
                Trim(queue);
                queue.Enqueue(DateTime.UtcNow);
            }

            KeyframeMetrics.RecordAuth("failure");
        }

        /// <summary>
        /// Drop expired failure windows. Called by the maintenance loop.
        /// </summary>
        public void Sweep()
        {
            foreach (KeyValuePair<string, Queue<DateTime>> kvp in _Failures)
            {
                bool empty;
                lock (kvp.Value)
                {
                    Trim(kvp.Value);
                    empty = kvp.Value.Count == 0;
                }

                if (empty)
                {
                    Queue<DateTime>? removed;
                    _Failures.TryRemove(kvp.Key, out removed);
                }
            }
        }

        /// <summary>
        /// Compare two strings in time that depends only on their lengths.
        /// </summary>
        /// <param name="presented">Presented value.</param>
        /// <param name="expected">Expected value.</param>
        /// <returns>True when equal.</returns>
        public static bool FixedEquals(string? presented, string? expected)
        {
            if (presented == null || expected == null) return false;
            byte[] a = SHA256.HashData(Encoding.UTF8.GetBytes(presented));
            byte[] b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        #endregion

        #region Private-Methods

        private static void Trim(Queue<DateTime> queue)
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);
            while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
        }

        #endregion
    }
}
