namespace Keyframe.Core.Requests
{
    using System;

    /// <summary>
    /// Filter for listing, summarizing, and bulk-deleting request history.
    /// </summary>
    public class RequestHistoryFilter
    {
        #region Public-Members

        /// <summary>
        /// Principal (access key name) to match exactly.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// HTTP method to match, case-insensitive.
        /// </summary>
        public string? Method { get; set; } = null;

        /// <summary>
        /// Status code to match exactly.
        /// </summary>
        public int? StatusCode { get; set; } = null;

        /// <summary>
        /// Substring the path must contain.
        /// </summary>
        public string? PathContains { get; set; } = null;

        /// <summary>
        /// Inclusive lower bound on CreatedUtc.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive upper bound on CreatedUtc.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// One-based page number. Default 1. Minimum 1.
        /// </summary>
        public int PageNumber
        {
            get
            {
                return _PageNumber;
            }
            set
            {
                _PageNumber = Math.Max(1, value);
            }
        }

        /// <summary>
        /// Page size. Default 25. Minimum 1, maximum 1000.
        /// </summary>
        public int PageSize
        {
            get
            {
                return _PageSize;
            }
            set
            {
                _PageSize = Math.Clamp(value, 1, 1000);
            }
        }

        /// <summary>
        /// Bucket width for summaries, in minutes. Default 60. Minimum 1, maximum 10080.
        /// </summary>
        public int BucketMinutes
        {
            get
            {
                return _BucketMinutes;
            }
            set
            {
                _BucketMinutes = Math.Clamp(value, 1, 10080);
            }
        }

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;
        private int _BucketMinutes = 60;

        #endregion
    }
}
