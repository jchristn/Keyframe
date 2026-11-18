namespace Keyframe.Core.Responses
{
    using Keyframe.Core.Enums;

    /// <summary>
    /// One audio clip rendered and sent to a listening model.
    /// </summary>
    public class ListenClip
    {
        /// <summary>
        /// Which calls the clip was sent to.
        /// </summary>
        public ListenClipScopeEnum Scope { get; set; } = ListenClipScopeEnum.Recording;

        /// <summary>
        /// Section or scene index for Section and Scene clips; null for Recording clips.
        /// </summary>
        public int? Index { get; set; } = null;

        /// <summary>
        /// Start of the clip in the source media, in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End of the clip in the source media, in milliseconds.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Size of the rendered clip in bytes, before base64 encoding.
        /// </summary>
        public long SizeBytes { get; set; } = 0;
    }
}
