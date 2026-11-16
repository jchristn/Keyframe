namespace Keyframe.Core.Database.Sqlite
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Sqlite.Implementations;
    using Keyframe.Core.Database.Sqlite.Queries;
    using Microsoft.Data.Sqlite;

    /// <summary>
    /// SQLite driver. Writes are serialized through a semaphore; the database runs in WAL mode.
    /// Timestamps are stored as fixed-width ISO 8601 UTC text so they sort lexically.
    /// </summary>
    public class SqliteDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum Type
        {
            get
            {
                return DatabaseTypeEnum.Sqlite;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _ConnectionString;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public SqliteDatabaseDriver(DatabaseSettings settings) : base(settings)
        {
            SqliteConnectionStringBuilder builder = new SqliteConnectionStringBuilder
            {
                DataSource = settings.Filename,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = true,
                DefaultTimeout = settings.CommandTimeoutSeconds
            };

            _ConnectionString = builder.ToString();
            RequestHistory = new RequestHistoryMethods(this);
            Jobs = new JobMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override object ToDbDate(DateTime value)
        {
            return TruncateToMicroseconds(value).ToString("yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'", CultureInfo.InvariantCulture);
        }

        /// <inheritdoc />
        public override Task CloseAsync(CancellationToken token = default)
        {
            SqliteConnection.ClearAllPools();
            return Task.CompletedTask;
        }

        #endregion

        #region Protected-Members-and-Methods

        /// <inheritdoc />
        protected override bool SerializeWrites
        {
            get
            {
                return true;
            }
        }

        /// <inheritdoc />
        protected override DbConnection CreateConnection()
        {
            return new SqliteConnection(_ConnectionString);
        }

        /// <inheritdoc />
        protected override string GetMigrationTableStatement()
        {
            return SetupQueries.MigrationTable;
        }

        /// <inheritdoc />
        protected override IReadOnlyList<Migration> GetMigrations()
        {
            return SetupQueries.Migrations();
        }

        /// <inheritdoc />
        protected override async Task PrepareStorageAsync(CancellationToken token)
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(Settings.Filename));
            if (!String.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            await ExecuteQueryAsync("PRAGMA journal_mode=WAL", null, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        protected override async Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken token)
        {
            using (DbCommand cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA busy_timeout = " + (Settings.CommandTimeoutSeconds * 1000).ToString(CultureInfo.InvariantCulture);
                await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }
        }

        #endregion
    }
}
