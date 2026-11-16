namespace Keyframe.Sdk.Responses
{
    using System;
    using System.Text.Json.Serialization;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Job status returned by the jobs API.
    /// </summary>
    public class JobResponse
    {
        #region Public-Members

        /// <summary>
        /// Job identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Lifecycle status.
        /// </summary>
        public JobStatusEnum Status { get; set; } = JobStatusEnum.Queued;

        /// <summary>
        /// Media kind.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Video;

        /// <summary>
        /// Principal that submitted the job.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Source URL, when supplied by URL.
        /// </summary>
        public string? SourceUrl { get; set; } = null;

        /// <summary>
        /// Media size in bytes, once resolved.
        /// </summary>
        public long SizeBytes { get; set; } = 0;

        /// <summary>
        /// Media duration in milliseconds, once probed.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Progress.
        /// </summary>
        public JobProgress Progress { get; set; } = new JobProgress();

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
        /// UTC time the job finished.
        /// </summary>
        public DateTime? CompletedUtc { get; set; } = null;

        /// <summary>
        /// Relative URL of the result, present once the job succeeded.
        /// </summary>
        public string? ResultUrl { get; set; } = null;

        /// <summary>
        /// Whether the job reached a terminal status (Succeeded, Failed, Canceled, or Interrupted).
        /// Not serialized.
        /// </summary>
        [JsonIgnore]
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
    }
}
