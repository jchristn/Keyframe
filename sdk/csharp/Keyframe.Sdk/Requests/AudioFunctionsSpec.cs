namespace Keyframe.Sdk.Requests
{
    /// <summary>
    /// Functions to run against audio. When the Functions object is omitted, Transcribe and Summarize are enabled
    /// and Extract is disabled. Summarize and Extract run on the transcript with the text model, so they need
    /// Transcribe enabled.
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

        #endregion

        #region Private-Members

        private TranscribeSpec _Transcribe = new TranscribeSpec { Enable = true };

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
