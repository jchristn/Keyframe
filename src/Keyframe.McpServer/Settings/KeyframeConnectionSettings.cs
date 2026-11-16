namespace Keyframe.McpServer.Settings
{
    using System;

    /// <summary>
    /// How the MCP server reaches the Keyframe REST server.
    /// </summary>
    public class KeyframeConnectionSettings
    {
        #region Public-Members

        /// <summary>
        /// Keyframe REST base URL. Default "http://127.0.0.1:8700".
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null or whitespace.</exception>
        public string Endpoint
        {
            get
            {
                return _Endpoint;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Endpoint));
                _Endpoint = value.TrimEnd('/');
            }
        }

        /// <summary>
        /// Access key the MCP server presents to Keyframe. Default "default". MCP clients authenticate to the MCP
        /// server with this same key as a bearer token when authentication is required.
        /// </summary>
        public string? AccessKey { get; set; } = "default";

        /// <summary>
        /// Request timeout in milliseconds. Default 600000. Minimum 1000, maximum 3600000.
        /// </summary>
        public int TimeoutMs
        {
            get
            {
                return _TimeoutMs;
            }
            set
            {
                _TimeoutMs = Math.Clamp(value, 1000, 3600000);
            }
        }

        #endregion

        #region Private-Members

        private string _Endpoint = "http://127.0.0.1:8700";
        private int _TimeoutMs = 600000;

        #endregion
    }
}
