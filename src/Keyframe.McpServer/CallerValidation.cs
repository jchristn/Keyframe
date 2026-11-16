namespace Keyframe.McpServer
{
    using System;

    /// <summary>
    /// A cached result of checking a caller's key against the Keyframe server.
    /// </summary>
    public class CallerValidation
    {
        /// <summary>
        /// Principal name reported by the Keyframe server.
        /// </summary>
        public string Principal { get; set; } = String.Empty;

        /// <summary>
        /// When the cached result expires.
        /// </summary>
        public DateTime ExpiresUtc { get; set; } = DateTime.UtcNow;
    }
}
