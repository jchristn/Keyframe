namespace Keyframe.Server.Services
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;

    /// <summary>
    /// An accepted job waiting for, or held by, a worker.
    /// </summary>
    public class JobWork : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Job record.
        /// </summary>
        public Job Job { get; }

        /// <summary>
        /// Request to run.
        /// </summary>
        public VideoAnalysisRequest Request { get; }

        /// <summary>
        /// Cancellation for this job.
        /// </summary>
        public CancellationTokenSource Cancellation { get; } = new CancellationTokenSource();

        /// <summary>
        /// Completes when the job reaches a terminal status.
        /// </summary>
        public TaskCompletionSource<Job> Completion { get; } = new TaskCompletionSource<Job>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Set when the caller canceled, as opposed to a server shutdown.
        /// </summary>
        public bool CanceledByCaller { get; set; } = false;

        /// <summary>
        /// UTC time progress was last persisted.
        /// </summary>
        public DateTime LastPersistUtc { get; set; } = DateTime.MinValue;

        #endregion

        #region Private-Members

        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="job">Job.</param>
        /// <param name="request">Request.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public JobWork(Job job, VideoAnalysisRequest request)
        {
            Job = job ?? throw new ArgumentNullException(nameof(job));
            Request = request ?? throw new ArgumentNullException(nameof(request));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Methods

        /// <summary>
        /// Dispose pattern implementation.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing) Cancellation.Dispose();
            _Disposed = true;
        }

        #endregion
    }
}
