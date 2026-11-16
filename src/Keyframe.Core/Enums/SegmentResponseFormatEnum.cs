namespace Keyframe.Core.Enums
{
    /// <summary>
    /// How a segment is returned.
    /// </summary>
    public enum SegmentResponseFormatEnum
    {
        /// <summary>
        /// The media bytes as the response body.
        /// </summary>
        Binary,

        /// <summary>
        /// A JSON body with the media as base64.
        /// </summary>
        Json
    }
}
