namespace Keyframe.Sdk.Settings
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Database connection settings. Changes require a restart.
    /// </summary>
    public class DatabaseSettings
    {
        #region Public-Members

        /// <summary>
        /// Database provider. Default Sqlite.
        /// </summary>
        public DatabaseTypeEnum Type { get; set; } = DatabaseTypeEnum.Sqlite;

        /// <summary>
        /// SQLite database filename. Default "data/keyframe.db".
        /// </summary>
        public string Filename
        {
            get
            {
                return _Filename;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Filename));
                _Filename = value;
            }
        }

        /// <summary>
        /// Server hostname for MySQL, PostgreSQL, and SQL Server.
        /// </summary>
        public string? Hostname { get; set; } = null;

        /// <summary>
        /// Server port. Zero uses the provider default. Minimum 0, maximum 65535.
        /// </summary>
        public int Port
        {
            get
            {
                return _Port;
            }
            set
            {
                _Port = Math.Clamp(value, 0, 65535);
            }
        }

        /// <summary>
        /// Database name for server providers.
        /// </summary>
        public string? DatabaseName { get; set; } = null;

        /// <summary>
        /// Username for server providers.
        /// </summary>
        public string? Username { get; set; } = null;

        /// <summary>
        /// Password for server providers. Never returned by the settings API.
        /// </summary>
        public string? Password { get; set; } = null;

        /// <summary>
        /// Whether to trust the server certificate (SQL Server and PostgreSQL). Default true for local development.
        /// </summary>
        public bool TrustServerCertificate { get; set; } = true;

        /// <summary>
        /// Command timeout in seconds. Default 30. Minimum 1, maximum 3600.
        /// </summary>
        public int CommandTimeoutSeconds
        {
            get
            {
                return _CommandTimeoutSeconds;
            }
            set
            {
                _CommandTimeoutSeconds = Math.Clamp(value, 1, 3600);
            }
        }

        #endregion

        #region Private-Members

        private string _Filename = "data/keyframe.db";
        private int _Port = 0;
        private int _CommandTimeoutSeconds = 30;

        #endregion
    }
}
