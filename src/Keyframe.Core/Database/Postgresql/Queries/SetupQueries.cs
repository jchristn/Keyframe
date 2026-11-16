namespace Keyframe.Core.Database.Postgresql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// PostgreSQL schema statements.
    /// </summary>
    public static class SetupQueries
    {
        /// <summary>
        /// Creates the migration tracking table.
        /// </summary>
        public const string MigrationTable =
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, appliedutc TIMESTAMPTZ NOT NULL)";

        /// <summary>
        /// Every migration, in version order.
        /// </summary>
        /// <returns>Migrations.</returns>
        public static IReadOnlyList<Migration> Migrations()
        {
            return new List<Migration>
            {
                new Migration(1, "Initial schema: request history, headers, jobs, job results", new List<string>
                {
                    "CREATE TABLE IF NOT EXISTS requesthistory ("
                    + "id VARCHAR(64) PRIMARY KEY, requestid VARCHAR(64) NULL, principalname VARCHAR(256) NULL, method VARCHAR(16) NOT NULL, path TEXT NOT NULL, url TEXT NOT NULL, "
                    + "statuscode INTEGER NOT NULL DEFAULT 0, success BOOLEAN NOT NULL DEFAULT FALSE, durationms DOUBLE PRECISION NOT NULL DEFAULT 0, sourceip VARCHAR(128) NULL, "
                    + "requestcontenttype VARCHAR(256) NULL, responsecontenttype VARCHAR(256) NULL, requestbody TEXT NULL, requestbodybytes BIGINT NOT NULL DEFAULT 0, "
                    + "requestbodytruncated BOOLEAN NOT NULL DEFAULT FALSE, responsebody TEXT NULL, responsebodybytes BIGINT NOT NULL DEFAULT 0, "
                    + "responsebodytruncated BOOLEAN NOT NULL DEFAULT FALSE, createdutc TIMESTAMPTZ NOT NULL, completedutc TIMESTAMPTZ NULL)",
                    "CREATE INDEX IF NOT EXISTS idx_requesthistory_createdutc ON requesthistory (createdutc)",
                    "CREATE INDEX IF NOT EXISTS idx_requesthistory_principalname ON requesthistory (principalname)",
                    "CREATE TABLE IF NOT EXISTS requesthistoryheaders ("
                    + "entryid VARCHAR(64) NOT NULL, direction VARCHAR(16) NOT NULL, ordinal INTEGER NOT NULL, name VARCHAR(256) NOT NULL, value TEXT NOT NULL, "
                    + "PRIMARY KEY (entryid, direction, ordinal))",
                    "CREATE TABLE IF NOT EXISTS jobs ("
                    + "id VARCHAR(64) PRIMARY KEY, mediakind VARCHAR(16) NOT NULL, status VARCHAR(16) NOT NULL, principalname VARCHAR(256) NULL, sourceurl TEXT NULL, "
                    + "sourcesha256 VARCHAR(64) NULL, sizebytes BIGINT NOT NULL DEFAULT 0, durationms BIGINT NOT NULL DEFAULT 0, progressstage VARCHAR(64) NULL, "
                    + "progresscompleted INTEGER NOT NULL DEFAULT 0, progresstotal INTEGER NOT NULL DEFAULT 0, progresspercent DOUBLE PRECISION NOT NULL DEFAULT 0, "
                    + "error TEXT NULL, createdutc TIMESTAMPTZ NOT NULL, startedutc TIMESTAMPTZ NULL, completedutc TIMESTAMPTZ NULL, lastupdateutc TIMESTAMPTZ NOT NULL)",
                    "CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs (status)",
                    "CREATE INDEX IF NOT EXISTS idx_jobs_createdutc ON jobs (createdutc)",
                    "CREATE TABLE IF NOT EXISTS jobresults ("
                    + "jobid VARCHAR(64) PRIMARY KEY, resultjson TEXT NOT NULL, resultbytes BIGINT NOT NULL DEFAULT 0, createdutc TIMESTAMPTZ NOT NULL)"
                })
            };
        }
    }
}
