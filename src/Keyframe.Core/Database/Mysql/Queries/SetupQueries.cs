namespace Keyframe.Core.Database.Mysql.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// MySQL schema statements. Indexes are declared inline because MySQL has no CREATE INDEX IF NOT EXISTS.
    /// </summary>
    public static class SetupQueries
    {
        /// <summary>
        /// Creates the migration tracking table.
        /// </summary>
        public const string MigrationTable =
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INT PRIMARY KEY, description VARCHAR(512) NOT NULL, appliedutc DATETIME(6) NOT NULL)";

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
                    + "statuscode INT NOT NULL DEFAULT 0, success BOOLEAN NOT NULL DEFAULT FALSE, durationms DOUBLE NOT NULL DEFAULT 0, sourceip VARCHAR(128) NULL, "
                    + "requestcontenttype VARCHAR(256) NULL, responsecontenttype VARCHAR(256) NULL, requestbody LONGTEXT NULL, requestbodybytes BIGINT NOT NULL DEFAULT 0, "
                    + "requestbodytruncated BOOLEAN NOT NULL DEFAULT FALSE, responsebody LONGTEXT NULL, responsebodybytes BIGINT NOT NULL DEFAULT 0, "
                    + "responsebodytruncated BOOLEAN NOT NULL DEFAULT FALSE, createdutc DATETIME(6) NOT NULL, completedutc DATETIME(6) NULL, "
                    + "INDEX idx_requesthistory_createdutc (createdutc), INDEX idx_requesthistory_principalname (principalname))",
                    "CREATE TABLE IF NOT EXISTS requesthistoryheaders ("
                    + "entryid VARCHAR(64) NOT NULL, direction VARCHAR(16) NOT NULL, ordinal INT NOT NULL, name VARCHAR(256) NOT NULL, value TEXT NOT NULL, "
                    + "PRIMARY KEY (entryid, direction, ordinal))",
                    "CREATE TABLE IF NOT EXISTS jobs ("
                    + "id VARCHAR(64) PRIMARY KEY, mediakind VARCHAR(16) NOT NULL, status VARCHAR(16) NOT NULL, principalname VARCHAR(256) NULL, sourceurl TEXT NULL, "
                    + "sourcesha256 VARCHAR(64) NULL, sizebytes BIGINT NOT NULL DEFAULT 0, durationms BIGINT NOT NULL DEFAULT 0, progressstage VARCHAR(64) NULL, "
                    + "progresscompleted INT NOT NULL DEFAULT 0, progresstotal INT NOT NULL DEFAULT 0, progresspercent DOUBLE NOT NULL DEFAULT 0, "
                    + "error TEXT NULL, createdutc DATETIME(6) NOT NULL, startedutc DATETIME(6) NULL, completedutc DATETIME(6) NULL, lastupdateutc DATETIME(6) NOT NULL, "
                    + "INDEX idx_jobs_status (status), INDEX idx_jobs_createdutc (createdutc))",
                    "CREATE TABLE IF NOT EXISTS jobresults ("
                    + "jobid VARCHAR(64) PRIMARY KEY, resultjson LONGTEXT NOT NULL, resultbytes BIGINT NOT NULL DEFAULT 0, createdutc DATETIME(6) NOT NULL)"
                })
            };
        }
    }
}
