namespace Keyframe.Core.Database.SqlServer
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.SqlServer.Implementations;
    using Keyframe.Core.Database.SqlServer.Queries;
    using Microsoft.Data.SqlClient;

    /// <summary>
    /// SQL Server driver. Creates the database on first boot when it does not exist.
    /// </summary>
    public class SqlServerDatabaseDriver : DatabaseDriverBase
    {
        #region Public-Members

        /// <inheritdoc />
        public override DatabaseTypeEnum Type
        {
            get
            {
                return DatabaseTypeEnum.SqlServer;
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
        public SqlServerDatabaseDriver(DatabaseSettings settings) : base(settings)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                DataSource = settings.Port > 0 ? settings.Hostname + "," + settings.Port : settings.Hostname,
                InitialCatalog = settings.DatabaseName,
                UserID = settings.Username,
                Password = settings.Password,
                TrustServerCertificate = settings.TrustServerCertificate,
                Pooling = true,
                CommandTimeout = settings.CommandTimeoutSeconds
            };

            _ConnectionString = builder.ToString();
            builder.InitialCatalog = "master";
            _AdminConnectionString = builder.ToString();

            RequestHistory = new RequestHistoryMethods(this);
            Jobs = new JobMethods(this);
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public override string PageClause(string skipParameter, string takeParameter)
        {
            return " OFFSET " + skipParameter + " ROWS FETCH NEXT " + takeParameter + " ROWS ONLY";
        }

        /// <inheritdoc />
        public override Task CloseAsync(CancellationToken token = default)
        {
            SqlConnection.ClearAllPools();
            return Task.CompletedTask;
        }

        #endregion

        #region Protected-Members-and-Methods

        /// <inheritdoc />
        protected override void ConfigureParameter(DbParameter parameter, object? value)
        {
            // SqlClient infers the legacy datetime type (1/300 s precision) for DateTime values; the columns are
            // DATETIME2(7), so boundary comparisons need DateTime2 parameters.
            if (value is DateTime) parameter.DbType = DbType.DateTime2;
        }

        /// <inheritdoc />
        protected override DbConnection CreateConnection()
        {
            return new SqlConnection(_ConnectionString);
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
            using (SqlConnection conn = new SqlConnection(_AdminConnectionString))
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                using (SqlCommand create = new SqlCommand("IF DB_ID(@name) IS NULL EXEC('CREATE DATABASE [" + name + "]')", conn))
                {
                    create.Parameters.AddWithValue("@name", name);
                    await create.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }
            }
        }

        #endregion
    }
}
