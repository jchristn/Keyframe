namespace Keyframe.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database;
    using Keyframe.Core.Media;
    using Keyframe.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Hourly housekeeping: prunes request history, finished jobs, and analytics past retention, removes orphaned media
    /// workspaces, and sweeps expired authentication failure windows.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class MaintenanceService : IDisposable
    {
        #region Private-Members

        private const string Header = "[Maintenance] ";

        private readonly DatabaseDriverBase _Database;
        private readonly Func<KeyframeSettings> _Settings;
        private readonly AuthenticationService _Auth;
        private readonly LoggingModule _Logging;
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Loop = null;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="database">Database.</param>
        /// <param name="settings">Live settings accessor.</param>
        /// <param name="auth">Authentication service.</param>
        /// <param name="logging">Logging.</param>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public MaintenanceService(DatabaseDriverBase database, Func<KeyframeSettings> settings, AuthenticationService auth, LoggingModule logging)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Auth = auth ?? throw new ArgumentNullException(nameof(auth));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start the hourly loop. The first pass runs one minute after start.
        /// </summary>
        public void Start()
        {
            if (_Loop != null) return;
            _Loop = Task.Run(() => LoopAsync(_Stopping.Token));
        }

        /// <summary>
        /// Stop the loop.
        /// </summary>
        public void Stop()
        {
            _Stopping.Cancel();
        }

        /// <summary>
        /// Run one maintenance pass now.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task RunOnceAsync(CancellationToken token = default)
        {
            KeyframeSettings settings = _Settings();
            try
            {
                int history = await _Database.RequestHistory.PruneAsync(DateTime.UtcNow.AddDays(-settings.RequestHistory.RetentionDays), token).ConfigureAwait(false);
                int jobs = await _Database.Jobs.PruneAsync(DateTime.UtcNow.AddDays(-settings.Jobs.RetentionDays), token).ConfigureAwait(false);
                int analytics = await _Database.Analytics.PruneAsync(DateTime.UtcNow.AddDays(-settings.Analytics.RetentionDays), token).ConfigureAwait(false);
                int workspaces = MediaWorkspace.CleanupOrphans(settings.Media.TempDirectory, TimeSpan.FromHours(6));
                _Auth.Sweep();
                if (history + jobs + analytics + workspaces > 0)
                    _Logging.Info(Header + "pruned " + history + " request history row(s), " + jobs + " job(s), " + analytics + " analytics row(s), " + workspaces + " workspace(s)");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _Logging.Warn(Header + "pass failed: " + ex.Message);
            }
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

        #region Private-Methods

        private async Task LoopAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), token).ConfigureAwait(false);
                while (!token.IsCancellationRequested)
                {
                    await RunOnceAsync(token).ConfigureAwait(false);
                    await Task.Delay(TimeSpan.FromHours(1), token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        #endregion
    }
}
