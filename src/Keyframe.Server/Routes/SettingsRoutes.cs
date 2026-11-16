namespace Keyframe.Server.Routes
{
    using System;
    using System.Threading.Tasks;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Classes;
    using WatsonWebserver.Core;

    /// <summary>
    /// Settings read and update, and the restart route. Admin only.
    /// </summary>
    public class SettingsRoutes
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
        public SettingsRoutes(ServerContext context)
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
            routes.Add(HttpMethod.GET, "/v1.0/settings", RouteAuthEnum.Admin, GetAsync,
                RouteRegistrar.Meta("getSettings", "Get settings", "Settings", "Settings with secrets masked as **** plus the last four characters, and per-field metadata: ApplyMode (Live or Restart), Secret, and OverriddenByEnvironment.")
                    .WithResponse(200, RouteRegistrar.Json("Settings.", typeof(SettingsResponse))));

            routes.Add(HttpMethod.PUT, "/v1.0/settings", RouteAuthEnum.Admin, PutAsync,
                RouteRegistrar.Meta("updateSettings", "Update settings", "Settings", "Validates, clamps, persists, and applies settings. Send masked secrets back unchanged to keep them. Live fields apply at once; RestartRequired is set when a restart-only field changed.")
                    .WithRequestBody(RouteRegistrar.Body(typeof(KeyframeSettings), "Complete settings object."))
                    .WithResponse(200, RouteRegistrar.Json("Saved settings.", typeof(SettingsResponse)))
                    .WithResponse(400, RouteRegistrar.Error("Invalid settings.")));

            routes.Add(HttpMethod.POST, "/v1.0/restart", RouteAuthEnum.Admin, RestartAsync,
                RouteRegistrar.Meta("restartServer", "Restart the server", "Settings", "Responds, then exits cleanly so the container runtime (restart: unless-stopped) relaunches the server, which reloads settings from disk.")
                    .WithResponse(202, RouteRegistrar.Json("Restart accepted.", typeof(RestartResponse))));
        }

        #endregion

        #region Private-Methods

        private async Task GetAsync(HttpContextBase context)
        {
            await RouteHelpers.JsonAsync(context, 200, _Context.SettingsManager.GetMasked()).ConfigureAwait(false);
        }

        private async Task PutAsync(HttpContextBase context)
        {
            KeyframeSettings incoming = RouteHelpers.Body<KeyframeSettings>(context);
            SettingsResponse saved = await _Context.SettingsManager.UpdateAsync(incoming, context.Token).ConfigureAwait(false);
            _Context.Analysis.Clients.Reset();
            _Context.Logging.Info("[Settings] settings updated by " + (RouteHelpers.Context(context).PrincipalName ?? "unknown") + (saved.RestartRequired ? " (restart required)" : String.Empty));
            await RouteHelpers.JsonAsync(context, 200, saved).ConfigureAwait(false);
        }

        private async Task RestartAsync(HttpContextBase context)
        {
            await RouteHelpers.JsonAsync(context, 202, new RestartResponse()).ConfigureAwait(false);
            _Context.Logging.Alert("[Settings] restart requested by " + (RouteHelpers.Context(context).PrincipalName ?? "unknown"));
            Action? restart = _Context.RequestRestart;
            if (restart != null)
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    restart();
                });
            }
        }

        #endregion
    }
}
