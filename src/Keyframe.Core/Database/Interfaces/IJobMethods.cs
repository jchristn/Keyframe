namespace Keyframe.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Job and job result data access methods.
    /// </summary>
    public interface IJobMethods
    {
        /// <summary>
        /// Insert a job.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when job is null.</exception>
        Task<Job> CreateAsync(Job job, CancellationToken token = default);

        /// <summary>
        /// Read a job.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Job, or null when not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<Job?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Update every mutable column of a job. LastUpdateUtc is set to now.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The stored job.</returns>
        /// <exception cref="ArgumentNullException">Thrown when job is null.</exception>
        Task<Job> UpdateAsync(Job job, CancellationToken token = default);

        /// <summary>
        /// Page through jobs, newest first.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        Task<EnumerationResult<Job>> EnumerateAsync(JobFilter filter, CancellationToken token = default);

        /// <summary>
        /// Count jobs in a status.
        /// </summary>
        /// <param name="status">Status.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Count.</returns>
        Task<long> CountByStatusAsync(JobStatusEnum status, CancellationToken token = default);

        /// <summary>
        /// Delete a job and its result.
        /// </summary>
        /// <param name="id">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when the job existed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Mark every queued or running job as interrupted. Called at startup.
        /// </summary>
        /// <param name="message">Error message to record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Jobs updated.</returns>
        Task<int> MarkInterruptedAsync(string message, CancellationToken token = default);

        /// <summary>
        /// Delete terminal jobs (and results) created before a cutoff.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff (UTC).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Jobs deleted.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);

        /// <summary>
        /// Store or replace a job result.
        /// </summary>
        /// <param name="result">Result.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when result is null.</exception>
        Task StoreResultAsync(JobResult result, CancellationToken token = default);

        /// <summary>
        /// Read a job result.
        /// </summary>
        /// <param name="jobId">Job identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result, or null when none is stored.</returns>
        /// <exception cref="ArgumentNullException">Thrown when jobId is null or empty.</exception>
        Task<JobResult?> ReadResultAsync(string jobId, CancellationToken token = default);
    }
}
