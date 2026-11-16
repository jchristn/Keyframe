namespace Keyframe.Core.Models
{
    using System;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Helpers;

    /// <summary>
    /// A background analysis job. Lifecycle and searchable fields only; the result lives in <see cref="JobResult"/>.
    /// </summary>
    public class Job
    {
        #region Public-Members

        /// <summary>
        /// Job identifier (prefix "kfj_").
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Id
        {
            get
            {
                return _Id;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Id));
                _Id = value;
            }
        }

        /// <summary>
        /// Media kind being analyzed.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Video;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public JobStatusEnum Status { get; set; } = JobStatusEnum.Queued;

        /// <summary>
        /// Access key name that submitted the job.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Source URL when the media was supplied by URL. Null for inline media.
        /// </summary>
        public string? SourceUrl { get; set; } = null;

        /// <summary>
        /// SHA-256 of the media bytes, once resolved.
        /// </summary>
        public string? SourceSha256 { get; set; } = null;

        /// <summary>
        /// Media size in bytes, once resolved.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Media duration in milliseconds, once probed.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Current progress stage label.
        /// </summary>
        public string? ProgressStage { get; set; } = null;

        /// <summary>
        /// Units completed in the current stage.
        /// </summary>
        public int ProgressCompleted { get; set; } = 0;

        /// <summary>
        /// Total units in the current stage.
        /// </summary>
        public int ProgressTotal { get; set; } = 0;

        /// <summary>
        /// Overall percent complete. Minimum 0, maximum 100.
        /// </summary>
        public double ProgressPercent
        {
            get
            {
                return _ProgressPercent;
            }
            set
            {
                _ProgressPercent = Math.Clamp(value, 0, 100);
            }
        }

        /// <summary>
        /// Error message for failed or interrupted jobs.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// UTC time the job was accepted.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// UTC time a worker started the job.
        /// </summary>
        public DateTime? StartedUtc { get; set; } = null;

        /// <summary>
        /// UTC time the job reached a terminal status.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// UTC time of the last update.
        /// </summary>
        public DateTime LastUpdateUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Whether the job is in a terminal status.
        /// </summary>
        public bool IsTerminal
        {
            get
            {
                return Status == JobStatusEnum.Succeeded
                    || Status == JobStatusEnum.Failed
                    || Status == JobStatusEnum.Canceled
                    || Status == JobStatusEnum.Interrupted;
            }
        }

        #endregion

        #region Private-Members

        private string _Id = IdGenerator.GenerateJobId();
        private double _ProgressPercent = 0;

        #endregion
    }
}
