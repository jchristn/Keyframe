namespace Keyframe.Core.Database.Interfaces
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Analytics data access methods: analyses and their model call attempts.
    /// </summary>
    public interface IAnalyticsMethods
    {
        /// <summary>
        /// Insert an analysis and its model call attempts (<see cref="AnalysisRecord.Calls"/>) in one transaction.
        /// </summary>
        /// <param name="record">Record.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        /// <exception cref="ArgumentNullException">Thrown when record is null.</exception>
        Task CreateAsync(AnalysisRecord record, CancellationToken token = default);

        /// <summary>
        /// Read one analysis with its model call attempts.
        /// </summary>
        /// <param name="id">Analysis identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Record with Calls filled, or null when not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<AnalysisRecord?> ReadAsync(string id, CancellationToken token = default);

        /// <summary>
        /// List analyses, newest first, without model calls.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Page.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        Task<EnumerationResult<AnalysisRecord>> EnumerateAsync(AnalyticsFilter filter, CancellationToken token = default);

        /// <summary>
        /// Summarize analyses and model call attempts over a range, with gap-filled buckets.
        /// </summary>
        /// <param name="filter">Filter.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Summary.</returns>
        /// <exception cref="ArgumentNullException">Thrown when filter is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the range produces too many buckets.</exception>
        Task<AnalyticsSummary> SummarizeAsync(AnalyticsSummaryFilter filter, CancellationToken token = default);

        /// <summary>
        /// Delete one analysis and its model call attempts.
        /// </summary>
        /// <param name="id">Analysis identifier.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>True when deleted, false when not found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when id is null or empty.</exception>
        Task<bool> DeleteAsync(string id, CancellationToken token = default);

        /// <summary>
        /// Delete analyses (and their model calls) that completed before a cutoff. Used by retention and the bulk delete route.
        /// </summary>
        /// <param name="olderThanUtc">Cutoff (exclusive).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Analyses deleted.</returns>
        Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default);
    }
}
