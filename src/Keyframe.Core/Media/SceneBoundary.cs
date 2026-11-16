namespace Keyframe.Core.Media
{
    /// <summary>
    /// A scene time range.
    /// </summary>
    public class SceneBoundary
    {
        /// <summary>
        /// Start offset in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End offset in milliseconds.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long DurationMs
        {
            get
            {
                return EndMs - StartMs;
            }
        }
    }
}
