namespace Keyframe.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Logging settings. Changing the log directory requires a restart.
    /// </summary>
    public class LoggingSettings
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
        /// Log directory. Null or empty disables file logging. Default "logs/".
        /// </summary>
        public string? LogDirectory { get; set; } = "logs/";

        /// <summary>
        /// Log filename inside the log directory. Default "keyframe.log".
        /// </summary>
        public string? LogFilename { get; set; } = "keyframe.log";

        /// <summary>
        /// Minimum syslog severity to emit (0 debug to 7 emergency). Default 0. Minimum 0, maximum 7.
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

        /// <summary>
        /// Syslog servers. Never null.
        /// </summary>
        public List<SyslogServerSettings> Servers
        {
            get
            {
                return _Servers;
            }
            set
            {
                _Servers = value ?? new List<SyslogServerSettings>();
            }
        }

        #endregion

        #region Private-Members

        private int _MinimumSeverity = 0;
        private List<SyslogServerSettings> _Servers = new List<SyslogServerSettings>();

        #endregion
    }
}
