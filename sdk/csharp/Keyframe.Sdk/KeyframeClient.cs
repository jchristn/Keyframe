namespace Keyframe.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Collections.Specialized;
    using System.Linq;
    using System.Net.Http;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk.Clients;
    using Keyframe.Sdk.Responses;
    using Keyframe.Sdk.Serialization;
    using RestWrapper;

    /// <summary>
    /// Client for the Keyframe REST API. Create one per server and reuse it; it holds one pooled HttpClient.
    /// The API key is never logged, returned, or included in ToString output.
    /// Thread safety: safe for concurrent use once configured. Changing configuration while requests are in flight is not supported.
    /// </summary>
    public class KeyframeClient : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Default base URL, http://127.0.0.1:8700.
        /// </summary>
        public static string DefaultBaseUrl { get; } = "http://127.0.0.1:8700";

        /// <summary>
        /// Server base URL without a trailing slash, for example http://127.0.0.1:8700.
        /// </summary>
        public string BaseUrl
        {
            get
            {
                return _BaseUrl;
            }
        }

        /// <summary>
        /// Whether an API key is configured. The key itself is not readable.
        /// </summary>
        public bool HasApiKey
        {
            get
            {
                return !String.IsNullOrEmpty(_ApiKey);
            }
        }

        /// <summary>
        /// Header used to present the API key. Default Bearer.
        /// </summary>
        public AuthHeaderEnum AuthHeader { get; set; } = AuthHeaderEnum.Bearer;

        /// <summary>
        /// Per-request timeout in milliseconds. Default 300000 (5 minutes). Minimum 1000, maximum 3600000.
        /// Values outside the range are clamped.
        /// </summary>
        public int TimeoutMs
        {
            get
            {
                return _TimeoutMs;
            }
            set
            {
                _TimeoutMs = Math.Clamp(value, 1000, 3600000);
            }
        }

        /// <summary>
        /// User-Agent header value. Default "Keyframe.Sdk/0.1.0".
        /// </summary>
        public string UserAgent
        {
            get
            {
                return _UserAgent;
            }
            set
            {
                _UserAgent = String.IsNullOrWhiteSpace(value) ? "Keyframe.Sdk/0.1.0" : value;
            }
        }

        /// <summary>
        /// Health check (anonymous).
        /// </summary>
        public HealthClient Health { get; }

        /// <summary>
        /// Resolve the presented key.
        /// </summary>
        public WhoAmIClient WhoAmI { get; }

        /// <summary>
        /// Image analysis.
        /// </summary>
        public ImageClient Image { get; }

        /// <summary>
        /// Video analysis, including submit-and-wait.
        /// </summary>
        public VideoClient Video { get; }

        /// <summary>
        /// Audio analysis, including submit-and-wait.
        /// </summary>
        public AudioClient Audio { get; }

        /// <summary>
        /// Segment retrieval: cut a time range or a frame out of a video or audio source.
        /// </summary>
        public SegmentsClient Segments { get; }

        /// <summary>
        /// Deterministic media probing.
        /// </summary>
        public ProbeClient Probe { get; }

        /// <summary>
        /// Video and audio job management.
        /// </summary>
        public JobsClient Jobs { get; }

        /// <summary>
        /// Model endpoint listing and testing.
        /// </summary>
        public EndpointsClient Endpoints { get; }

        /// <summary>
        /// Request history.
        /// </summary>
        public RequestHistoryClient RequestHistory { get; }

        /// <summary>
        /// Analytics summaries and stored analyses (admin key required).
        /// </summary>
        public AnalyticsClient Analytics { get; }

        /// <summary>
        /// Settings management and restart (admin key required).
        /// </summary>
        public SettingsClient Settings { get; }

        #endregion

        #region Private-Members

        private readonly string _BaseUrl;
        private string? _ApiKey = null;
        private int _TimeoutMs = 300000;
        private string _UserAgent = "Keyframe.Sdk/0.1.0";
        private readonly HttpClient _HttpClient;
        private readonly bool _OwnsHttpClient;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create a client.
        /// </summary>
        /// <param name="baseUrl">Server base URL, for example http://127.0.0.1:8700. Null uses DefaultBaseUrl.</param>
        /// <param name="apiKey">Access key or admin key. Null sends no credential (only health is anonymous).</param>
        /// <param name="httpClient">Optional HttpClient to use. When supplied, the caller owns and disposes it.</param>
        /// <exception cref="ArgumentException">Thrown when baseUrl is not an absolute http or https URL.</exception>
        public KeyframeClient(string? baseUrl = null, string? apiKey = null, HttpClient? httpClient = null)
        {
            string url = String.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("Base URL must be an absolute http or https URL.", nameof(baseUrl));

            _BaseUrl = url.TrimEnd('/');
            _ApiKey = String.IsNullOrEmpty(apiKey) ? null : apiKey;

            if (httpClient != null)
            {
                _HttpClient = httpClient;
                _OwnsHttpClient = false;
            }
            else
            {
                _HttpClient = new HttpClient();
                _HttpClient.Timeout = Timeout.InfiniteTimeSpan;
                _OwnsHttpClient = true;
            }

            Health = new HealthClient(this);
            WhoAmI = new WhoAmIClient(this);
            Image = new ImageClient(this);
            Video = new VideoClient(this);
            Audio = new AudioClient(this);
            Segments = new SegmentsClient(this);
            Probe = new ProbeClient(this);
            Jobs = new JobsClient(this);
            Endpoints = new EndpointsClient(this);
            RequestHistory = new RequestHistoryClient(this);
            Analytics = new AnalyticsClient(this);
            Settings = new SettingsClient(this);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Replace the API key. Null or empty clears it.
        /// </summary>
        /// <param name="apiKey">Access key or admin key.</param>
        public void SetApiKey(string? apiKey)
        {
            _ApiKey = String.IsNullOrEmpty(apiKey) ? null : apiKey;
        }

        /// <summary>
        /// Send a request and return the raw response without throwing on error status codes.
        /// Useful for routes the typed clients do not cover.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path relative to the base URL, starting with a slash, optionally with a query string.</param>
        /// <param name="body">Object to serialize as the JSON body, or null for no body.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Raw response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when method or path is null.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the client is disposed.</exception>
        /// <exception cref="HttpRequestException">Thrown when the server cannot be reached.</exception>
        /// <exception cref="TimeoutException">Thrown when the request exceeds TimeoutMs.</exception>
        /// <exception cref="OperationCanceledException">Thrown when the token is canceled.</exception>
        public Task<RawResponse> SendRawAsync(HttpMethod method, string path, object? body = null, CancellationToken token = default)
        {
            return SendCoreAsync(method, path, body, false, token);
        }

        /// <summary>
        /// Returns the base URL and whether a key is set. Never includes the key.
        /// </summary>
        /// <returns>Description.</returns>
        public override string ToString()
        {
            return "KeyframeClient " + _BaseUrl + (HasApiKey ? " (key set)" : " (no key)");
        }

        /// <summary>
        /// Dispose the client and, when the client created it, its HttpClient.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Internal-Methods

        internal async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken token)
        {
            RawResponse raw = await SendCheckedAsync(method, path, body, token).ConfigureAwait(false);
            return Deserialize<T>(method, path, raw);
        }

        internal async Task<RawResponse> SendCheckedAsync(HttpMethod method, string path, object? body, CancellationToken token)
        {
            RawResponse raw = await SendRawAsync(method, path, body, token).ConfigureAwait(false);
            if (raw.StatusCode < 200 || raw.StatusCode > 299) throw ToException(method, path, raw);
            return raw;
        }

        internal async Task<RawResponse> SendBinaryCheckedAsync(HttpMethod method, string path, object? body, CancellationToken token)
        {
            RawResponse raw = await SendCoreAsync(method, path, body, true, token).ConfigureAwait(false);
            if (raw.StatusCode < 200 || raw.StatusCode > 299) throw ToException(method, path, raw);
            return raw;
        }

        internal static T Deserialize<T>(HttpMethod method, string path, RawResponse raw)
        {
            if (String.IsNullOrWhiteSpace(raw.Body))
                throw new KeyframeApiException(method.Method, path, raw.StatusCode, null, raw.Body, raw.RequestId);

            try
            {
                T? value = KeyframeSerializer.Deserialize<T>(raw.Body);
                if (value == null) throw new KeyframeApiException(method.Method, path, raw.StatusCode, null, raw.Body, raw.RequestId);
                return value;
            }
            catch (JsonException e)
            {
                throw new InvalidOperationException(method + " " + path + " returned a body that is not a valid " + typeof(T).Name + ": " + e.Message, e);
            }
        }

        internal static string BuildQuery(Dictionary<string, string?> values)
        {
            List<string> parts = values
                .Where(kvp => !String.IsNullOrEmpty(kvp.Value))
                .Select(kvp => Uri.EscapeDataString(kvp.Key) + "=" + Uri.EscapeDataString(kvp.Value!))
                .ToList();
            if (!parts.Any()) return String.Empty;
            return "?" + String.Join("&", parts);
        }

        internal static string Segment(string value, string name)
        {
            if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(name);
            return Uri.EscapeDataString(value);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose managed resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing && _OwnsHttpClient) _HttpClient.Dispose();
            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<RawResponse> SendCoreAsync(HttpMethod method, string path, object? body, bool binary, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(path);
            ObjectDisposedException.ThrowIf(_Disposed, this);
            token.ThrowIfCancellationRequested();

            string url = _BaseUrl + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);

            using (CancellationTokenSource timeout = new CancellationTokenSource(_TimeoutMs))
            {
                using (CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token))
                {
                    using (RestRequest request = new RestRequest(url, method, _HttpClient))
                    {
                        request.UserAgent = _UserAgent;
                        request.Headers.Add("Accept", binary ? "*/*" : "application/json");
                        ApplyAuth(request);

                        try
                        {
                            RestResponse? response;
                            if (body != null)
                            {
                                request.ContentType = "application/json";
                                string json = KeyframeSerializer.Serialize(body);
                                response = await request.SendAsync(json, linked.Token).ConfigureAwait(false);
                            }
                            else
                            {
                                response = await request.SendAsync(linked.Token).ConfigureAwait(false);
                            }

                            if (response == null) throw new HttpRequestException("No response from " + method + " " + path + ".");

                            using (response)
                            {
                                RawResponse raw = new RawResponse
                                {
                                    StatusCode = response.StatusCode,
                                    ContentType = response.ContentType,
                                    RequestId = Header(response.Headers, "x-request-id"),
                                    JobId = Header(response.Headers, "x-keyframe-job-id"),
                                    Location = Header(response.Headers, "Location"),
                                    Headers = Headers(response.Headers)
                                };

                                if (binary)
                                {
                                    raw.Data = response.DataAsBytes ?? Array.Empty<byte>();
                                    bool isJson = !String.IsNullOrEmpty(raw.ContentType) && raw.ContentType.Contains("json", StringComparison.OrdinalIgnoreCase);
                                    bool isError = raw.StatusCode < 200 || raw.StatusCode > 299;
                                    raw.Body = (isJson || isError) ? Encoding.UTF8.GetString(raw.Data) : String.Empty;
                                }
                                else
                                {
                                    raw.Body = response.DataAsString ?? String.Empty;
                                }

                                return raw;
                            }
                        }
                        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !token.IsCancellationRequested)
                        {
                            throw new TimeoutException(method + " " + path + " did not complete within " + _TimeoutMs + "ms.");
                        }
                    }
                }
            }
        }

        private void ApplyAuth(RestRequest request)
        {
            if (String.IsNullOrEmpty(_ApiKey)) return;
            if (AuthHeader == AuthHeaderEnum.XApiKey) request.Headers.Add("x-api-key", _ApiKey);
            else request.Authorization.BearerToken = _ApiKey;
        }

        private static KeyframeApiException ToException(HttpMethod method, string path, RawResponse raw)
        {
            ApiErrorResponse? error = null;
            if (!String.IsNullOrWhiteSpace(raw.Body))
            {
                try
                {
                    error = KeyframeSerializer.Deserialize<ApiErrorResponse>(raw.Body);
                }
                catch (JsonException)
                {
                    error = null;
                }
            }

            return new KeyframeApiException(method.Method, path, raw.StatusCode, error, raw.Body, raw.RequestId);
        }

        private static string? Header(NameValueCollection? headers, string name)
        {
            if (headers == null) return null;
            foreach (string? key in headers.AllKeys)
            {
                if (key != null && String.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return headers[key];
            }

            return null;
        }

        private static Dictionary<string, string> Headers(NameValueCollection? headers)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (headers == null) return result;
            foreach (string? key in headers.AllKeys)
            {
                if (key == null) continue;
                string? value = headers[key];
                if (value != null) result[key] = value;
            }

            return result;
        }

        #endregion
    }
}
