namespace Keyframe.Core.Database.Sqlite.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// SQLite schema statements.
    /// </summary>
    public static class SetupQueries
    {
        /// <summary>
        /// Creates the migration tracking table.
        /// </summary>
        public const string MigrationTable =
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, description TEXT NOT NULL, appliedutc TEXT NOT NULL)";

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
                    + "id TEXT PRIMARY KEY, requestid TEXT NULL, principalname TEXT NULL, method TEXT NOT NULL, path TEXT NOT NULL, url TEXT NOT NULL, "
                    + "statuscode INTEGER NOT NULL DEFAULT 0, success INTEGER NOT NULL DEFAULT 0, durationms REAL NOT NULL DEFAULT 0, sourceip TEXT NULL, "
                    + "requestcontenttype TEXT NULL, responsecontenttype TEXT NULL, requestbody TEXT NULL, requestbodybytes INTEGER NOT NULL DEFAULT 0, "
                    + "requestbodytruncated INTEGER NOT NULL DEFAULT 0, responsebody TEXT NULL, responsebodybytes INTEGER NOT NULL DEFAULT 0, "
                    + "responsebodytruncated INTEGER NOT NULL DEFAULT 0, createdutc TEXT NOT NULL, completedutc TEXT NULL)",
                    "CREATE INDEX IF NOT EXISTS idx_requesthistory_createdutc ON requesthistory (createdutc)",
                    "CREATE INDEX IF NOT EXISTS idx_requesthistory_principalname ON requesthistory (principalname)",
                    "CREATE TABLE IF NOT EXISTS requesthistoryheaders ("
                    + "entryid TEXT NOT NULL, direction TEXT NOT NULL, ordinal INTEGER NOT NULL, name TEXT NOT NULL, value TEXT NOT NULL, "
                    + "PRIMARY KEY (entryid, direction, ordinal))",
                    "CREATE TABLE IF NOT EXISTS jobs ("
                    + "id TEXT PRIMARY KEY, mediakind TEXT NOT NULL, status TEXT NOT NULL, principalname TEXT NULL, sourceurl TEXT NULL, sourcesha256 TEXT NULL, "
                    + "sizebytes INTEGER NOT NULL DEFAULT 0, durationms INTEGER NOT NULL DEFAULT 0, progressstage TEXT NULL, progresscompleted INTEGER NOT NULL DEFAULT 0, "
                    + "progresstotal INTEGER NOT NULL DEFAULT 0, progresspercent REAL NOT NULL DEFAULT 0, error TEXT NULL, createdutc TEXT NOT NULL, "
                    + "startedutc TEXT NULL, completedutc TEXT NULL, lastupdateutc TEXT NOT NULL)",
                    "CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs (status)",
                    "CREATE INDEX IF NOT EXISTS idx_jobs_createdutc ON jobs (createdutc)",
                    "CREATE TABLE IF NOT EXISTS jobresults ("
                    + "jobid TEXT PRIMARY KEY, resultjson TEXT NOT NULL, resultbytes INTEGER NOT NULL DEFAULT 0, createdutc TEXT NOT NULL)"
                })
            };
        }
    }
}
