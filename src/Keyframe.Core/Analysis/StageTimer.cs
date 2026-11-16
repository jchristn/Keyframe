namespace Keyframe.Core.Analysis
{
    using System;
    using System.Diagnostics;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Observability;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Times one pipeline stage. Records a stage timing, a metric, and a span when disposed.
    /// Thread safety: use from one logical flow; dispose once.
    /// </summary>
    public class StageTimer : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Stage.
        /// </summary>
        public AnalysisStageEnum Stage { get; }

        #endregion

        #region Private-Members

        private readonly AnalysisContext _Context;
        private readonly StageTiming _Timing;
        private readonly Stopwatch _Stopwatch;
        private readonly Activity? _Activity;
        private bool _Success = true;
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Start timing a stage.
        /// </summary>
        /// <param name="context">Analysis context.</param>
        /// <param name="stage">Stage.</param>
        /// <exception cref="ArgumentNullException">Thrown when context is null.</exception>
        public StageTimer(AnalysisContext context, AnalysisStageEnum stage)
        {
            ArgumentNullException.ThrowIfNull(context);
            _Context = context;
            Stage = stage;
            _Timing = new StageTiming { Name = stage.ToString(), StartUtc = DateTime.UtcNow };
            _Stopwatch = Stopwatch.StartNew();
            _Activity = KeyframeTelemetry.Source.StartActivity("stage:" + stage.ToString(), ActivityKind.Internal);
            _Activity?.SetTag("keyframe.media_kind", context.MediaKindLabel);
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Mark the stage as failed.
        /// </summary>
        /// <param name="reason">Failure reason recorded on the span.</param>
        public void Fail(string? reason = null)
        {
            _Success = false;
            _Activity?.SetStatus(ActivityStatusCode.Error, reason);
        }

        /// <summary>
        /// Stop timing and record the stage.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Private-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            _Disposed = true;
            _Stopwatch.Stop();
            _Timing.DurationMs = Math.Round(_Stopwatch.Elapsed.TotalMilliseconds, 2);
            _Timing.Success = _Success;
            _Context.AddStage(_Timing);
            KeyframeMetrics.RecordStage(_Context.MediaKindLabel, Stage.ToString().ToLowerInvariant(), _Stopwatch.Elapsed.TotalSeconds);
            if (disposing) _Activity?.Dispose();
        }

        #endregion
    }
}
