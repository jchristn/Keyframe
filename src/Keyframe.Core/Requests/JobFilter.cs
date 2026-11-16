namespace Keyframe.Core.Requests
{
    using System;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Filter for listing jobs.
    /// </summary>
    public class JobFilter
    {
        #region Public-Members

        /// <summary>
        /// Status to match.
        /// </summary>
        public JobStatusEnum? Status { get; set; } = null;

        /// <summary>
        /// Media kind to match.
        /// </summary>
        public MediaKindEnum? MediaKind { get; set; } = null;

        /// <summary>
        /// Principal (access key name) to match exactly.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

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

        #endregion

        #region Private-Members

        private int _PageNumber = 1;
        private int _PageSize = 25;

        #endregion
    }
}
