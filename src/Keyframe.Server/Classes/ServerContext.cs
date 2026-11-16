namespace Keyframe.Server.Classes
{
    using System;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Database;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Services;
    using SyslogLogging;

    /// <summary>
    /// Services shared by route groups. Built once by the server.
    /// </summary>
    public class ServerContext
    {
        #region Public-Members

        /// <summary>
        /// Settings manager.
        /// </summary>
        public SettingsManager SettingsManager { get; }

        /// <summary>
        /// Current effective settings.
        /// </summary>
        public KeyframeSettings Settings
        {
            get
            {
                return SettingsManager.Settings;
            }
        }

        /// <summary>
        /// Database.
        /// </summary>
        public DatabaseDriverBase Database { get; }

        /// <summary>
        /// Analysis services.
        /// </summary>
        public AnalysisServices Analysis { get; }

        /// <summary>
        /// Authentication.
        /// </summary>
        public AuthenticationService Authentication { get; }

        /// <summary>
        /// Jobs.
        /// </summary>
        public JobService Jobs { get; }

        /// <summary>
        /// Endpoint health.
        /// </summary>
        public EndpointHealthService EndpointHealth { get; }

        /// <summary>
        /// Request history capture.
        /// </summary>
        public RequestHistoryCaptureService RequestHistory { get; }

        /// <summary>
        /// Analytics persistence.
        /// </summary>
        public AnalyticsService Analytics { get; }

        /// <summary>
        /// Logging.
        /// </summary>
        public LoggingModule Logging { get; }

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartedUtc { get; } = DateTime.UtcNow;

        /// <summary>
        /// ffmpeg version line, or null when ffmpeg was not found.
        /// </summary>
        public string? FfmpegVersion { get; set; } = null;

        /// <summary>
        /// Invoked by the restart route after the response is sent.
        /// </summary>
        public Action? RequestRestart { get; set; } = null;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settingsManager">Settings manager.</param>
        /// <param name="database">Database.</param>
        /// <param name="analysis">Analysis services.</param>
        /// <param name="authentication">Authentication.</param>
        /// <param name="jobs">Jobs.</param>
        /// <param name="endpointHealth">Endpoint health.</param>
        /// <param name="requestHistory">Request history capture.</param>
        /// <param name="analytics">Analytics persistence.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public ServerContext(
            SettingsManager settingsManager,
            DatabaseDriverBase database,
            AnalysisServices analysis,
            AuthenticationService authentication,
            JobService jobs,
            EndpointHealthService endpointHealth,
            RequestHistoryCaptureService requestHistory,
            AnalyticsService analytics,
            LoggingModule logging)
        {
            SettingsManager = settingsManager ?? throw new ArgumentNullException(nameof(settingsManager));
            Database = database ?? throw new ArgumentNullException(nameof(database));
            Analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
            Authentication = authentication ?? throw new ArgumentNullException(nameof(authentication));
            Jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
            EndpointHealth = endpointHealth ?? throw new ArgumentNullException(nameof(endpointHealth));
            RequestHistory = requestHistory ?? throw new ArgumentNullException(nameof(requestHistory));
            Analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion
    }
}
