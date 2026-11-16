namespace Keyframe.Sdk.Responses
{
    using System;

    /// <summary>
    /// Timing for one pipeline stage.
    /// </summary>
    public class StageTiming
    {
        /// <summary>
        /// Stage name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Whether the stage completed without error.
        /// </summary>
        public bool Success { get; set; } = true;
    }
}
