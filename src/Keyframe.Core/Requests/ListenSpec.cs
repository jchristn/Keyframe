namespace Keyframe.Core.Requests
{
    using System;

    /// <summary>
    /// Listening options for audio analysis: whether the model hears the audio itself, which model, and how much of it.
    /// </summary>
    public class ListenSpec
    {
        #region Public-Members

        /// <summary>
        /// Send the audio itself to the listening model for Summarize, Extract, and per-section functions. When no
        /// audio-capable endpoint is usable, the analysis falls back to the transcript and Listening.Listened is false.
        /// Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Listening endpoint and model. Null uses Inference.DefaultListening. Naming an endpoint whose provider cannot take
        /// audio input is rejected with 400. Default null.
        /// </summary>
        public ModelSelection? Model { get; set; } = null;

        /// <summary>
        /// Quality of the audio sent to the model. Never null; null members use the Media.Listen* defaults.
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

        /// <summary>
        /// Number of evenly spaced excerpts sent when the recording is too long to send whole and sections are off.
        /// Default 4. Minimum 1, maximum 32.
        /// </summary>
        public int SampleClips
        {
            get
            {
                return _SampleClips;
            }
            set
            {
                _SampleClips = Math.Clamp(value, 1, 32);
            }
        }

        /// <summary>
        /// Length of each excerpt in seconds. Shortened when SampleClips times ClipSeconds exceeds the listen budget.
        /// Default 30. Minimum 5, maximum 300.
        /// </summary>
        public int ClipSeconds
        {
            get
            {
                return _ClipSeconds;
            }
            set
            {
                _ClipSeconds = Math.Clamp(value, 5, 300);
            }
        }

        #endregion

        #region Private-Members

        private AudioRenditionSpec _Rendition = new AudioRenditionSpec();
        private int _SampleClips = 4;
        private int _ClipSeconds = 30;

        #endregion
    }
}
