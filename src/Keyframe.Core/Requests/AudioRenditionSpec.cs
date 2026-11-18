namespace Keyframe.Core.Requests
{
    using System;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Quality of the audio sent to a listening model. Each null member uses its Media.Listen* server default. In a
    /// response (ListeningInfo.Rendition) every member is filled with the effective value.
    /// </summary>
    public class AudioRenditionSpec
    {
        #region Public-Members

        /// <summary>
        /// Container and codec. Null uses Media.ListenFormat (default Mp3). OpenAI-compatible endpoints accept only Mp3 and Wav.
        /// </summary>
        public ListenFormatEnum? Format { get; set; } = null;

        /// <summary>
        /// Sample rate in Hz. Null uses Media.ListenSampleRate (default 16000). Minimum 8000, maximum 48000.
        /// </summary>
        public int? SampleRate
        {
            get
            {
                return _SampleRate;
            }
            set
            {
                _SampleRate = value.HasValue ? Math.Clamp(value.Value, 8000, 48000) : null;
            }
        }

        /// <summary>
        /// Channel count. Null uses Media.ListenChannels (default 1). Minimum 1, maximum 2.
        /// </summary>
        public int? Channels
        {
            get
            {
                return _Channels;
            }
            set
            {
                _Channels = value.HasValue ? Math.Clamp(value.Value, 1, 2) : null;
            }
        }

        /// <summary>
        /// Mp3 bitrate in kbps. Ignored for Wav and Flac. Null uses Media.ListenBitrateKbps (default 64). Minimum 16, maximum 320.
        /// </summary>
        public int? BitrateKbps
        {
            get
            {
                return _BitrateKbps;
            }
            set
            {
                _BitrateKbps = value.HasValue ? Math.Clamp(value.Value, 16, 320) : null;
            }
        }

        #endregion

        #region Private-Members

        private int? _SampleRate = null;
        private int? _Channels = null;
        private int? _BitrateKbps = null;

        #endregion
    }
}
