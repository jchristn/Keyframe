namespace Keyframe.Core.Settings
{
    using System;

    /// <summary>
    /// Background job settings. Limits apply live.
    /// </summary>
    public class JobSettings
    {
        #region Public-Members

        /// <summary>
        /// Jobs processed concurrently. Default 2. Minimum 1, maximum 64.
        /// </summary>
        public int MaxConcurrentJobs
        {
            get
            {
                return _MaxConcurrentJobs;
            }
            set
            {
                _MaxConcurrentJobs = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Jobs allowed to wait in the queue before new submissions are rejected with 429. Default 100. Minimum 1, maximum 100000.
        /// </summary>
        public int MaxQueuedJobs
        {
            get
            {
                return _MaxQueuedJobs;
            }
            set
            {
                _MaxQueuedJobs = Math.Clamp(value, 1, 100000);
            }
        }

        /// <summary>
        /// Days to keep finished jobs and results. Default 7. Minimum 1, maximum 3650.
        /// </summary>
        public int RetentionDays
        {
            get
            {
                return _RetentionDays;
            }
            set
            {
                _RetentionDays = Math.Clamp(value, 1, 3650);
            }
        }

        /// <summary>
        /// Maximum wait in milliseconds honored for synchronous video requests (Wait = true). Default 600000. Minimum 1000, maximum 3600000.
        /// </summary>
        public int MaxWaitTimeoutMs
        {
            get
            {
                return _MaxWaitTimeoutMs;
            }
            set
            {
                _MaxWaitTimeoutMs = Math.Clamp(value, 1000, 3600000);
            }
        }

        #endregion

        #region Private-Members

        private int _MaxConcurrentJobs = 2;
        private int _MaxQueuedJobs = 100;
        private int _RetentionDays = 7;
        private int _MaxWaitTimeoutMs = 600000;

        #endregion
    }
}
