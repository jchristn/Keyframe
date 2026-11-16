namespace Keyframe.Core.Database
{
    using System;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Mysql;
    using Keyframe.Core.Database.Postgresql;
    using Keyframe.Core.Database.Sqlite;
    using Keyframe.Core.Database.SqlServer;

    /// <summary>
    /// Creates database drivers from settings.
    /// </summary>
    public static class DatabaseDriverFactory
    {
        #region Private-Members

        private static readonly Regex _Identifier = new Regex("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.Compiled);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Create a driver without initializing it.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null, or a server provider is missing its hostname or database name.</exception>
        /// <exception cref="NotSupportedException">Thrown for an unknown provider.</exception>
        public static DatabaseDriverBase Create(DatabaseSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            if (settings.Type != DatabaseTypeEnum.Sqlite)
            {
                if (String.IsNullOrWhiteSpace(settings.Hostname)) throw new ArgumentNullException(nameof(settings.Hostname), "Database.Hostname is required for " + settings.Type + ".");
                RequireIdentifier(settings.DatabaseName);
            }

            switch (settings.Type)
            {
                case DatabaseTypeEnum.Sqlite:
                    return new SqliteDatabaseDriver(settings);
                case DatabaseTypeEnum.Mysql:
                    return new MysqlDatabaseDriver(settings);
                case DatabaseTypeEnum.Postgresql:
                    return new PostgresqlDatabaseDriver(settings);
                case DatabaseTypeEnum.SqlServer:
                    return new SqlServerDatabaseDriver(settings);
                default:
                    throw new NotSupportedException("Database type '" + settings.Type + "' is not supported.");
            }
        }

        /// <summary>
        /// Create and initialize a driver (storage preparation and migrations).
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Initialized driver.</returns>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public static async Task<DatabaseDriverBase> CreateAndInitializeAsync(DatabaseSettings settings, CancellationToken token = default)
        {
            DatabaseDriverBase driver = Create(settings);
            try
            {
                await driver.InitializeAsync(token).ConfigureAwait(false);
                return driver;
            }
            catch
            {
                driver.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Require a safe SQL identifier (letters, digits, underscore; 63 characters at most).
        /// </summary>
        /// <param name="name">Identifier.</param>
        /// <returns>The identifier.</returns>
        /// <exception cref="ArgumentNullException">Thrown when name is null or empty.</exception>
        /// <exception cref="ArgumentException">Thrown when name contains unsupported characters.</exception>
        public static string RequireIdentifier(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) throw new ArgumentNullException(nameof(name), "Database.DatabaseName is required.");
            if (!_Identifier.IsMatch(name)) throw new ArgumentException("Database name '" + name + "' may contain only letters, digits, and underscores.", nameof(name));
            return name;
        }

        #endregion
    }
}
