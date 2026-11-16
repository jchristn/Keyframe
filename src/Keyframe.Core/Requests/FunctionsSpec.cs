namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Functions to run against an image, or against a whole video.
    /// </summary>
    public class FunctionsSpec
    {
        #region Public-Members

        /// <summary>
        /// Summarize options.
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
        /// Extract options.
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

        private SummarizeSpec _Summarize = new SummarizeSpec();
        private ExtractSpec _Extract = new ExtractSpec();

        #endregion
    }
}
