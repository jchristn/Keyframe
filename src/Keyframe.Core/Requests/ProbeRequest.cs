namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Request for deterministic media properties only.
    /// </summary>
    public class ProbeRequest
    {
        /// <summary>
        /// Media to probe. Required.
        /// </summary>
        public MediaSource? Media { get; set; } = null;
    }
}
