namespace Keyframe.Server.Services
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Text;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Keyframe.Core.Database;
    using Keyframe.Core.Models;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Classes;
    using SyslogLogging;
    using WatsonWebserver.Core;

    /// <summary>
    /// Captures completed requests into request history. Entries are built synchronously in PostRouting, queued on a
    /// bounded channel (dropped when full, never blocking the response), and written by a background loop. Secret
    /// headers are redacted. Request bodies on the settings route are redacted because they carry keys, and response
    /// bodies of request history routes are not kept because they embed earlier entries.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class RequestHistoryCaptureService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Value written in place of redacted values.
        /// </summary>
        public const string Redacted = "[redacted]";

        /// <summary>
        /// Entries dropped because the queue was full.
        /// </summary>
        public long Dropped
        {
            get
            {
                return Interlocked.Read(ref _Dropped);
            }
        }

        /// <summary>
        /// Entries waiting to be written.
        /// </summary>
        public int Pending
        {
            get
            {
                return _Queue.Reader.CanCount ? _Queue.Reader.Count : 0;
            }
        }

        #endregion

        #region Private-Members

        private static readonly HashSet<string> _SecretHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "authorization", "proxy-authorization", "cookie", "set-cookie"
        };

        private const string HistoryPrefix = "/v1.0/api/request-history";
        private const string SettingsPath = "/v1.0/settings";

        private readonly DatabaseDriverBase _Database;
        private readonly Func<KeyframeSettings> _Settings;
        private readonly LoggingModule _Logging;
        private readonly Channel<RequestHistoryEntry> _Queue;
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Writer = null;
        private long _Dropped = 0;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="settings">Live settings accessor.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="capacity">Queue capacity. Default 10000. Minimum 100, maximum 1000000.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public RequestHistoryCaptureService(DatabaseDriverBase database, Func<KeyframeSettings> settings, LoggingModule logging, int capacity = 10000)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));

            BoundedChannelOptions options = new BoundedChannelOptions(Math.Clamp(capacity, 100, 1000000))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            };

            _Queue = Channel.CreateBounded<RequestHistoryEntry>(options);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the background writer.
        /// </summary>
        public void Start()
        {
            if (_Writer != null) return;
            _Writer = Task.Run(() => WriteLoopAsync(_Stopping.Token));
        }

        /// <summary>
        /// Stop after draining queued entries, waiting at most five seconds.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token = default)
        {
            _Queue.Writer.TryComplete();
            if (_Writer == null) return;

            try
            {
                await _Writer.WaitAsync(TimeSpan.FromSeconds(5), token).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _Stopping.Cancel();
            }
            catch (OperationCanceledException)
            {
                _Stopping.Cancel();
            }
        }

        /// <summary>
        /// Wait until queued entries are written. Used by tests.
        /// </summary>
        /// <param name="timeoutMs">Maximum wait in milliseconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the queue drained.</returns>
        public async Task<bool> FlushAsync(int timeoutMs, CancellationToken token = default)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (Pending == 0 && Interlocked.CompareExchange(ref _Writing, 0, 0) == 0) return true;
                await Task.Delay(20, token).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>
        /// Capture a completed request. Never throws.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        public void Capture(HttpContextBase context)
        {
            if (context == null) return;
            try
            {
                if (!_Settings().RequestHistory.Enabled) return;
                RequestHistoryEntry entry = Build(context, _Settings().RequestHistory);
                if (!_Queue.Writer.TryWrite(entry)) Interlocked.Increment(ref _Dropped);
            }
            catch (Exception ex)
            {
                _Logging.Debug("[RequestHistory] capture failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Build an entry from a completed request.
        /// </summary>
        /// <param name="context">HTTP context.</param>
        /// <param name="settings">Capture settings.</param>
        /// <returns>Entry.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static RequestHistoryEntry Build(HttpContextBase context, RequestHistorySettings settings)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            RequestHistoryEntry entry = new RequestHistoryEntry();
            string path = context.Request.Url.RawWithoutQuery ?? "/";
            entry.Method = context.Request.Method.ToString();
            entry.Path = Truncate(path, 1024);
            entry.Url = Truncate(context.Request.Url.RawWithQuery ?? path, 2048);
            entry.StatusCode = context.Response.StatusCode;
            entry.CreatedUtc = context.Timestamp.Start;
            entry.CompletedUtc = context.Timestamp.End ?? DateTime.UtcNow;
            entry.DurationMs = Math.Round(context.Timestamp.TotalMs ?? (entry.CompletedUtc.Value - entry.CreatedUtc).TotalMilliseconds, 3);
            entry.SourceIp = context.Request.Source.IpAddress;
            entry.RequestContentType = context.Request.ContentType;
            entry.ResponseContentType = context.Response.ContentType;

            if (context.Metadata is RequestContext ctx)
            {
                entry.RequestId = ctx.RequestId;
                entry.PrincipalName = ctx.PrincipalName;
            }

            CopyHeaders(context.Request.Headers, entry.RequestHeaders!);
            CopyHeaders(context.Response.Headers, entry.ResponseHeaders!);

            string requestBody = SafeRequestBody(context);
            bool binaryResponse = IsBinary(context.Response.ContentType);
            string responseBody = binaryResponse ? String.Empty : SafeResponseBody(context);
            entry.RequestBodyBytes = Encoding.UTF8.GetByteCount(requestBody);
            entry.ResponseBodyBytes = binaryResponse ? SafeResponseLength(context) : Encoding.UTF8.GetByteCount(responseBody);

            if (path.StartsWith(SettingsPath, StringComparison.OrdinalIgnoreCase) && context.Request.Method != HttpMethod.GET)
            {
                entry.RequestBody = entry.RequestBodyBytes > 0 ? Redacted : null;
            }
            else
            {
                bool truncated;
                entry.RequestBody = Clip(requestBody, settings.MaxRequestBodyBytes, out truncated);
                entry.RequestBodyTruncated = truncated;
            }

            if (binaryResponse || path.StartsWith(HistoryPrefix, StringComparison.OrdinalIgnoreCase) || path.StartsWith(SettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                entry.ResponseBody = null;
            }
            else
            {
                bool truncated;
                entry.ResponseBody = Clip(responseBody, settings.MaxResponseBodyBytes, out truncated);
                entry.ResponseBodyTruncated = truncated;
            }

            return entry;
        }

        /// <summary>
        /// Whether a header must be redacted: Authorization, Proxy-Authorization, Cookie, Set-Cookie, and any name
        /// containing "api-key" or "token" (case-insensitive).
        /// </summary>
        /// <param name="name">Header name.</param>
        /// <returns>True when redacted.</returns>
        public static bool IsSecretHeader(string? name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            if (_SecretHeaders.Contains(name)) return true;
            string lower = name.ToLowerInvariant();
            return lower.Contains("api-key") || lower.Contains("apikey") || lower.Contains("token") || lower.Contains("secret") || lower.Contains("password");
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
                _Stopping.Cancel();
                _Stopping.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Members-and-Methods

        private int _Writing = 0;

        private async Task WriteLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _Queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    RequestHistoryEntry? entry;
                    while (_Queue.Reader.TryRead(out entry))
                    {
                        Interlocked.Exchange(ref _Writing, 1);
                        try
                        {
                            await _Database.RequestHistory.CreateAsync(entry, token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }
                        catch (Exception ex)
                        {
                            _Logging.Warn("[RequestHistory] persist failed: " + ex.Message);
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _Writing, 0);
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static void CopyHeaders(NameValueCollection? source, Dictionary<string, string> target)
        {
            if (source == null) return;
            foreach (string? key in source.AllKeys)
            {
                if (String.IsNullOrEmpty(key)) continue;
                target[Truncate(key, 256)] = IsSecretHeader(key) ? Redacted : Truncate(source[key] ?? String.Empty, 4096);
            }
        }

        private static string? Clip(string body, int maxBytes, out bool truncated)
        {
            truncated = false;
            if (String.IsNullOrEmpty(body)) return null;
            if (Encoding.UTF8.GetByteCount(body) <= maxBytes) return body;
            truncated = true;
            if (maxBytes <= 0) return String.Empty;

            // Encode only a prefix so a large base64 upload is not copied in full.
            string prefix = body.Length > maxBytes ? body.Substring(0, maxBytes) : body;
            byte[] bytes = Encoding.UTF8.GetBytes(prefix);
            if (bytes.Length <= maxBytes) return prefix;
            return Encoding.UTF8.GetString(bytes, 0, maxBytes).TrimEnd('�');
        }

        private static string Truncate(string value, int max)
        {
            return value.Length <= max ? value : value.Substring(0, max);
        }

        private static string SafeRequestBody(HttpContextBase context)
        {
            try
            {
                return context.Request.DataAsString ?? String.Empty;
            }
            catch (Exception)
            {
                return String.Empty;
            }
        }

        private static bool IsBinary(string? contentType)
        {
            if (String.IsNullOrWhiteSpace(contentType)) return false;
            string type = contentType.Trim().ToLowerInvariant();
            return type.StartsWith("video/", StringComparison.Ordinal)
                || type.StartsWith("audio/", StringComparison.Ordinal)
                || type.StartsWith("image/", StringComparison.Ordinal)
                || type.StartsWith("application/octet-stream", StringComparison.Ordinal);
        }

        private static long SafeResponseLength(HttpContextBase context)
        {
            try
            {
                return context.Response.DataAsBytes?.Length ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static string SafeResponseBody(HttpContextBase context)
        {
            try
            {
                return context.Response.DataAsString ?? String.Empty;
            }
            catch (Exception)
            {
                return String.Empty;
            }
        }

        #endregion
    }
}
