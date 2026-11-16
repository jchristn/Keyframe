namespace Keyframe.Sdk
{
    using System;
    using Keyframe.Sdk.Responses;

    /// <summary>
    /// Thrown by wait helpers when a job reaches a terminal status other than Succeeded
    /// (Failed, Canceled, or Interrupted).
    /// </summary>
    public class KeyframeJobException : Exception
    {
        #region Public-Members

        /// <summary>
        /// The job as last observed. Never null.
        /// </summary>
        public JobResponse Job { get; }

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Create an exception for a job that did not succeed.
        /// </summary>
        /// <param name="job">Job in its terminal state.</param>
        /// <exception cref="ArgumentNullException">Thrown when job is null.</exception>
        public KeyframeJobException(JobResponse job)
            : base(BuildMessage(job))
        {
            ArgumentNullException.ThrowIfNull(job);
            Job = job;
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(JobResponse job)
        {
            if (job == null) return "Job did not succeed.";
            return "Job " + job.Id + " finished with status " + job.Status + (job.Error != null ? ": " + job.Error : ".");
        }

        #endregion
    }
}
