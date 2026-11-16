namespace Keyframe.Core.Settings
{
    using System;

    /// <summary>
    /// REST listener settings. Changing these requires a restart.
    /// </summary>
    public class RestSettings
    {
        #region Public-Members

        /// <summary>
        /// Hostname or address to bind. Default "127.0.0.1". Use "*" or "0.0.0.0" to listen on all interfaces.
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
        /// TCP port. Default 8700. Minimum 1, maximum 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(Port), "Port must be between 1 and 65535.");
                _Port = value;
            }
        }

        /// <summary>
        /// Whether to use TLS. Default false.
        /// </summary>
        public bool Ssl { get; set; } = false;

        /// <summary>
        /// Maximum accepted request body size in bytes. Default 268435456 (256 MB). Minimum 1024, maximum 4294967296 (4 GB).
        /// Base64 media inflates by about one third, so prefer URLs for large videos.
        /// </summary>
        public long MaxRequestBodyBytes
        {
            get
            {
                return _MaxRequestBodyBytes;
            }
            set
            {
                _MaxRequestBodyBytes = Math.Clamp(value, 1024L, 4294967296L);
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "127.0.0.1";
        private int _Port = 8700;
        private long _MaxRequestBodyBytes = 268435456L;

        #endregion
    }
}
