namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Scene audio: attach each scene's audio to that scene's model calls, when the vision endpoint can take audio.
    /// </summary>
    public class SceneAudioSpec
    {
        #region Public-Members

        /// <summary>
        /// Attach each scene's audio. When the vision endpoint cannot take audio, is unhealthy, or the video has no audio
        /// track, scenes run on frames and transcript and VideoAnalysisResponse.Listening says why; never a 400.
        /// Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Quality of the scene audio. Never null; null members use the Media.Listen* defaults.
        /// </summary>
        public AudioRenditionSpec Rendition
        {
            get
            {
                return _Rendition;
            }
            set
            {
                _Rendition = value ?? new AudioRenditionSpec();
            }
        }

        #endregion

        #region Private-Members

        private AudioRenditionSpec _Rendition = new AudioRenditionSpec();

        #endregion
    }
}
