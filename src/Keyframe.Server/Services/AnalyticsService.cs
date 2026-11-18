namespace Keyframe.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Channels;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Analytics;
    using Keyframe.Core.Database;
    using Keyframe.Core.Models;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Settings;
    using SyslogLogging;

    /// <summary>
    /// Persists analytics for every finished analysis. Completions are queued without blocking the response path
    /// and written by one background writer; write failures are logged and counted, never thrown. Respects
    /// Analytics.Enable live.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AnalyticsService : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Completions dropped because the queue was full.
        /// </summary>
        public long Dropped
        {
            get
            {
                return Interlocked.Read(ref _Dropped);
            }
        }

        /// <summary>
        /// Records waiting to be written.
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

        private const string Header = "[Analytics] ";

        private readonly DatabaseDriverBase _Database;
        private readonly Func<KeyframeSettings> _Settings;
        private readonly LoggingModule _Logging;
        private readonly Channel<AnalysisRecord> _Queue;
        private readonly CancellationTokenSource _Stopping = new CancellationTokenSource();
        private Task? _Writer = null;
        private long _Dropped = 0;
        private int _Writing = 0;
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
        public AnalyticsService(DatabaseDriverBase database, Func<KeyframeSettings> settings, LoggingModule logging, int capacity = 10000)
        {
            _Database = database ?? throw new ArgumentNullException(nameof(database));
            _Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _Logging = logging ?? throw new ArgumentNullException(nameof(logging));

            BoundedChannelOptions options = new BoundedChannelOptions(Math.Clamp(capacity, 100, 1000000))
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            };

            _Queue = Channel.CreateBounded<AnalysisRecord>(options);
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
        /// Queue a finished analysis. Never throws and never blocks. Ignored when analytics are disabled.
        /// </summary>
        /// <param name="completion">Completion.</param>
        public void Record(AnalysisCompletion completion)
        {
            if (completion == null) return;
            try
            {
                if (!_Settings().Analytics.Enable) return;
                AnalysisRecord record = AnalyticsRecordFactory.Build(completion);
                if (!_Queue.Writer.TryWrite(record))
                {
                    Interlocked.Increment(ref _Dropped);
                    KeyframeMetrics.RecordAnalyticsWrite("dropped");
                }
            }
            catch (Exception ex)
            {
                _Logging.Warn(Header + "could not queue analysis " + completion.Response?.Id + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Wait until queued records are written. Used by tests.
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
        /// Stop accepting records and drain the queue, waiting up to 5 seconds.
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

        private async Task WriteLoopAsync(CancellationToken token)
        {
            try
            {
                while (await _Queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
                {
                    AnalysisRecord? record;
                    while (_Queue.Reader.TryRead(out record))
                    {
                        Interlocked.Exchange(ref _Writing, 1);
                        try
                        {
                            await _Database.Analytics.CreateAsync(record, token).ConfigureAwait(false);
                            KeyframeMetrics.RecordAnalyticsWrite("success");
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            KeyframeMetrics.RecordAnalyticsWrite("failure");
                            _Logging.Warn(Header + "could not write analysis " + record.Id + ": " + ex.Message);
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

        #endregion
    }
}
