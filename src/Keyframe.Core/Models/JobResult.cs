namespace Keyframe.Core.Models
{
    using System;

    /// <summary>
    /// Stored result of a finished job.
    /// </summary>
    /// <remarks>
    /// <see cref="ResultJson"/> is an unmanaged JSON column by explicit exception: the result carries
    /// caller-defined field keys and types, so it cannot map to fixed columns. Searchable fields live on <see cref="Job"/>.
    /// </remarks>
    public class JobResult
    {
        #region Public-Members

        /// <summary>
        /// Owning job identifier.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string JobId
        {
            get
            {
                return _JobId;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(JobId));
                _JobId = value;
            }
        }

        /// <summary>
        /// Serialized analysis response.
        /// </summary>
        public string ResultJson { get; set; } = "{}";

        /// <summary>
        /// Length of <see cref="ResultJson"/> in UTF-8 bytes.
        /// </summary>
        public long ResultBytes { get; set; } = 0;

        /// <summary>
        /// UTC time the result was stored.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        #endregion

        #region Private-Members

        private string _JobId = "unset";

        #endregion
    }
}
