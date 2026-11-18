namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Deterministic audio measurements: loudness, levels, silence, and stereo width from one ffmpeg decode pass (two for
    /// stereo). No model calls.
    /// </summary>
    public class MeasureSpec
    {
        /// <summary>
        /// Run the measurements. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;
    }
}
