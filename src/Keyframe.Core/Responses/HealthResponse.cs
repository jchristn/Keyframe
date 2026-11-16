namespace Keyframe.Core.Responses
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Server health.
    /// </summary>
    public class HealthResponse
    {
        /// <summary>
        /// Overall status: "Healthy" or "Degraded".
        /// </summary>
        public string Status { get; set; } = "Healthy";

        /// <summary>
        /// Product name.
        /// </summary>
        public string Product { get; set; } = Constants.ProductName;

        /// <summary>
        /// Server version.
        /// </summary>
        public string Version { get; set; } = String.Empty;

        /// <summary>
        /// UTC start time.
        /// </summary>
        public DateTime StartedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Seconds since start.
        /// </summary>
        public long UptimeSeconds { get; set; } = 0;

        /// <summary>
        /// Whether ffmpeg and ffprobe were found.
        /// </summary>
        public bool FfmpegAvailable { get; set; } = false;

        /// <summary>
        /// ffmpeg version line, when available.
        /// </summary>
        public string? FfmpegVersion { get; set; } = null;

        /// <summary>
        /// Database provider in use.
        /// </summary>
        public string Database { get; set; } = String.Empty;

        /// <summary>
        /// Jobs currently running.
        /// </summary>
        public int ActiveJobs { get; set; } = 0;

        /// <summary>
        /// Jobs waiting for a worker.
        /// </summary>
        public int QueuedJobs { get; set; } = 0;

        /// <summary>
        /// Endpoint health keyed by endpoint name.
        /// </summary>
        public Dictionary<string, bool> Endpoints { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    }
}
