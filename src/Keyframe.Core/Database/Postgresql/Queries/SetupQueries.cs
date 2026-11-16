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
                }),
                new Migration(2, "Analytics: analyses and model call attempts", new List<string>
                {
                    "CREATE TABLE IF NOT EXISTS analyses (id VARCHAR(64) PRIMARY KEY, requestid VARCHAR(64) NULL, jobid VARCHAR(64) NULL, mediakind VARCHAR(16) NOT NULL, "
                    + "principalname VARCHAR(256) NULL, success BOOLEAN NOT NULL DEFAULT FALSE, error TEXT NULL, startedutc TIMESTAMPTZ NOT NULL, "
                    + "completedutc TIMESTAMPTZ NOT NULL, analysisms DOUBLE PRECISION NOT NULL DEFAULT 0, queuewaitms DOUBLE PRECISION NOT NULL DEFAULT 0, "
                    + "mediadurationms BIGINT NULL, mediabytes BIGINT NOT NULL DEFAULT 0, realtimefactor DOUBLE PRECISION NULL, visionendpoint VARCHAR(64) NULL, "
                    + "visionmodel VARCHAR(256) NULL, textendpoint VARCHAR(64) NULL, textmodel VARCHAR(256) NULL, transcriptionendpoint VARCHAR(64) NULL, "
                    + "transcriptionmodel VARCHAR(256) NULL, modelcalls INTEGER NOT NULL DEFAULT 0, failedmodelcalls INTEGER NOT NULL DEFAULT 0, "
                    + "retries INTEGER NOT NULL DEFAULT 0, repairattempts INTEGER NOT NULL DEFAULT 0, prompttokens BIGINT NOT NULL DEFAULT 0, "
                    + "completiontokens BIGINT NOT NULL DEFAULT 0, totaltokens BIGINT NOT NULL DEFAULT 0, cachedtokens BIGINT NOT NULL DEFAULT 0, "
                    + "reasoningtokens BIGINT NOT NULL DEFAULT 0, inputimages INTEGER NOT NULL DEFAULT 0, inputimagebytes BIGINT NOT NULL DEFAULT 0, "
                    + "inputaudiobytes BIGINT NOT NULL DEFAULT 0, scenes INTEGER NOT NULL DEFAULT 0, transcriptcharacters BIGINT NOT NULL DEFAULT 0, "
                    + "fieldcount INTEGER NOT NULL DEFAULT 0, invalidfields INTEGER NOT NULL DEFAULT 0, stagesjson TEXT NULL)",
                    "CREATE INDEX IF NOT EXISTS idx_analyses_completedutc ON analyses (completedutc)",
                    "CREATE INDEX IF NOT EXISTS idx_analyses_mediakind_completedutc ON analyses (mediakind, completedutc)",
                    "CREATE INDEX IF NOT EXISTS idx_analyses_jobid ON analyses (jobid)",
                    "CREATE TABLE IF NOT EXISTS modelcalls (id VARCHAR(64) PRIMARY KEY, analysisid VARCHAR(64) NOT NULL, purpose VARCHAR(64) NOT NULL, "
                    + "target VARCHAR(256) NULL, endpoint VARCHAR(64) NOT NULL, provider VARCHAR(64) NOT NULL, model VARCHAR(256) NULL, attempt INTEGER NOT NULL DEFAULT 1, "
                    + "startutc TIMESTAMPTZ NOT NULL, success BOOLEAN NOT NULL DEFAULT FALSE, error TEXT NULL, statuscode INTEGER NULL, "
                    + "durationms DOUBLE PRECISION NOT NULL DEFAULT 0, queuems DOUBLE PRECISION NOT NULL DEFAULT 0, ttftms DOUBLE PRECISION NULL, "
                    + "generationms DOUBLE PRECISION NULL, tokenspersecond DOUBLE PRECISION NULL, prompttokens BIGINT NULL, completiontokens BIGINT NULL, "
                    + "totaltokens BIGINT NULL, cachedtokens BIGINT NULL, reasoningtokens BIGINT NULL, inputimages INTEGER NOT NULL DEFAULT 0, "
                    + "inputimagebytes BIGINT NOT NULL DEFAULT 0, inputaudiobytes BIGINT NOT NULL DEFAULT 0, finishreason VARCHAR(64) NULL)",
                    "CREATE INDEX IF NOT EXISTS idx_modelcalls_analysisid ON modelcalls (analysisid)",
                    "CREATE INDEX IF NOT EXISTS idx_modelcalls_startutc ON modelcalls (startutc)",
                    "CREATE INDEX IF NOT EXISTS idx_modelcalls_endpoint_model ON modelcalls (endpoint, model)"
                })
            };
        }
    }
}
