namespace Keyframe.McpServer.Tools.Inputs
{
    /// <summary>
    /// Input for listing jobs.
    /// </summary>
    public class JobListInput
    {
        /// <summary>
        /// Optional status filter.
        /// </summary>
        public string? Status { get; set; } = null;

        /// <summary>
        /// Page number from 1. Default 1.
        /// </summary>
        public int? PageNumber { get; set; } = null;

        /// <summary>
        /// Page size, 1 to 100. Default 25.
        /// </summary>
        public int? PageSize { get; set; } = null;
    }
}
