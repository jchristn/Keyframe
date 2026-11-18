namespace Keyframe.Sdk.Requests
{
    /// <summary>
    /// Functions to run against audio. When the Functions object is omitted, Transcribe, Summarize, and Measure are
    /// enabled, and Extract and Sections are disabled. Summarize, Extract, and section functions need evidence: the
    /// transcript (Transcribe enabled), the audio itself (Listen.Enable on the request), or both.
    /// </summary>
    public class AudioFunctionsSpec : FunctionsSpec
    {
        #region Public-Members

        /// <summary>
        /// Transcription options. Enabled by default.
        /// </summary>
        public TranscribeSpec Transcribe
        {
            get
            {
                return _Transcribe;
            }
            set
            {
                _Transcribe = value ?? new TranscribeSpec();
            }
        }

        /// <summary>
        /// Deterministic measurements (loudness, levels, silence, stereo width). Never null. On by default.
        /// </summary>
        public MeasureSpec Measure
        {
            get
            {
                return _Measure;
            }
            set
            {
                _Measure = value ?? new MeasureSpec();
            }
        }

        /// <summary>
        /// Audio sections split at silences, with per-section Summarize and Extract. Never null. Off by default.
        /// </summary>
        public SectionsSpec Sections
        {
            get
            {
                return _Sections;
            }
            set
            {
                _Sections = value ?? new SectionsSpec();
            }
        }

        #endregion

        #region Private-Members

        private TranscribeSpec _Transcribe = new TranscribeSpec { Enable = true };
        private MeasureSpec _Measure = new MeasureSpec();
        private SectionsSpec _Sections = new SectionsSpec();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate with Transcribe and Summarize enabled.
        /// </summary>
        public AudioFunctionsSpec()
        {
            Summarize.Enable = true;
        }

        #endregion
    }
}
