namespace Keyframe.Server.Services
{
    using System;

    /// <summary>
    /// Last observed health of one endpoint.
    /// </summary>
    public class EndpointHealthState
    {
        /// <summary>
        /// Whether the endpoint answered.
        /// </summary>
        public bool Healthy { get; set; } = false;

        /// <summary>
        /// Explanation when unhealthy.
        /// </summary>
        public string? Message { get; set; } = null;

        /// <summary>
        /// UTC time of the check.
        /// </summary>
        public DateTime CheckedUtc { get; set; } = DateTime.UtcNow;
    }
}
