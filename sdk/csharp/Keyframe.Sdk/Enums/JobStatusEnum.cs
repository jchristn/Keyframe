namespace Keyframe.Sdk.Enums
{
    /// <summary>
    /// Lifecycle state of an analysis job.
    /// </summary>
    public enum JobStatusEnum
    {
        /// <summary>
        /// Accepted and waiting for a worker.
        /// </summary>
        Queued,

        /// <summary>
        /// Being processed.
        /// </summary>
        Running,

        /// <summary>
        /// Finished and a result is available.
        /// </summary>
        Succeeded,

        /// <summary>
        /// Finished with an error.
        /// </summary>
        Failed,

        /// <summary>
        /// Canceled by a caller.
        /// </summary>
        Canceled,

        /// <summary>
        /// The server stopped while the job was running.
        /// </summary>
        Interrupted
    }
}
