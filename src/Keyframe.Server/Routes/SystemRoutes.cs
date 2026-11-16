namespace Keyframe.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Threading.Tasks;
    using Keyframe.Core.Responses;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Root page, favicon, health, and whoami.
    /// </summary>
    public class SystemRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;
        private readonly byte[]? _Favicon;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public SystemRoutes(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
            string icon = Path.Combine(AppContext.BaseDirectory, "assets", "glyph.ico");
            if (File.Exists(icon)) _Favicon = File.ReadAllBytes(icon);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Add(HttpMethod.HEAD, "/", RouteAuthEnum.Anonymous, HeadRootAsync,
                RouteRegistrar.Meta("headRoot", "Check server availability", "System", "Returns 200 with no body. Use for lightweight liveness checks.")
                    .WithResponse(200, OpenApiResponseMetadata.Create("Server is reachable.")));

            routes.Add(HttpMethod.GET, "/", RouteAuthEnum.Anonymous, RootAsync,
                RouteRegistrar.Meta("getRoot", "Server home page", "System", "Returns a small HTML page with the version and links to the API documentation.")
                    .WithResponse(200, OpenApiResponseMetadata.Text("HTML home page.")));

            routes.Add(HttpMethod.GET, "/favicon.ico", RouteAuthEnum.Anonymous, FaviconAsync,
                RouteRegistrar.Meta("getFavicon", "Favicon", "System", "Returns the Keyframe favicon (assets/glyph.ico).")
                    .WithResponse(200, OpenApiResponseMetadata.Binary("Icon bytes.", "image/x-icon")));

            routes.Add(HttpMethod.GET, "/v1.0/health", RouteAuthEnum.Anonymous, HealthAsync,
                RouteRegistrar.Meta("getHealth", "Server health", "System", "Version, uptime, ffmpeg availability, job counts, and endpoint health. Anonymous so orchestrators can probe it.")
                    .WithResponse(200, RouteRegistrar.Json("Health.", typeof(HealthResponse))));

            routes.Add(HttpMethod.GET, "/v1.0/whoami", RouteAuthEnum.Key, WhoAmIAsync,
                RouteRegistrar.Meta("getWhoAmI", "Resolve the presented key", "System", "Returns the principal name and whether the key is the admin key. The dashboard uses it to validate a login.")
                    .WithResponse(200, RouteRegistrar.Json("Principal.", typeof(WhoAmIResponse))));
        }

        #endregion

        #region Private-Methods

        private async Task HeadRootAsync(HttpContextBase context)
        {
            context.Response.StatusCode = 200;
            await context.Response.Send(context.Token).ConfigureAwait(false);
        }

        private async Task RootAsync(HttpContextBase context)
        {
            string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>Keyframe</title><link rel=\"icon\" href=\"/favicon.ico\"></head>"
                + "<body style=\"font-family:system-ui,sans-serif;max-width:40rem;margin:3rem auto;padding:0 1rem\">"
                + "<h1>Keyframe</h1><p>Version " + ServerVersion.Value + " (ALPHA). Summaries and typed metadata from images and video.</p>"
                + "<ul><li><a href=\"/v1.0/health\">Health</a></li><li><a href=\"/openapi.json\">OpenAPI document</a></li><li><a href=\"/swagger\">Swagger UI</a></li></ul>"
                + "</body></html>";
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.Send(html, context.Token).ConfigureAwait(false);
        }

        private async Task FaviconAsync(HttpContextBase context)
        {
            if (_Favicon == null)
            {
                context.Response.StatusCode = 404;
                await context.Response.Send(context.Token).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = 200;
            context.Response.ContentType = "image/x-icon";
            context.Response.Headers["Cache-Control"] = "public, max-age=86400";
            await context.Response.Send(_Favicon, context.Token).ConfigureAwait(false);
        }

        private async Task HealthAsync(HttpContextBase context)
        {
            HealthResponse health = new HealthResponse
            {
                Version = ServerVersion.Value,
                StartedUtc = _Context.StartedUtc,
                UptimeSeconds = (long)(DateTime.UtcNow - _Context.StartedUtc).TotalSeconds,
                FfmpegAvailable = _Context.FfmpegVersion != null,
                FfmpegVersion = _Context.FfmpegVersion,
                Database = _Context.Database.Type.ToString(),
                ActiveJobs = _Context.Jobs.ActiveCount,
                QueuedJobs = _Context.Jobs.QueuedCount
            };

            foreach (KeyValuePair<string, bool> kvp in _Context.EndpointHealth.Snapshot()) health.Endpoints[kvp.Key] = kvp.Value;
            health.Status = health.FfmpegAvailable ? "Healthy" : "Degraded";
            await RouteHelpers.JsonAsync(context, 200, health).ConfigureAwait(false);
        }

        private async Task WhoAmIAsync(HttpContextBase context)
        {
            RequestContext ctx = RouteHelpers.Context(context);
            await RouteHelpers.JsonAsync(context, 200, new WhoAmIResponse
            {
                IsAuthenticated = ctx.IsAuthenticated,
                IsAdmin = ctx.IsAdmin,
                PrincipalName = ctx.PrincipalName,
                AuthenticationRequired = _Context.Settings.Auth.RequireAuthentication,
                Version = ServerVersion.Value
            }).ConfigureAwait(false);
        }

        #endregion
    }
}
