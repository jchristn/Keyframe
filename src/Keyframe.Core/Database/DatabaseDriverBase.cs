namespace Keyframe.Core.Database
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Data.Common;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Interfaces;

    /// <summary>
    /// Base class for database drivers. Providers supply connections, dialect details, and migrations;
    /// the base supplies parameterized execution, write serialization, and the migration runner.
    /// Thread safety: safe for concurrent use. Each operation opens a pooled connection.
    /// </summary>
    public abstract class DatabaseDriverBase : IDisposable
    {
        #region Public-Members

        /// <summary>
        /// Settings used by this driver.
        /// </summary>
        public DatabaseSettings Settings { get; }

        /// <summary>
        /// Provider type.
        /// </summary>
        public abstract DatabaseTypeEnum Type { get; }

        /// <summary>
        /// Request history methods. Set by the provider constructor.
        /// </summary>
        public IRequestHistoryMethods RequestHistory { get; protected set; } = null!;

        /// <summary>
        /// Job methods. Set by the provider constructor.
        /// </summary>
        public IJobMethods Jobs { get; protected set; } = null!;

        /// <summary>
        /// Analytics methods. Set by the provider constructor.
        /// </summary>
        public IAnalyticsMethods Analytics { get; protected set; } = null!;

        /// <summary>
        /// Highest schema version applied, available after <see cref="InitializeAsync"/>.
        /// </summary>
        public int SchemaVersion { get; private set; } = 0;

        /// <summary>
        /// Whether <see cref="InitializeAsync"/> has completed.
        /// </summary>
        public bool IsInitialized { get; private set; } = false;

        #endregion

        #region Private-Members

        private readonly SemaphoreSlim _WriteLock = new SemaphoreSlim(1, 1);
        private bool _Disposed = false;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings is null.</exception>
        protected DatabaseDriverBase(DatabaseSettings settings)
        {
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Prepare storage (create files or databases as needed) and apply pending migrations.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task InitializeAsync(CancellationToken token = default)
        {
            await PrepareStorageAsync(token).ConfigureAwait(false);

            await ExecuteNonQueryAsync(GetMigrationTableStatement(), null, token).ConfigureAwait(false);

            HashSet<int> applied = new HashSet<int>();
            DataTable existing = await ExecuteQueryAsync("SELECT version FROM schema_migrations", null, token).ConfigureAwait(false);
            foreach (DataRow row in existing.Rows)
            {
                applied.Add(Convert.ToInt32(row["version"], CultureInfo.InvariantCulture));
            }

            foreach (Migration migration in GetMigrations().OrderBy(m => m.Version))
            {
                token.ThrowIfCancellationRequested();
                if (applied.Contains(migration.Version)) continue;

                List<DbCommandSpec> specs = new List<DbCommandSpec>();
                foreach (string statement in migration.Statements)
                {
                    specs.Add(new DbCommandSpec(statement));
                }

                DbCommandSpec record = new DbCommandSpec("INSERT INTO schema_migrations (version, description, appliedutc) VALUES (@version, @description, @appliedutc)");
                record.Add("@version", migration.Version);
                record.Add("@description", migration.Description);
                record.Add("@appliedutc", ToDbDate(DateTime.UtcNow));
                specs.Add(record);

                await ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
                applied.Add(migration.Version);
            }

            SchemaVersion = applied.Count > 0 ? applied.Max() : 0;
            IsInitialized = true;
        }

        /// <summary>
        /// Run a raw statement with optional parameters and return its rows.
        /// </summary>
        /// <param name="query">SQL text.</param>
        /// <param name="parameters">Parameters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Result rows; empty for statements that return none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null or empty.</exception>
        public async Task<DataTable> ExecuteQueryAsync(string query, IDictionary<string, object?>? parameters = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));

            using (DbConnection conn = await OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (DbCommand cmd = BuildCommand(conn, null, query, parameters))
                {
                    using (DbDataReader reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false))
                    {
                        // DataTable.Load is avoided on purpose: it infers primary keys from the reader schema
                        // and silently merges rows that share a partial key.
                        DataTable table = new DataTable();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            table.Columns.Add(reader.GetName(i), typeof(object));
                        }

                        object[] values = new object[reader.FieldCount];
                        while (await reader.ReadAsync(token).ConfigureAwait(false))
                        {
                            reader.GetValues(values);
                            table.Rows.Add(values);
                        }

                        return table;
                    }
                }
            }
        }

        /// <summary>
        /// Run a statement that returns no rows. Writes are serialized when the provider requires it.
        /// </summary>
        /// <param name="query">SQL text.</param>
        /// <param name="parameters">Parameters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Rows affected.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null or empty.</exception>
        public async Task<int> ExecuteNonQueryAsync(string query, IDictionary<string, object?>? parameters = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));

            bool locked = false;
            try
            {
                if (SerializeWrites)
                {
                    await _WriteLock.WaitAsync(token).ConfigureAwait(false);
                    locked = true;
                }

                using (DbConnection conn = await OpenConnectionAsync(token).ConfigureAwait(false))
                {
                    using (DbCommand cmd = BuildCommand(conn, null, query, parameters))
                    {
                        return await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                if (locked) _WriteLock.Release();
            }
        }

        /// <summary>
        /// Run a scalar query.
        /// </summary>
        /// <param name="query">SQL text.</param>
        /// <param name="parameters">Parameters, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The first column of the first row, or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when query is null or empty.</exception>
        public async Task<object?> ExecuteScalarAsync(string query, IDictionary<string, object?>? parameters = null, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(query)) throw new ArgumentNullException(nameof(query));

            using (DbConnection conn = await OpenConnectionAsync(token).ConfigureAwait(false))
            {
                using (DbCommand cmd = BuildCommand(conn, null, query, parameters))
                {
                    object? result = await cmd.ExecuteScalarAsync(token).ConfigureAwait(false);
                    if (result is DBNull) return null;
                    return result;
                }
            }
        }

        /// <summary>
        /// Run several statements in one transaction. Rolls back on any failure.
        /// </summary>
        /// <param name="commands">Commands, run in order.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Total rows affected.</returns>
        /// <exception cref="ArgumentNullException">Thrown when commands is null.</exception>
        public async Task<int> ExecuteTransactionAsync(IEnumerable<DbCommandSpec> commands, CancellationToken token = default)
        {
            if (commands == null) throw new ArgumentNullException(nameof(commands));

            bool locked = false;
            try
            {
                if (SerializeWrites)
                {
                    await _WriteLock.WaitAsync(token).ConfigureAwait(false);
                    locked = true;
                }

                using (DbConnection conn = await OpenConnectionAsync(token).ConfigureAwait(false))
                {
                    using (DbTransaction tx = await conn.BeginTransactionAsync(token).ConfigureAwait(false))
                    {
                        int total = 0;
                        try
                        {
                            foreach (DbCommandSpec spec in commands)
                            {
                                using (DbCommand cmd = BuildCommand(conn, tx, spec.Sql, spec.Parameters))
                                {
                                    total += await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                                }
                            }

                            await tx.CommitAsync(token).ConfigureAwait(false);
                            return total;
                        }
                        catch
                        {
                            try
                            {
                                await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                // The original exception is more useful than a rollback failure.
                            }

                            throw;
                        }
                    }
                }
            }
            finally
            {
                if (locked) _WriteLock.Release();
            }
        }

        /// <summary>
        /// Paging clause for this provider. The query must already contain ORDER BY.
        /// </summary>
        /// <param name="skipParameter">Name of the skip parameter (for example "@skip").</param>
        /// <param name="takeParameter">Name of the take parameter (for example "@take").</param>
        /// <returns>SQL fragment.</returns>
        public virtual string PageClause(string skipParameter, string takeParameter)
        {
            return " LIMIT " + takeParameter + " OFFSET " + skipParameter;
        }

        /// <summary>
        /// Convert a UTC timestamp into the value this provider stores. Values are truncated to whole
        /// microseconds, the finest precision every provider keeps, so stored and compared values agree.
        /// </summary>
        /// <param name="value">Timestamp.</param>
        /// <returns>Database value.</returns>
        public virtual object ToDbDate(DateTime value)
        {
            return TruncateToMicroseconds(value);
        }

        /// <summary>
        /// Convert to UTC and truncate to whole microseconds.
        /// </summary>
        /// <param name="value">Timestamp.</param>
        /// <returns>UTC timestamp.</returns>
        public static DateTime TruncateToMicroseconds(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
            return new DateTime(utc.Ticks - (utc.Ticks % 10), DateTimeKind.Utc);
        }

        /// <summary>
        /// Convert a nullable UTC timestamp into the value this provider stores.
        /// </summary>
        /// <param name="value">Timestamp or null.</param>
        /// <returns>Database value, or DBNull.</returns>
        public object ToDbDate(DateTime? value)
        {
            if (value == null) return DBNull.Value;
            return ToDbDate(value.Value);
        }

        /// <summary>
        /// Read a timestamp column written by <see cref="ToDbDate(DateTime)"/>.
        /// </summary>
        /// <param name="value">Column value.</param>
        /// <returns>UTC timestamp, or null for database nulls.</returns>
        public static DateTime? FromDbDate(object? value)
        {
            if (value == null || value is DBNull) return null;
            if (value is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            if (value is DateTimeOffset dto) return dto.UtcDateTime;
            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? String.Empty;
            if (String.IsNullOrWhiteSpace(text)) return null;
            return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
        }

        /// <summary>
        /// Close the driver. Pooled connections are released.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public virtual Task CloseAsync(CancellationToken token = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Protected-Members-and-Methods

        /// <summary>
        /// Whether writes are serialized through a single semaphore. True for SQLite.
        /// </summary>
        protected virtual bool SerializeWrites
        {
            get
            {
                return false;
            }
        }

        /// <summary>
        /// Create a new, unopened provider connection.
        /// </summary>
        /// <returns>Connection.</returns>
        protected abstract DbConnection CreateConnection();

        /// <summary>
        /// Statement that creates the schema_migrations table if it does not exist.
        /// </summary>
        /// <returns>SQL text.</returns>
        protected abstract string GetMigrationTableStatement();

        /// <summary>
        /// Every migration for this provider.
        /// </summary>
        /// <returns>Migrations.</returns>
        protected abstract IReadOnlyList<Migration> GetMigrations();

        /// <summary>
        /// Create files, directories, or the database itself before migrations run.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected virtual Task PrepareStorageAsync(CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Called after a connection opens; providers use it for per-connection pragmas.
        /// </summary>
        /// <param name="connection">Open connection.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        protected virtual Task OnConnectionOpenedAsync(DbConnection connection, CancellationToken token)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Adjust a parameter after its value is set; providers use it to pick precise database types.
        /// </summary>
        /// <param name="parameter">Parameter.</param>
        /// <param name="value">Original value, possibly null.</param>
        protected virtual void ConfigureParameter(DbParameter parameter, object? value)
        {
        }

        /// <summary>
        /// Dispose managed resources.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_Disposed) return;
            if (disposing)
            {
                _WriteLock.Dispose();
            }

            _Disposed = true;
        }

        #endregion

        #region Private-Methods

        private async Task<DbConnection> OpenConnectionAsync(CancellationToken token)
        {
            DbConnection conn = CreateConnection();
            try
            {
                await conn.OpenAsync(token).ConfigureAwait(false);
                await OnConnectionOpenedAsync(conn, token).ConfigureAwait(false);
                return conn;
            }
            catch
            {
                conn.Dispose();
                throw;
            }
        }

        private DbCommand BuildCommand(DbConnection conn, DbTransaction? tx, string sql, IDictionary<string, object?>? parameters)
        {
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = Settings.CommandTimeoutSeconds;
            if (tx != null) cmd.Transaction = tx;

            if (parameters != null)
            {
                foreach (KeyValuePair<string, object?> kvp in parameters)
                {
                    DbParameter p = cmd.CreateParameter();
                    p.ParameterName = kvp.Key;
                    p.Value = kvp.Value ?? DBNull.Value;
                    ConfigureParameter(p, kvp.Value);
                    cmd.Parameters.Add(p);
                }
            }

            return cmd;
        }

        #endregion
    }
}
