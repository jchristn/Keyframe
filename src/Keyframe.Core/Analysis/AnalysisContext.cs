namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Collects telemetry, warnings, and errors for one analysis, and reports progress for jobs.
    /// Thread safety: safe for concurrent use; model calls from parallel scenes are recorded under a lock.
    /// </summary>
    public class AnalysisContext
    {
        #region Public-Members

        /// <summary>
        /// Media kind analyzed.
        /// </summary>
        public MediaKindEnum MediaKind { get; }

        /// <summary>
        /// Low-cardinality media kind label for metrics.
        /// </summary>
        public string MediaKindLabel
        {
            get
            {
                return MediaKind.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Optional progress callback for jobs.
        /// </summary>
        public Action<JobProgress>? Progress { get; set; } = null;

        /// <summary>
        /// Where the analysis came from. Null when the host supplied nothing.
        /// </summary>
        public AnalysisOrigin? Origin { get; set; } = null;

        /// <summary>
        /// Media size in bytes, set once the media is resolved.
        /// </summary>
        public long MediaBytes { get; set; } = 0;

        /// <summary>
        /// Media duration in milliseconds, set once video or audio is probed.
        /// </summary>
        public long? MediaDurationMs { get; set; } = null;

        /// <summary>
        /// Vision endpoint name, when used.
        /// </summary>
        public string? VisionEndpoint { get; set; } = null;

        /// <summary>
        /// Vision model, when used.
        /// </summary>
        public string? VisionModel { get; set; } = null;

        /// <summary>
        /// Text endpoint name, when used.
        /// </summary>
        public string? TextEndpoint { get; set; } = null;

        /// <summary>
        /// Text model, when used.
        /// </summary>
        public string? TextModel { get; set; } = null;

        /// <summary>
        /// Transcription endpoint name, when used.
        /// </summary>
        public string? TranscriptionEndpoint { get; set; } = null;

        /// <summary>
        /// Transcription model, when used.
        /// </summary>
        public string? TranscriptionModel { get; set; } = null;

        #endregion

        #region Private-Members

        private readonly object _Lock = new object();
        private readonly AnalysisTelemetry _Telemetry = new AnalysisTelemetry();
        private readonly List<string> _Warnings = new List<string>();
        private readonly List<string> _Errors = new List<string>();
        private readonly Stopwatch _Stopwatch = Stopwatch.StartNew();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="mediaKind">Media kind.</param>
        public AnalysisContext(MediaKindEnum mediaKind)
        {
            MediaKind = mediaKind;
            _Telemetry.StartUtc = DateTime.UtcNow;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Start timing a stage.
        /// </summary>
        /// <param name="stage">Stage.</param>
        /// <returns>Timer; dispose to record.</returns>
        public StageTimer BeginStage(AnalysisStageEnum stage)
        {
            return new StageTimer(this, stage);
        }

        /// <summary>
        /// Record a stage timing.
        /// </summary>
        /// <param name="timing">Timing.</param>
        public void AddStage(StageTiming timing)
        {
            ArgumentNullException.ThrowIfNull(timing);
            lock (_Lock) { _Telemetry.Stages.Add(timing); }
        }

        /// <summary>
        /// Record a model call.
        /// </summary>
        /// <param name="call">Call telemetry.</param>
        public void AddModelCall(ModelCallTelemetry call)
        {
            ArgumentNullException.ThrowIfNull(call);
            lock (_Lock) { _Telemetry.ModelCalls.Add(call); }
        }

        /// <summary>
        /// Record a warning.
        /// </summary>
        /// <param name="warning">Warning.</param>
        public void AddWarning(string warning)
        {
            if (String.IsNullOrWhiteSpace(warning)) return;
            lock (_Lock) { _Warnings.Add(warning); }
        }

        /// <summary>
        /// Record an error.
        /// </summary>
        /// <param name="error">Error.</param>
        public void AddError(string error)
        {
            if (String.IsNullOrWhiteSpace(error)) return;
            lock (_Lock) { _Errors.Add(error); }
        }

        /// <summary>
        /// Whether any error has been recorded.
        /// </summary>
        /// <returns>True when errors exist.</returns>
        public bool HasErrors()
        {
            lock (_Lock) { return _Errors.Count > 0; }
        }

        /// <summary>
        /// Report job progress. Best effort; callback failures are ignored.
        /// </summary>
        /// <param name="stage">Stage name.</param>
        /// <param name="completed">Units completed.</param>
        /// <param name="total">Total units.</param>
        /// <param name="percent">Overall percent, 0 to 100.</param>
        public void ReportProgress(string stage, int completed, int total, double percent)
        {
            Action<JobProgress>? callback = Progress;
            if (callback == null) return;
            try
            {
                callback(new JobProgress
                {
                    Stage = stage,
                    Completed = completed,
                    Total = total,
                    Percent = Math.Round(Math.Clamp(percent, 0.0, 100.0), 1)
                });
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Finish the analysis and copy telemetry, warnings, and errors into a response.
        /// </summary>
        /// <param name="response">Response to fill.</param>
        public void CompleteInto(AnalysisResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);
            _Stopwatch.Stop();

            lock (_Lock)
            {
                _Telemetry.EndUtc = DateTime.UtcNow;
                _Telemetry.TotalMs = Math.Round(_Stopwatch.Elapsed.TotalMilliseconds, 2);
                _Telemetry.ModelCalls = _Telemetry.ModelCalls.OrderBy(c => c.StartUtc).ToList();

                TokenTotals totals = new TokenTotals();
                foreach (ModelCallTelemetry call in _Telemetry.ModelCalls)
                {
                    totals.ModelCalls++;
                    if (!call.Success) totals.FailedModelCalls++;
                    totals.ModelMs += call.DurationMs;
                    totals.PromptTokens += call.PromptTokens ?? 0;
                    totals.CompletionTokens += call.CompletionTokens ?? 0;
                    totals.TotalTokens += call.TotalTokens ?? ((call.PromptTokens ?? 0) + (call.CompletionTokens ?? 0));
                    totals.CachedTokens += call.CachedTokens ?? 0;
                    totals.ReasoningTokens += call.ReasoningTokens ?? 0;
                    totals.InputImages += call.InputImages;
                    totals.InputImageBytes += call.InputImageBytes;
                }

                totals.ModelMs = Math.Round(totals.ModelMs, 2);
                _Telemetry.Totals = totals;
                if (Origin != null)
                {
                    _Telemetry.QueueWaitMs = Math.Round(Origin.QueueWaitMs, 2);
                    if (Origin.JobId != null) _Telemetry.QueuedMs = _Telemetry.QueueWaitMs;
                    if (response.RequestId == null) response.RequestId = Origin.RequestId;
                }

                response.Telemetry = _Telemetry;
                response.Warnings.AddRange(_Warnings);
                response.Errors.AddRange(_Errors);
            }
        }

        /// <summary>
        /// Build the completion record handed to the host. Call after <see cref="CompleteInto"/>.
        /// </summary>
        /// <param name="response">Completed response.</param>
        /// <param name="exception">Message of the exception that ended the analysis, or null.</param>
        /// <param name="canceled">Whether the analysis was canceled.</param>
        /// <param name="scenes">Scenes in the result.</param>
        /// <param name="transcript">Transcript, when transcription ran.</param>
        /// <returns>Completion.</returns>
        /// <exception cref="ArgumentNullException">Thrown when response is null.</exception>
        public AnalysisCompletion BuildCompletion(AnalysisResponse response, string? exception, bool canceled, int scenes, TranscriptResult? transcript)
        {
            ArgumentNullException.ThrowIfNull(response);
            return new AnalysisCompletion
            {
                Response = response,
                MediaKind = MediaKind,
                Origin = Origin,
                Exception = exception,
                Canceled = canceled,
                MediaBytes = MediaBytes,
                MediaDurationMs = MediaDurationMs,
                VisionEndpoint = VisionEndpoint,
                VisionModel = VisionModel,
                TextEndpoint = TextEndpoint,
                TextModel = TextModel,
                TranscriptionEndpoint = TranscriptionEndpoint,
                TranscriptionModel = TranscriptionModel,
                Scenes = scenes,
                TranscriptCharacters = transcript?.Text?.Length ?? 0
            };
        }

        #endregion
    }
}
