namespace Keyframe.McpServer.Settings
{
    using System;

    /// <summary>
    /// One MCP transport listener.
    /// </summary>
    public class TransportSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether the transport is started. Default true.
        /// </summary>
        public bool Enable { get; set; } = true;

        /// <summary>
        /// Hostname or address to bind. Default "127.0.0.1". Use "*" (HTTP, WebSocket) or "0.0.0.0" (TCP) for all interfaces.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
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
        /// TCP port. Minimum 1, maximum 65535.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when outside 1 to 65535.</exception>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                if (value < 1 || value > 65535) throw new ArgumentOutOfRangeException(nameof(Port));
                _Port = value;
            }
        }

        #endregion

        #region Private-Members

        private string _Hostname = "127.0.0.1";
        private int _Port = 8702;

        #endregion
    }
}
