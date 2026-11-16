namespace Keyframe.Core.Responses
{
    /// <summary>
    /// Result of an endpoint connectivity test.
    /// </summary>
    public class EndpointTestResponse
    {
        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Whether the test call succeeded.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Round-trip time in milliseconds.
        /// </summary>
        public double DurationMs { get; set; } = 0;

        /// <summary>
        /// Model that answered.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Error message on failure.
        /// </summary>
        public string? Error { get; set; } = null;
    }
}
