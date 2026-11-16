namespace Keyframe.Core.Inference
{
    using System;

    /// <summary>
    /// A provider-neutral transcription request.
    /// </summary>
    public class TranscriptionRequest
    {
        #region Public-Members

        /// <summary>
        /// Audio bytes.
        /// </summary>
        public byte[] Audio
        {
            get
            {
                return _Audio;
            }
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                _Audio = value;
            }
        }

        /// <summary>
        /// MIME type of the audio. Default "audio/flac".
        /// </summary>
        public string MediaType { get; set; } = "audio/flac";

        /// <summary>
        /// File name sent with multipart uploads. Default "audio.flac".
        /// </summary>
        public string FileName { get; set; } = "audio.flac";

        /// <summary>
        /// Duration hint in milliseconds, when known.
        /// </summary>
        public long? DurationMs { get; set; } = null;

        /// <summary>
        /// Model override.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Language as an ISO-639 code.
        /// </summary>
        public string? Language { get; set; } = null;

        /// <summary>
        /// Prompt guiding spelling of names and terms.
        /// </summary>
        public string? Prompt { get; set; } = null;

        #endregion

        #region Private-Members

        private byte[] _Audio = Array.Empty<byte>();

        #endregion
    }
}
