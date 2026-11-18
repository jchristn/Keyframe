namespace Keyframe.Core.Inference
{
    using System;

    /// <summary>
    /// An audio clip sent with an inference request.
    /// </summary>
    public class InferenceAudio
    {
        #region Public-Members

        /// <summary>
        /// Encoded audio bytes.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        public byte[] Data
        {
            get
            {
                return _Data;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _Data = value;
            }
        }

        /// <summary>
        /// MIME type of the bytes. Default "audio/mpeg".
        /// </summary>
        public string MediaType { get; set; } = "audio/mpeg";

        /// <summary>
        /// Text placed in the prompt immediately before the clip, for example "Excerpt at 01:32:10 (30s)", or null.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// SHA-256 of the bytes as lowercase hex, or null when not computed.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        /// <summary>
        /// Start of the clip in the source media, in milliseconds. Minimum 0.
        /// </summary>
        public long StartMs
        {
            get
            {
                return _StartMs;
            }
            set
            {
                _StartMs = Math.Max(0, value);
            }
        }

        /// <summary>
        /// End of the clip in the source media, in milliseconds. Never less than <see cref="StartMs"/> when read.
        /// </summary>
        public long EndMs
        {
            get
            {
                return Math.Max(_EndMs, _StartMs);
            }
            set
            {
                _EndMs = Math.Max(0, value);
            }
        }

        #endregion

        #region Private-Members

        private byte[] _Data = Array.Empty<byte>();
        private long _StartMs = 0;
        private long _EndMs = 0;

        #endregion
    }
}
