namespace Keyframe.Sdk.Responses
{
    using System;

    /// <summary>
    /// A segment returned as binary media by POST /v1.0/segment, with the metadata from the response headers.
    /// </summary>
    public class SegmentMedia
    {
        #region Public-Members

        /// <summary>
        /// Media type from Content-Type, for example video/mp4, audio/mpeg, or image/jpeg.
        /// </summary>
        public string MediaType { get; set; } = String.Empty;

        /// <summary>
        /// File name from Content-Disposition, for example segment-12000-18500.mp4. Empty when the header is missing.
        /// </summary>
        public string Filename { get; set; } = String.Empty;

        /// <summary>
        /// Start of the segment in milliseconds (x-keyframe-segment-start-ms).
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End of the segment in milliseconds after clamping to the media duration (x-keyframe-segment-end-ms). Equal to StartMs for frames.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Segment duration in milliseconds (x-keyframe-segment-duration-ms). 0 for frames.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Size of the media in bytes.
        /// </summary>
        public long SizeBytes
        {
            get
            {
                return _Data.LongLength;
            }
        }

        /// <summary>
        /// Value of the x-request-id response header, or null.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Media bytes. Never null.
        /// </summary>
        public byte[] Data
        {
            get
            {
                return _Data;
            }
            set
            {
                _Data = value ?? Array.Empty<byte>();
            }
        }

        #endregion

        #region Private-Members

        private byte[] _Data = Array.Empty<byte>();

        #endregion
    }
}
