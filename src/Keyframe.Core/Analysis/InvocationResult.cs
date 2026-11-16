namespace Keyframe.Core.Analysis
{
    using Keyframe.Core.Responses;

    /// <summary>
    /// Outcome of a model invocation after retries and repairs.
    /// </summary>
    public class InvocationResult
    {
        /// <summary>
        /// Whether a usable response was produced.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Final response text.
        /// </summary>
        public string? Text { get; set; } = null;

        /// <summary>
        /// Error when unsuccessful.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Telemetry recorded for the call.
        /// </summary>
        public ModelCallTelemetry Telemetry { get; set; } = new ModelCallTelemetry();
    }
}
