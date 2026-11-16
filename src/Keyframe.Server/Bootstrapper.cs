namespace Keyframe.Server
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Classes;
    using Keyframe.Server.Observability;
    using SyslogLogging;
    using LoggingSettings = Keyframe.Core.Settings.LoggingSettings;

    /// <summary>
    /// Composes and runs the server: settings, logging, telemetry, and the Keyframe host. Exits on SIGINT, SIGTERM,
    /// or an admin restart request.
    /// </summary>
    public static class Bootstrapper
    {
        #region Public-Methods

        /// <summary>
        /// Run the server.
        /// </summary>
        /// <param name="args">Command-line arguments. Supports "--settings &lt;path&gt;" and "--help".</param>
        /// <returns>Process exit code: 0 on a clean stop or restart, 1 on a startup failure.</returns>
        public static async Task<int> RunAsync(string[] args)
        {
            if (Array.Exists(args, a => a == "--help" || a == "-h"))
            {
                Console.WriteLine("Keyframe.Server " + ServerVersion.Value);
                Console.WriteLine("Usage: Keyframe.Server [--settings <path>]");
                Console.WriteLine("Settings file defaults to " + SettingsManager.SettingsFileVariable + " or ./keyframe.json.");
                return 0;
            }

            string settingsFile = ResolveSettingsFile(args);
            Console.WriteLine("Keyframe " + ServerVersion.Value + " (ALPHA)");
            Console.WriteLine("Settings file: " + settingsFile);

            SettingsManager settings;
            try
            {
                settings = new SettingsManager(settingsFile);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Unable to load settings: " + ex.Message);
                return 1;
            }

            LoggingModule logging = BuildLogging(settings.Settings.Logging);
            using (TelemetryHost telemetry = TelemetryHost.Start(settings.Settings.Telemetry))
            using (KeyframeServer server = new KeyframeServer(settings, logging))
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                telemetry.AttachLogging(logging);
                if (telemetry.IsRunning) logging.Info("[Bootstrapper] telemetry export enabled" + (telemetry.PrometheusUrl != null ? ", Prometheus at " + telemetry.PrometheusUrl : String.Empty));
                else logging.Info("[Bootstrapper] " + telemetry.StatusMessage);

                server.RestartRequested += (sender, e) => stop.Cancel();
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };

                using (PosixSignalRegistration term = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    stop.Cancel();
                }))
                {
                    try
                    {
                        await server.StartAsync(stop.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        logging.Alert("[Bootstrapper] startup failed: " + ex.Message);
                        Console.Error.WriteLine("Startup failed: " + ex);
                        return 1;
                    }

                    try
                    {
                        await Task.Delay(Timeout.Infinite, stop.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }

                    logging.Info("[Bootstrapper] stopping");
                    await server.StopAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }

            return 0;
        }

        /// <summary>
        /// Build the logging module from settings.
        /// </summary>
        /// <param name="settings">Logging settings.</param>
        /// <returns>Logging module.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static LoggingModule BuildLogging(LoggingSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            List<SyslogServer> servers = new List<SyslogServer>();
            foreach (SyslogServerSettings server in settings.Servers)
            {
                servers.Add(new SyslogServer { Hostname = server.Hostname, Port = server.Port });
            }

            LoggingModule logging = servers.Count > 0 ? new LoggingModule(servers) : new LoggingModule();
            logging.Settings.EnableConsole = settings.ConsoleLogging;
            logging.Settings.EnableColors = settings.EnableColors;
            logging.Settings.MinimumSeverity = (Severity)settings.MinimumSeverity;

            if (!String.IsNullOrWhiteSpace(settings.LogDirectory) && !String.IsNullOrWhiteSpace(settings.LogFilename))
            {
                Directory.CreateDirectory(settings.LogDirectory);
                logging.Settings.FileLogging = FileLoggingMode.FileWithDate;
                logging.Settings.LogFilename = Path.Combine(settings.LogDirectory, settings.LogFilename);
            }

            return logging;
        }

        #endregion

        #region Private-Methods

        private static string ResolveSettingsFile(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--settings") return args[i + 1];
            }

            string? fromEnvironment = Environment.GetEnvironmentVariable(SettingsManager.SettingsFileVariable);
            if (!String.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment;
            return "keyframe.json";
        }

        #endregion
    }
}
