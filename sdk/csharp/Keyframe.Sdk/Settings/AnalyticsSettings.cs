namespace Keyframe.Sdk.Settings
{
    using System;

    /// <summary>
    /// Analytics persistence settings: one row per finished analysis and one per model call attempt. Changes apply live.
    /// </summary>
    public class AnalyticsSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether analytics rows are written after each analysis. Default true. Existing rows stay readable when disabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Days to keep analytics rows before maintenance prunes them. Default 90. Minimum 1, maximum 3650.
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

        private int _RetentionDays = 90;

        #endregion
    }
}
