namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Functions to run against a video.
    /// </summary>
    public class VideoFunctionsSpec : FunctionsSpec
    {
        #region Public-Members

        /// <summary>
        /// Scene breakdown options.
        /// </summary>
        public ScenesSpec Scenes
        {
            get
            {
                return _Scenes;
            }
            set
            {
                _Scenes = value ?? new ScenesSpec();
            }
        }

        /// <summary>
        /// Transcription options.
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

        private ScenesSpec _Scenes = new ScenesSpec();
        private TranscribeSpec _Transcribe = new TranscribeSpec();

        #endregion
    }
}
