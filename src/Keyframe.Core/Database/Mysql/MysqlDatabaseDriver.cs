namespace Keyframe.Core.Database.Mysql
{
    using System;
    using System.Collections.Generic;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Mysql.Implementations;
    using Keyframe.Core.Database.Mysql.Queries;
    using MySqlConnector;

    /// <summary>
    /// MySQL driver. Creates the database on first boot when it does not exist.
    /// </summary>
    public class MysqlDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum Type
        {
            get
            {
                return DatabaseTypeEnum.Mysql;
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
        public MysqlDatabaseDriver(DatabaseSettings settings) : base(settings)
        {
            MySqlConnectionStringBuilder builder = new MySqlConnectionStringBuilder
            {
                Server = settings.Hostname,
                Port = (uint)(settings.Port > 0 ? settings.Port : 3306),
                Database = settings.DatabaseName,
                UserID = settings.Username,
                Password = settings.Password,
                Pooling = true,
                DefaultCommandTimeout = (uint)settings.CommandTimeoutSeconds,
                AllowUserVariables = false
            };

            _ConnectionString = builder.ToString();
            builder.Database = String.Empty;
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
            MySqlConnection.ClearAllPools();
            return Task.CompletedTask;
        }

        #endregion

        #region Protected-Members-and-Methods

        /// <inheritdoc />
        protected override DbConnection CreateConnection()
        {
            return new MySqlConnection(_ConnectionString);
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
            using (MySqlConnection conn = new MySqlConnection(_AdminConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (MySqlCommand create = new MySqlCommand("CREATE DATABASE IF NOT EXISTS `" + name + "` CHARACTER SET utf8mb4", conn))
                {
                    await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
