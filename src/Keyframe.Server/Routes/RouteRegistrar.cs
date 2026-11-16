namespace Keyframe.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Responses;
    using Keyframe.Server.Classes;
    using SyslogLogging;
    using WatsonWebserver;
    using WatsonWebserver.Core;
    using ApiErrorResponse = Keyframe.Core.Responses.ApiErrorResponse;
    using WatsonWebserver.Core.OpenApi;

    /// <summary>
    /// Registers routes with Watson and OpenAPI metadata. Anonymous routes go in the pre-authentication group; keyed
    /// and admin routes go after authentication, and admin routes reject non-admin keys with 403. Every handler runs
    /// inside the shared exception mapping.
    /// </summary>
    public class RouteRegistrar
    {
        #region Public-Members

        /// <summary>
        /// Registered routes as "METHOD path" strings, for diagnostics and tests.
        /// </summary>
        public List<string> Registered { get; } = new List<string>();

        #endregion

        #region Private-Members

        private static readonly Regex _ParameterPattern = new Regex("\\{([A-Za-z0-9_]+)\\}", RegexOptions.Compiled);

        private readonly Webserver _Server;
        private readonly LoggingModule _Logging;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="server">Watson server.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public RouteRegistrar(Webserver server, LoggingModule logging)
        {
            _Server = server ?? throw new ArgumentNullException(nameof(server));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register a route.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path template; "{name}" segments become path parameters.</param>
        /// <param name="auth">Who may call it.</param>
        /// <param name="handler">Handler.</param>
        /// <param name="metadata">OpenAPI metadata. Path parameters and auth responses are added automatically.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public void Add(HttpMethod method, string path, RouteAuthEnum auth, Func<HttpContextBase, Task> handler, OpenApiRouteMetadata metadata)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentNullException(nameof(path));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));

            foreach (Match match in _ParameterPattern.Matches(path))
            {
                metadata.WithParameter(OpenApiParameterMetadata.Path(match.Groups[1].Value, match.Groups[1].Value + " path parameter.", OpenApiSchemaMetadata.String()));
            }

            if (auth != RouteAuthEnum.Anonymous)
            {
                metadata.WithResponse(401, Error("Missing or invalid key."));
                metadata.WithResponse(429, Error("Too many failed authentication attempts from this address."));
                if (auth == RouteAuthEnum.Admin) metadata.WithResponse(403, Error("The admin key is required."));
            }

            metadata.WithDefaultResponse(Error("Error. The body is an ApiErrorResponse."));

            Func<HttpContextBase, Task> wrapped = context => InvokeAsync(context, auth, handler);
            bool parameterized = path.Contains('{');

            if (auth == RouteAuthEnum.Anonymous)
            {
                if (parameterized) _Server.Routes.PreAuthentication.Parameter.Add(method, path, wrapped, null, openApiMetadata: metadata);
                else _Server.Routes.PreAuthentication.Static.Add(method, path, wrapped, null, openApiMetadata: metadata);
            }
            else
            {
                if (parameterized) _Server.Routes.PostAuthentication.Parameter.Add(method, path, wrapped, null, openApiMetadata: metadata);
                else _Server.Routes.PostAuthentication.Static.Add(method, path, wrapped, null, openApiMetadata: metadata);
            }

            Registered.Add(method.ToString() + " " + path);
        }

        /// <summary>
        /// Build route metadata.
        /// </summary>
        /// <param name="operationId">Operation identifier.</param>
        /// <param name="summary">Summary.</param>
        /// <param name="tag">Tag.</param>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRouteMetadata Meta(string operationId, string summary, string tag, string description)
        {
            OpenApiRouteMetadata metadata = OpenApiRouteMetadata.Create(summary, tag).WithDescription(description);
            metadata.OperationId = operationId;
            return metadata;
        }

        /// <summary>
        /// JSON response metadata for a type.
        /// </summary>
        /// <param name="description">Description.</param>
        /// <param name="type">Response type.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiResponseMetadata Json(string description, Type type)
        {
            return OpenApiResponseMetadata.Json(description, OpenApiSchemaFactory.For(type));
        }

        /// <summary>
        /// JSON request body metadata for a type.
        /// </summary>
        /// <param name="type">Request type.</param>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiRequestBodyMetadata Body(Type type, string description)
        {
            return OpenApiRequestBodyMetadata.Json(OpenApiSchemaFactory.For(type), description, true);
        }

        /// <summary>
        /// Error response metadata.
        /// </summary>
        /// <param name="description">Description.</param>
        /// <returns>Metadata.</returns>
        public static OpenApiResponseMetadata Error(string description)
        {
            return OpenApiResponseMetadata.Json(description, OpenApiSchemaFactory.For(typeof(ApiErrorResponse)));
        }

        #endregion

        #region Private-Methods

        private async Task InvokeAsync(HttpContextBase context, RouteAuthEnum auth, Func<HttpContextBase, Task> handler)
        {
            try
            {
                if (auth == RouteAuthEnum.Admin && !RouteHelpers.Context(context).IsAdmin)
                {
                    await RouteHelpers.ErrorAsync(context, ApiErrorEnum.AuthorizationFailed, 403, "This route requires the admin key.").ConfigureAwait(false);
                    return;
                }

                await handler(context).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (context.Token.IsCancellationRequested)
            {
                _Logging.Debug("[Routes] " + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " canceled by the client");
            }
            catch (Exception ex)
            {
                int status = await RouteHelpers.ExceptionAsync(context, ex).ConfigureAwait(false);
                if (status >= 500)
                    _Logging.Warn("[Routes] " + context.Request.Method + " " + context.Request.Url.RawWithoutQuery + " request " + RouteHelpers.Context(context).RequestId + " failed: " + ex);
            }
        }

        #endregion
    }
}
