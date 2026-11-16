namespace Keyframe.Core.Inference
{
    using System;

    /// <summary>
    /// An image sent with an inference request.
    /// </summary>
    public class InferenceImage
    {
        #region Public-Members

        /// <summary>
        /// Encoded image bytes.
        /// </summary>
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
        /// MIME type of the bytes. Default "image/jpeg".
        /// </summary>
        public string MediaType { get; set; } = "image/jpeg";

        /// <summary>
        /// Optional label placed in the prompt before the image, for example "Frame at 00:12.400".
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// SHA-256 of the bytes, used by the mock client for deterministic answers.
        /// </summary>
        public string? Sha256 { get; set; } = null;

        #endregion

        #region Private-Members

        private byte[] _Data = Array.Empty<byte>();

        #endregion
    }
}
