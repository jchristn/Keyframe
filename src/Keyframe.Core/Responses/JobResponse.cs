namespace Keyframe.Core.Responses
{
    using System;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;

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

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build a response from a job.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <returns>Response.</returns>
        /// <exception cref="ArgumentNullException">Thrown when job is null.</exception>
        public static JobResponse FromJob(Job job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            return new JobResponse
            {
                Id = job.Id,
                Status = job.Status,
                MediaKind = job.MediaKind,
                PrincipalName = job.PrincipalName,
                SourceUrl = job.SourceUrl,
                SizeBytes = job.SizeBytes,
                DurationMs = job.DurationMs,
                Progress = new JobProgress
                {
                    Stage = job.ProgressStage,
                    Completed = job.ProgressCompleted,
                    Total = job.ProgressTotal,
                    Percent = job.ProgressPercent
                },
                Error = job.Error,
                CreatedUtc = job.CreatedUtc,
                StartedUtc = job.StartedUtc,
                CompletedUtc = job.CompletedUtc,
                ResultUrl = job.Status == JobStatusEnum.Succeeded ? "/v1.0/jobs/" + job.Id + "/result" : null
            };
        }

        #endregion
    }
}
