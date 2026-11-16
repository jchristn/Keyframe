namespace Keyframe.Server
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Database;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Helpers;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Classes;
    using Keyframe.Server.Routes;
    using Keyframe.Server.Services;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// The Keyframe REST server: composes the database, analysis services, background services, and the Watson host.
    /// Thread safety: start and stop from one thread; request handling is concurrent.
    /// </summary>
    public class KeyframeServer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Shared server context, available after <see cref="StartAsync"/>.
        /// </summary>
        public ServerContext Context
        {
            get
            {
                return _Context ?? throw new InvalidOperationException("The server has not started.");
            }
        }

        /// <summary>
        /// Base URL, for example "http://127.0.0.1:8700".
        /// </summary>
        public string BaseUrl
        {
            get
            {
                KeyframeSettings settings = _SettingsManager.Settings;
                string host = settings.Rest.Hostname == "*" || settings.Rest.Hostname == "0.0.0.0" || settings.Rest.Hostname == "+" ? "127.0.0.1" : settings.Rest.Hostname;
                return (settings.Rest.Ssl ? "https://" : "http://") + host + ":" + settings.Rest.Port;
            }
        }

        /// <summary>
        /// Registered routes, for diagnostics and tests.
        /// </summary>
        public List<string> Routes
        {
            get
            {
                return _Routes?.Registered ?? new List<string>();
            }
        }

        /// <summary>
        /// Raised when an admin requests a restart.
        /// </summary>
        public event EventHandler? RestartRequested;

        #endregion

        #region Private-Members

        private const string Header = "[KeyframeServer] ";

        private readonly SettingsManager _SettingsManager;
        private readonly LoggingModule _Logging;
        private readonly InferenceClientFactory? _ClientOverride;
        private DatabaseDriverBase? _Database = null;
        private AnalysisServices? _Analysis = null;
        private AuthenticationService? _Auth = null;
        private JobService? _Jobs = null;
        private EndpointHealthService? _EndpointHealth = null;
        private RequestHistoryCaptureService? _Capture = null;
        private MaintenanceService? _Maintenance = null;
        private ServerContext? _Context = null;
        private Webserver? _Server = null;
        private RouteRegistrar? _Routes = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settingsManager">Settings manager.</param>
        /// <param name="logging">Logging.</param>
        /// <param name="clients">Optional inference client factory, used by tests to inject clients.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public KeyframeServer(SettingsManager settingsManager, LoggingModule logging, InferenceClientFactory? clients = null)
        {
            _SettingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
            _ClientOverride = clients;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Initialize storage and services, register routes, and start listening.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StartAsync(CancellationToken token = default)
        {
            KeyframeSettings settings = _SettingsManager.Settings;
            Func<KeyframeSettings> live = () => _SettingsManager.Settings;

            _Logging.Info(Header + "database provider " + settings.Database.Type);
            _Database = await DatabaseDriverFactory.CreateAndInitializeAsync(settings.Database, token).ConfigureAwait(false);
            _Logging.Info(Header + "database schema version " + _Database.SchemaVersion);

            _Analysis = new AnalysisServices(live, _ClientOverride);
            _Auth = new AuthenticationService(live);
            _Jobs = new JobService(_Database, _Analysis, live, _Logging);
            _EndpointHealth = new EndpointHealthService(_Analysis, live, _Logging);
            _Capture = new RequestHistoryCaptureService(_Database, live, _Logging);
            _Maintenance = new MaintenanceService(_Database, live, _Auth, _Logging);

            _Context = new ServerContext(_SettingsManager, _Database, _Analysis, _Auth, _Jobs, _EndpointHealth, _Capture, _Logging);
            _Context.RequestRestart = () => RestartRequested?.Invoke(this, EventArgs.Empty);

            try
            {
                _Context.FfmpegVersion = await _Analysis.Ffmpeg.GetFfmpegVersionAsync(token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _Logging.Debug(Header + "ffmpeg check failed: " + ex.Message);
            }

            if (_Context.FfmpegVersion == null) _Logging.Warn(Header + "ffmpeg was not found at '" + settings.Media.FfmpegPath + "'; media routes will fail until it is installed");
            else _Logging.Info(Header + _Context.FfmpegVersion);

            if (!settings.Auth.RequireAuthentication) _Logging.Warn(Header + "authentication is DISABLED (Auth.RequireAuthentication = false); use this only for local development");
            if (settings.Auth.AdminApiKey == "keyframeadmin") _Logging.Warn(Header + "the admin key is the default; change Auth.AdminApiKey or set " + SettingsManager.AdminKeyVariable + " before sharing this server");

            await _Jobs.StartAsync(token).ConfigureAwait(false);
            _Capture.Start();
            _EndpointHealth.Start();
            _Maintenance.Start();

            _Server = BuildWebserver(settings);
            _Routes = new RouteRegistrar(_Server, _Logging);
            new SystemRoutes(_Context).Register(_Routes);
            new AnalysisRoutes(_Context).Register(_Routes);
            new JobRoutes(_Context).Register(_Routes);
            new EndpointRoutes(_Context).Register(_Routes);
            new SettingsRoutes(_Context).Register(_Routes);
            new RequestHistoryRoutes(_Context).Register(_Routes);

            _Server.Start(token);
            _Logging.Info(Header + "listening on " + BaseUrl + " (" + _Routes.Registered.Count + " routes)");
        }

        /// <summary>
        /// Stop listening and stop background services.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task StopAsync(CancellationToken token = default)
        {
            try
            {
                _Server?.Stop();
            }
            catch (Exception ex)
            {
                _Logging.Debug(Header + "stop: " + ex.Message);
            }

            _Maintenance?.Stop();
            _EndpointHealth?.Stop();
            if (_Jobs != null) await _Jobs.StopAsync(token).ConfigureAwait(false);
            if (_Capture != null) await _Capture.StopAsync(token).ConfigureAwait(false);
            if (_Database != null) await _Database.CloseAsync(token).ConfigureAwait(false);
            _Logging.Info(Header + "stopped");
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
                _Server?.Dispose();
                _Maintenance?.Dispose();
                _EndpointHealth?.Dispose();
                _Jobs?.Dispose();
                _Capture?.Dispose();
                _Analysis?.Dispose();
                _Database?.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private Webserver BuildWebserver(KeyframeSettings settings)
        {
            WebserverSettings ws = new WebserverSettings(settings.Rest.Hostname, settings.Rest.Port, settings.Rest.Ssl);
            ws.IO.MaxRequestBodySize = settings.Rest.MaxRequestBodyBytes;
            ws.Headers.DefaultHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Watson's built-in HTTP telemetry is the foundation for the REST surface; the telemetry host subscribes to
            // the Watson meter and activity source. Watson's own /metrics stays off because Radiant serves Prometheus.
            ws.Telemetry.Enable = settings.Telemetry.Enable;
            ws.Telemetry.EnableMetrics = true;
            ws.Telemetry.EnableTraces = true;
            ws.Telemetry.PropagateContext = true;
            ws.Telemetry.Prometheus.Enable = false;

            Webserver server = new Webserver(ws, DefaultRouteAsync);
            server.Settings.Headers.DefaultHeaders.Remove("Host");
            server.Events.Logger = message => _Logging.Debug("[Watson] " + message);
            server.Routes.PreRouting = PreRoutingAsync;
            server.Routes.Preflight = PreflightAsync;
            server.Routes.AuthenticateRequest = AuthenticateAsync;
            server.Routes.PostRouting = PostRoutingAsync;
            server.Routes.Exception = ExceptionAsync;

            server.UseOpenApi(openApi =>
            {
                openApi.Info.Title = "Keyframe API";
                openApi.Info.Version = ServerVersion.Value;
                openApi.Info.Description = "Keyframe generates summaries and extracts typed metadata from images and video. "
                    + "Authenticate with 'Authorization: Bearer <key>' or 'x-api-key: <key>'. Settings, restart, endpoint tests, and request history deletion require the admin key.";
                openApi.DocumentPath = "/openapi.json";
                openApi.SwaggerUiPath = "/swagger";
                openApi.EnableOpenApi = true;
                openApi.EnableSwaggerUi = true;
                openApi.Tags.Add(new OpenApiTag { Name = "System", Description = "Health, home page, and identity." });
                openApi.Tags.Add(new OpenApiTag { Name = "Analysis", Description = "Image and video analysis and media probing." });
                openApi.Tags.Add(new OpenApiTag { Name = "Jobs", Description = "Background video jobs." });
                openApi.Tags.Add(new OpenApiTag { Name = "Endpoints", Description = "Configured model endpoints." });
                openApi.Tags.Add(new OpenApiTag { Name = "Settings", Description = "Server settings and restart (admin)." });
                openApi.Tags.Add(new OpenApiTag { Name = "Request History", Description = "Captured HTTP requests." });
            });

            return server;
        }

        private Task PreRoutingAsync(HttpContextBase context)
        {
            string? requestId = context.Request.Headers["x-request-id"];
            if (String.IsNullOrWhiteSpace(requestId) || requestId.Length > 64) requestId = IdGenerator.GenerateRequestId();

            context.Metadata = new RequestContext { RequestId = requestId, ReceivedUtc = DateTime.UtcNow };
            context.Response.Headers["x-request-id"] = requestId;

            // Watson's OpenAPI and Swagger handlers add their own CORS headers; adding ours too would send
            // Access-Control-Allow-Origin twice, which browsers reject.
            string path = context.Request.Url.RawWithoutQuery ?? String.Empty;
            if (!path.Equals("/openapi.json", StringComparison.OrdinalIgnoreCase) && !path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
                AddCors(context);

            return Task.CompletedTask;
        }

        private async Task PreflightAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 204;
            AddCors(context);
            context.Response.Headers["Access-Control-Max-Age"] = "86400";
            await context.Response.Send(context.Token).ConfigureAwait(false);
        }

        private async Task AuthenticateAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            AuthenticationService auth = _Auth!;
            string? source = context.Request.Source.IpAddress;

            if (auth.IsRateLimited(source))
            {
                context.Response.Headers["Retry-After"] = "60";
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.TooManyRequests, 429, "Too many failed authentication attempts. Retry in a minute.").ConfigureAwait(false);
                KeyframeMetrics.RecordAuth("rate_limited");
                return;
            }

            string? key = AuthenticationService.ExtractKey(context.Request.Headers["Authorization"], context.Request.Headers["x-api-key"]);
            auth.Resolve(key, ctx);
            if (ctx.IsAuthenticated)
            {
                KeyframeMetrics.RecordAuth("success");
                return;
            }

            auth.RecordFailure(source);
            context.Response.Headers["WWW-Authenticate"] = "Bearer";
            await RouteHelpers.ErrorAsync(context, ApiErrorEnum.AuthenticationFailed, 401,
                key == null ? "A key is required. Send 'Authorization: Bearer <key>' or 'x-api-key: <key>'." : "The key is not valid.").ConfigureAwait(false);
        }

        private Task PostRoutingAsync(HttpContextBase context)
        {
            context.Timestamp.End = DateTime.UtcNow;
            string path = context.Request.Url.RawWithoutQuery ?? String.Empty;
            if (context.Request.Method != HttpMethod.OPTIONS && !String.Equals(path, "/favicon.ico", StringComparison.Ordinal))
                _Capture?.Capture(context);

            _Logging.Debug(Header + context.Request.Method + " " + path + " " + context.Response.StatusCode + " (" + (context.Timestamp.TotalMs ?? 0).ToString("F1") + "ms)");
            return Task.CompletedTask;
        }

        private async Task ExceptionAsync(HttpContextBase context, Exception ex)
        {
            _Logging.Warn(Header + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " unhandled: " + ex);
            if (context.Response.ResponseSent) return;
            await RouteHelpers.ErrorAsync(context, ApiErrorEnum.InternalError, 500, "An internal error occurred. Check the server log for request " + RouteHelpers.Context(context).RequestId + ".").ConfigureAwait(false);
        }

        private static async Task DefaultRouteAsync(HttpContextBase context)
        {
            await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "No route matches " + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + ".").ConfigureAwait(false);
        }

        private static void AddCors(HttpContextBase context)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS, HEAD";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, x-api-key, x-request-id, traceparent, tracestate";
            context.Response.Headers["Access-Control-Expose-Headers"] = "x-request-id, Location, Retry-After, x-keyframe-job-id";
        }

        #endregion
    }
}
