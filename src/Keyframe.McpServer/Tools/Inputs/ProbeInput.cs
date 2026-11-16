namespace Keyframe.McpServer.Tools.Inputs
{
    /// <summary>
    /// Arguments of keyframe_probe.
    /// </summary>
    public class ProbeInput
    {
        /// <summary>
        /// HTTP or HTTPS URL.
        /// </summary>
        public string? Url { get; set; } = null;

        /// <summary>
        /// Base64 bytes.
        /// </summary>
        public string? Data { get; set; } = null;
    }
}
