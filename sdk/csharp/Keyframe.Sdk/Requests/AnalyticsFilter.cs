namespace Keyframe.Sdk.Requests
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Filter and paging for listing analytics records. Time bounds apply to CompletedUtc.
    /// </summary>
    public class AnalyticsFilter
    {
        #region Public-Members

        /// <summary>
        /// Media kind to match.
        /// </summary>
        public MediaKindEnum? MediaKind { get; set; } = null;

        /// <summary>
        /// Success to match.
        /// </summary>
        public bool? Success { get; set; } = null;

        /// <summary>
        /// Inclusive lower bound on CompletedUtc.
        /// </summary>
        public DateTime? FromUtc { get; set; } = null;

        /// <summary>
        /// Exclusive upper bound on CompletedUtc.
        /// </summary>
        public DateTime? ToUtc { get; set; } = null;

        /// <summary>
        /// Records to skip. Default 0. Minimum 0.
        /// </summary>
        public int Skip
        {
            get
            {
                return _Skip;
            }
            set
            {
                _Skip = Math.Max(0, value);
            }
        }

        /// <summary>
        /// Maximum records to return. Default 50. Minimum 1, maximum 1000.
        /// </summary>
        public int MaxResults
        {
            get
            {
                return _MaxResults;
            }
            set
            {
                _MaxResults = Math.Clamp(value, 1, 1000);
            }
        }

        #endregion

        #region Private-Members

        private int _Skip = 0;
        private int _MaxResults = 50;

        #endregion
    }
}
