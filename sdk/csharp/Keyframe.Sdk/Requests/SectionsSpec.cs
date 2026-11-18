namespace Keyframe.Sdk.Requests
{
    /// <summary>
    /// Audio sections: split the recording at silences and run Summarize and Extract on each section. The audio
    /// counterpart of <see cref="ScenesSpec"/>.
    /// </summary>
    public class SectionsSpec
    {
        #region Public-Members

        /// <summary>
        /// Detect sections. Default false.
        /// </summary>
        public bool Enable { get; set; } = false;

        /// <summary>
        /// Detection options. Never null.
        /// </summary>
        public SectionDetectionSpec Detection
        {
            get
            {
                return _Detection;
            }
            set
            {
                _Detection = value ?? new SectionDetectionSpec();
            }
        }

        /// <summary>
        /// Per-section summary. Never null. Off by default.
        /// </summary>
        public SummarizeSpec Summarize
        {
            get
            {
                return _Summarize;
            }
            set
            {
                _Summarize = value ?? new SummarizeSpec();
            }
        }

        /// <summary>
        /// Per-section field extraction. Never null. Off by default.
        /// </summary>
        public ExtractSpec Extract
        {
            get
            {
                return _Extract;
            }
            set
            {
                _Extract = value ?? new ExtractSpec();
            }
        }

        #endregion

        #region Private-Members

        private SectionDetectionSpec _Detection = new SectionDetectionSpec();
        private SummarizeSpec _Summarize = new SummarizeSpec();
        private ExtractSpec _Extract = new ExtractSpec();

        #endregion
    }
}
