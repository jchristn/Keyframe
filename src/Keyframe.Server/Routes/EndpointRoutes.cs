namespace Keyframe.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Responses;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;

    /// <summary>
    /// Configured model endpoints and connectivity tests.
    /// </summary>
    public class EndpointRoutes
    {
        #region Private-Members

        private readonly ServerContext _Context;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="context">Server context.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public EndpointRoutes(ServerContext context)
        {
            _Context = context ?? throw new ArgumentNullException(nameof(context));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register routes.
        /// </summary>
        /// <param name="routes">Registrar.</param>
        public void Register(RouteRegistrar routes)
        {
            routes.Add(HttpMethod.GET, "/v1.0/endpoints", RouteAuthEnum.Key, ListAsync,
                RouteRegistrar.Meta("listEndpoints", "List model endpoints", "Endpoints", "Configured completion and transcription endpoints with API keys masked, provider availability, capabilities (SupportsImages, SupportsStructuredOutput, SupportsTranscription, SupportsAudioInput), and last observed health. Edit endpoints through the settings API.")
                    .WithResponse(200, RouteRegistrar.Json("Endpoints.", typeof(List<EndpointResponse>))));

            routes.Add(HttpMethod.POST, "/v1.0/endpoints/{name}/test", RouteAuthEnum.Admin, TestAsync,
                RouteRegistrar.Meta("testEndpoint", "Test an endpoint", "Endpoints", "Sends a one-token prompt (completion endpoints) and records the result as the endpoint's health.")
                    .WithResponse(200, RouteRegistrar.Json("Test result.", typeof(EndpointTestResponse)))
                    .WithResponse(404, RouteRegistrar.Error("Endpoint not found.")));
        }

        #endregion

        #region Private-Methods

        private async Task ListAsync(HttpContextBase context)
        {
            await RouteHelpers.JsonAsync(context, 200, _Context.EndpointHealth.Describe()).ConfigureAwait(false);
        }

        private async Task TestAsync(HttpContextBase context)
        {
            string name = RouteHelpers.Param(context, "name") ?? String.Empty;
            EndpointTestResponse? result = await _Context.EndpointHealth.TestAsync(name, context.Token).ConfigureAwait(false);
            if (result == null)
            {
                await RouteHelpers.ErrorAsync(context, ApiErrorEnum.NotFound, 404, "Endpoint '" + name + "' is not configured.").ConfigureAwait(false);
                return;
            }

            await RouteHelpers.JsonAsync(context, 200, result).ConfigureAwait(false);
        }

        #endregion
    }
}
