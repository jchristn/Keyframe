namespace Keyframe.McpServer.Settings
{
    using System;

    /// <summary>
    /// MCP server logging.
    /// </summary>
    public class McpLoggingSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether to log to the console. Default true.
        /// </summary>
        public bool ConsoleLogging { get; set; } = true;

        /// <summary>
        /// Whether console output uses colors. Default true.
        /// </summary>
        public bool EnableColors { get; set; } = true;

        /// <summary>
        /// Log directory; null or empty disables file logging. Default "logs/".
        /// </summary>
        public string? LogDirectory { get; set; } = "logs/";

        /// <summary>
        /// Log filename. Default "keyframe-mcp.log".
        /// </summary>
        public string? LogFilename { get; set; } = "keyframe-mcp.log";

        /// <summary>
        /// Minimum severity, 0 (debug) to 7 (emergency). Default 1.
        /// </summary>
        public int MinimumSeverity
        {
            get
            {
                return _MinimumSeverity;
            }
            set
            {
                _MinimumSeverity = Math.Clamp(value, 0, 7);
            }
        }

        #endregion

        #region Private-Members

        private int _MinimumSeverity = 1;

        #endregion
    }
}
