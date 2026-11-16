namespace Keyframe.Core.Settings
{
    using System;

    /// <summary>
    /// A syslog server destination.
    /// </summary>
    public class SyslogServerSettings
    {
        #region Public-Members

        /// <summary>
        /// Syslog server hostname. Default "127.0.0.1".
        /// </summary>
        public string Hostname
        {
            get
            {
                return _Hostname;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Hostname));
                _Hostname = value;
            }
        }

        /// <summary>
        /// Syslog server UDP port. Default 514. Minimum 1, maximum 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 1, 65535);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "127.0.0.1";
        private int _Port = 514;

        #endregion
    }
}
