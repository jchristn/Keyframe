namespace Keyframe.Core.Analysis
{
    using System;

    /// <summary>
    /// Where an analysis came from: the HTTP request, the job, and the caller. Supplied by the host and copied into
    /// the response telemetry and the analytics record.
    /// </summary>
    public class AnalysisOrigin
    {
        #region Public-Members

        /// <summary>
        /// HTTP request identifier, when known.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Job identifier for queued analyses (video and audio). Null for synchronous image analyses.
        /// </summary>
        public string? JobId { get; set; } = null;

        /// <summary>
        /// Name of the access key that submitted the analysis, when known.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Time from job queued to job started in milliseconds. Default 0. Minimum 0.
        /// </summary>
        public double QueueWaitMs
        {
            get
            {
                return _QueueWaitMs;
            }
            set
            {
                _QueueWaitMs = Math.Max(0, value);
            }
        }

        #endregion

        #region Private-Members

        private double _QueueWaitMs = 0;

        #endregion
    }
}
