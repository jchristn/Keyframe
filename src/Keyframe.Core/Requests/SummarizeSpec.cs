namespace Keyframe.Core.Requests
{
    using System;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Summarize function options.
    /// </summary>
    public class SummarizeSpec
    {
        #region Public-Members

        /// <summary>
        /// Whether to generate a summary. Default false.
        /// </summary>
        public bool Enable { get; set; } = false;

        /// <summary>
        /// Key under which the summary is returned in Results. Default "Summary".
        /// </summary>
        public string Key { get; set; } = Constants.DefaultSummaryKey;

        /// <summary>
        /// Target maximum words. Default 100. Minimum 5, maximum 2000.
        /// </summary>
        public int MaxWords
        {
            get
            {
                return _MaxWords;
            }
            set
            {
                _MaxWords = Math.Clamp(value, 5, 2000);
            }
        }

        /// <summary>
        /// Summary style. Default Paragraph.
        /// </summary>
        public SummaryStyleEnum Style { get; set; } = SummaryStyleEnum.Paragraph;

        /// <summary>
        /// Additional caller instructions for the summary, for example what to emphasize. Maximum 4000 characters.
        /// </summary>
        public string? Instructions { get; set; } = null;

        #endregion

        #region Private-Members

        private int _MaxWords = 100;

        #endregion
    }
}
