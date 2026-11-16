namespace Keyframe.Core.Database.Postgresql
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Postgresql.Implementations;
    using Keyframe.Core.Database.Postgresql.Queries;
    using Npgsql;

    /// <summary>
    /// PostgreSQL driver. Creates the database on first boot when it does not exist.
    /// </summary>
    public class PostgresqlDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum Type
        {
            get
            {
                return DatabaseTypeEnum.Postgresql;
            }
        }

        #endregion

        #region Private-Members

        private readonly string _ConnectionString;
        private readonly string _AdminConnectionString;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        public PostgresqlDatabaseDriver(DatabaseSettings settings) : base(settings)
        {
            NpgsqlConnectionStringBuilder builder = new NpgsqlConnectionStringBuilder
            {
                Host = settings.Hostname,
                Port = settings.Port > 0 ? settings.Port : 5432,
                Database = settings.DatabaseName,
                Username = settings.Username,
                Password = settings.Password,
                Pooling = true,
                CommandTimeout = settings.CommandTimeoutSeconds
            };

            _ConnectionString = builder.ToString();
            builder.Database = "postgres";
            _AdminConnectionString = builder.ToString();

            RequestHistory = new RequestHistoryMethods(this);
            Jobs = new JobMethods(this);
            Analytics = new AnalyticsMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override Task CloseAsync(CancellationToken token = default)
        {
            NpgsqlConnection.ClearAllPools();
            return Task.CompletedTask;
        }

        #endregion

        #region Protected-Members-and-Methods

        /// <inheritdoc />
        protected override DbConnection CreateConnection()
        {
            return new NpgsqlConnection(_ConnectionString);
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
            string name = DatabaseDriverFactory.RequireIdentifier(Settings.DatabaseName);
            using (NpgsqlConnection conn = new NpgsqlConnection(_AdminConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (NpgsqlCommand check = new NpgsqlCommand("SELECT 1 FROM pg_database WHERE datname = @name", conn))
                {
                    check.Parameters.AddWithValue("@name", name);
                    object? exists = await check.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (exists != null) return;
                }

                using (NpgsqlCommand create = new NpgsqlCommand("CREATE DATABASE \"" + name + "\"", conn))
                {
                    await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
