namespace Keyframe.Core.Settings
{
    using System;

    /// <summary>
    /// Request capture settings. Changes apply live.
    /// </summary>
    public class RequestHistorySettings
    {
        #region Public-Members

        /// <summary>
        /// Whether request capture is enabled. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Maximum request body bytes captured before truncation. Default 65536. Minimum 0, maximum 1048576.
        /// </summary>
        public int MaxRequestBodyBytes
        {
            get
            {
                return _MaxRequestBodyBytes;
            }
            set
            {
                _MaxRequestBodyBytes = Math.Clamp(value, 0, 1048576);
            }
        }

        /// <summary>
        /// Maximum response body bytes captured before truncation. Default 65536. Minimum 0, maximum 1048576.
        /// </summary>
        public int MaxResponseBodyBytes
        {
            get
            {
                return _MaxResponseBodyBytes;
            }
            set
            {
                _MaxResponseBodyBytes = Math.Clamp(value, 0, 1048576);
            }
        }

        /// <summary>
        /// Days to keep captured requests. Default 30. Minimum 1, maximum 3650.
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

        #endregion

        #region Private-Members

        private int _MaxRequestBodyBytes = 65536;
        private int _MaxResponseBodyBytes = 65536;
        private int _RetentionDays = 30;

        #endregion
    }
}
