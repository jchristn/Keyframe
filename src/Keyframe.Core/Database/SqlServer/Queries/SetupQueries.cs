namespace Keyframe.Core.Database.SqlServer.Queries
{
    using System.Collections.Generic;

    /// <summary>
    /// SQL Server schema statements, guarded with existence checks.
    /// </summary>
    public static class SetupQueries
    {
        /// <summary>
        /// Creates the migration tracking table.
        /// </summary>
        public const string MigrationTable =
            "IF OBJECT_ID(N'dbo.schema_migrations', N'U') IS NULL CREATE TABLE dbo.schema_migrations (version INT PRIMARY KEY, description NVARCHAR(512) NOT NULL, appliedutc DATETIME2(7) NOT NULL)";

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
                    "IF OBJECT_ID(N'dbo.requesthistory', N'U') IS NULL CREATE TABLE dbo.requesthistory ("
                    + "id NVARCHAR(64) PRIMARY KEY, requestid NVARCHAR(64) NULL, principalname NVARCHAR(256) NULL, method NVARCHAR(16) NOT NULL, path NVARCHAR(MAX) NOT NULL, url NVARCHAR(MAX) NOT NULL, "
                    + "statuscode INT NOT NULL DEFAULT 0, success BIT NOT NULL DEFAULT 0, durationms FLOAT NOT NULL DEFAULT 0, sourceip NVARCHAR(128) NULL, "
                    + "requestcontenttype NVARCHAR(256) NULL, responsecontenttype NVARCHAR(256) NULL, requestbody NVARCHAR(MAX) NULL, requestbodybytes BIGINT NOT NULL DEFAULT 0, "
                    + "requestbodytruncated BIT NOT NULL DEFAULT 0, responsebody NVARCHAR(MAX) NULL, responsebodybytes BIGINT NOT NULL DEFAULT 0, "
                    + "responsebodytruncated BIT NOT NULL DEFAULT 0, createdutc DATETIME2(7) NOT NULL, completedutc DATETIME2(7) NULL)",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_requesthistory_createdutc') CREATE INDEX idx_requesthistory_createdutc ON dbo.requesthistory (createdutc)",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_requesthistory_principalname') CREATE INDEX idx_requesthistory_principalname ON dbo.requesthistory (principalname)",
                    "IF OBJECT_ID(N'dbo.requesthistoryheaders', N'U') IS NULL CREATE TABLE dbo.requesthistoryheaders ("
                    + "entryid NVARCHAR(64) NOT NULL, direction NVARCHAR(16) NOT NULL, ordinal INT NOT NULL, name NVARCHAR(256) NOT NULL, value NVARCHAR(MAX) NOT NULL, "
                    + "PRIMARY KEY (entryid, direction, ordinal))",
                    "IF OBJECT_ID(N'dbo.jobs', N'U') IS NULL CREATE TABLE dbo.jobs ("
                    + "id NVARCHAR(64) PRIMARY KEY, mediakind NVARCHAR(16) NOT NULL, status NVARCHAR(16) NOT NULL, principalname NVARCHAR(256) NULL, sourceurl NVARCHAR(MAX) NULL, "
                    + "sourcesha256 NVARCHAR(64) NULL, sizebytes BIGINT NOT NULL DEFAULT 0, durationms BIGINT NOT NULL DEFAULT 0, progressstage NVARCHAR(64) NULL, "
                    + "progresscompleted INT NOT NULL DEFAULT 0, progresstotal INT NOT NULL DEFAULT 0, progresspercent FLOAT NOT NULL DEFAULT 0, "
                    + "error NVARCHAR(MAX) NULL, createdutc DATETIME2(7) NOT NULL, startedutc DATETIME2(7) NULL, completedutc DATETIME2(7) NULL, lastupdateutc DATETIME2(7) NOT NULL)",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_jobs_status') CREATE INDEX idx_jobs_status ON dbo.jobs (status)",
                    "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'idx_jobs_createdutc') CREATE INDEX idx_jobs_createdutc ON dbo.jobs (createdutc)",
                    "IF OBJECT_ID(N'dbo.jobresults', N'U') IS NULL CREATE TABLE dbo.jobresults ("
                    + "jobid NVARCHAR(64) PRIMARY KEY, resultjson NVARCHAR(MAX) NOT NULL, resultbytes BIGINT NOT NULL DEFAULT 0, createdutc DATETIME2(7) NOT NULL)"
                })
            };
        }
    }
}
