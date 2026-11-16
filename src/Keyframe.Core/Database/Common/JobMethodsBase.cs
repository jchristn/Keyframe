namespace Keyframe.Core.Database.Common
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Interfaces;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Job methods shared by every provider. Statements are portable; dialect differences come from the driver.
    /// </summary>
    public abstract class JobMethodsBase : IJobMethods
    {
        #region Private-Members

        private const string Columns = "id, mediakind, status, principalname, sourceurl, sourcesha256, sizebytes, durationms, progressstage, progresscompleted, progresstotal, progresspercent, error, createdutc, startedutc, completedutc, lastupdateutc";

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        protected JobMethodsBase(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task<Job> CreateAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            job.LastUpdateUtc = DateTime.UtcNow;
            DbCommandSpec spec = new DbCommandSpec(
                "INSERT INTO jobs (" + Columns + ") VALUES (@id, @mediakind, @status, @principalname, @sourceurl, @sourcesha256, @sizebytes, "
                + "@durationms, @progressstage, @progresscompleted, @progresstotal, @progresspercent, @error, @createdutc, @startedutc, @completedutc, @lastupdateutc)");
            Bind(spec, job);
            await _Driver.ExecuteTransactionAsync(new List<DbCommandSpec> { spec }, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc />
        public async Task<Job?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@id", id } };
            DataTable table = await _Driver.ExecuteQueryAsync("SELECT " + Columns + " FROM jobs WHERE id = @id", p, token).ConfigureAwait(false);
            if (table.Rows.Count < 1) return null;
            return FromRow(table.Rows[0]);
        }

        /// <inheritdoc />
        public async Task<Job> UpdateAsync(Job job, CancellationToken token = default)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));

            job.LastUpdateUtc = DateTime.UtcNow;
            DbCommandSpec spec = new DbCommandSpec(
                "UPDATE jobs SET mediakind = @mediakind, status = @status, principalname = @principalname, sourceurl = @sourceurl, "
                + "sourcesha256 = @sourcesha256, sizebytes = @sizebytes, durationms = @durationms, progressstage = @progressstage, "
                + "progresscompleted = @progresscompleted, progresstotal = @progresstotal, progresspercent = @progresspercent, error = @error, "
                + "createdutc = @createdutc, startedutc = @startedutc, completedutc = @completedutc, lastupdateutc = @lastupdateutc WHERE id = @id");
            Bind(spec, job);
            await _Driver.ExecuteTransactionAsync(new List<DbCommandSpec> { spec }, token).ConfigureAwait(false);
            return job;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<Job>> EnumerateAsync(JobFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            SqlWhere where = new SqlWhere();
            if (filter.Status != null) where.Add("status = @status", "@status", filter.Status.Value.ToString());
            if (filter.MediaKind != null) where.Add("mediakind = @mediakind", "@mediakind", filter.MediaKind.Value.ToString());
            if (!String.IsNullOrWhiteSpace(filter.PrincipalName)) where.Add("principalname = @principalname", "@principalname", filter.PrincipalName);
            if (filter.FromUtc != null) where.Add("createdutc >= @fromutc", "@fromutc", _Driver.ToDbDate(filter.FromUtc.Value));
            if (filter.ToUtc != null) where.Add("createdutc < @toutc", "@toutc", _Driver.ToDbDate(filter.ToUtc.Value));

            object? countObj = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM jobs" + where.ToSql(), where.Parameters, token).ConfigureAwait(false);
            long total = countObj == null ? 0 : Convert.ToInt64(countObj);

            Dictionary<string, object?> p = where.CopyParameters();
            p["@skip"] = (filter.PageNumber - 1) * filter.PageSize;
            p["@take"] = filter.PageSize;

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT " + Columns + " FROM jobs" + where.ToSql() + " ORDER BY createdutc DESC, id DESC" + _Driver.PageClause("@skip", "@take"),
                p, token).ConfigureAwait(false);

            EnumerationResult<Job> result = new EnumerationResult<Job>
            {
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)filter.PageSize)
            };

            foreach (DataRow row in table.Rows)
            {
                result.Items.Add(FromRow(row));
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<long> CountByStatusAsync(JobStatusEnum status, CancellationToken token = default)
        {
            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@status", status.ToString() } };
            object? countObj = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM jobs WHERE status = @status", p, token).ConfigureAwait(false);
            return countObj == null ? 0 : Convert.ToInt64(countObj);
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            List<DbCommandSpec> specs = new List<DbCommandSpec>
            {
                new DbCommandSpec("DELETE FROM jobresults WHERE jobid = @id").Add("@id", id),
                new DbCommandSpec("DELETE FROM jobs WHERE id = @id").Add("@id", id)
            };

            Job? existing = await ReadAsync(id, token).ConfigureAwait(false);
            if (existing == null) return false;
            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> MarkInterruptedAsync(string message, CancellationToken token = default)
        {
            DbCommandSpec spec = new DbCommandSpec(
                "UPDATE jobs SET status = @interrupted, error = @error, completedutc = @now, lastupdateutc = @now WHERE status = @queued OR status = @running")
                .Add("@interrupted", JobStatusEnum.Interrupted.ToString())
                .Add("@error", message)
                .Add("@now", _Driver.ToDbDate(DateTime.UtcNow))
                .Add("@queued", JobStatusEnum.Queued.ToString())
                .Add("@running", JobStatusEnum.Running.ToString());
            return await _Driver.ExecuteTransactionAsync(new List<DbCommandSpec> { spec }, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            string terminal = "status IN (@succeeded, @failed, @canceled, @interrupted) AND createdutc < @cutoff";
            Dictionary<string, object?> p = new Dictionary<string, object?>
            {
                { "@succeeded", JobStatusEnum.Succeeded.ToString() },
                { "@failed", JobStatusEnum.Failed.ToString() },
                { "@canceled", JobStatusEnum.Canceled.ToString() },
                { "@interrupted", JobStatusEnum.Interrupted.ToString() },
                { "@cutoff", _Driver.ToDbDate(olderThanUtc) }
            };

            object? countObj = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM jobs WHERE " + terminal, p, token).ConfigureAwait(false);
            int count = countObj == null ? 0 : Convert.ToInt32(countObj);
            if (count < 1) return 0;

            DbCommandSpec results = new DbCommandSpec("DELETE FROM jobresults WHERE jobid IN (SELECT id FROM jobs WHERE " + terminal + ")");
            DbCommandSpec jobs = new DbCommandSpec("DELETE FROM jobs WHERE " + terminal);
            foreach (KeyValuePair<string, object?> kvp in p)
            {
                results.Add(kvp.Key, kvp.Value);
                jobs.Add(kvp.Key, kvp.Value);
            }

            await _Driver.ExecuteTransactionAsync(new List<DbCommandSpec> { results, jobs }, token).ConfigureAwait(false);
            return count;
        }

        /// <inheritdoc />
        public async Task StoreResultAsync(JobResult result, CancellationToken token = default)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            result.ResultBytes = Encoding.UTF8.GetByteCount(result.ResultJson ?? String.Empty);
            List<DbCommandSpec> specs = new List<DbCommandSpec>
            {
                new DbCommandSpec("DELETE FROM jobresults WHERE jobid = @jobid").Add("@jobid", result.JobId),
                new DbCommandSpec("INSERT INTO jobresults (jobid, resultjson, resultbytes, createdutc) VALUES (@jobid, @resultjson, @resultbytes, @createdutc)")
                    .Add("@jobid", result.JobId)
                    .Add("@resultjson", result.ResultJson ?? "{}")
                    .Add("@resultbytes", result.ResultBytes)
                    .Add("@createdutc", _Driver.ToDbDate(result.CreatedUtc))
            };

            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<JobResult?> ReadResultAsync(string jobId, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(jobId)) throw new ArgumentNullException(nameof(jobId));

            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@jobid", jobId } };
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT jobid, resultjson, resultbytes, createdutc FROM jobresults WHERE jobid = @jobid", p, token).ConfigureAwait(false);
            if (table.Rows.Count < 1) return null;

            DataRow row = table.Rows[0];
            return new JobResult
            {
                JobId = RowReader.GetString(row, "jobid") ?? jobId,
                ResultJson = RowReader.GetString(row, "resultjson") ?? "{}",
                ResultBytes = RowReader.GetInt64(row, "resultbytes"),
                CreatedUtc = RowReader.GetDate(row, "createdutc") ?? DateTime.UtcNow
            };
        }

        #endregion

        #region Private-Methods

        private void Bind(DbCommandSpec spec, Job job)
        {
            spec.Add("@id", job.Id)
                .Add("@mediakind", job.MediaKind.ToString())
                .Add("@status", job.Status.ToString())
                .Add("@principalname", job.PrincipalName)
                .Add("@sourceurl", job.SourceUrl)
                .Add("@sourcesha256", job.SourceSha256)
                .Add("@sizebytes", job.SizeBytes)
                .Add("@durationms", job.DurationMs)
                .Add("@progressstage", job.ProgressStage)
                .Add("@progresscompleted", job.ProgressCompleted)
                .Add("@progresstotal", job.ProgressTotal)
                .Add("@progresspercent", job.ProgressPercent)
                .Add("@error", job.Error)
                .Add("@createdutc", _Driver.ToDbDate(job.CreatedUtc))
                .Add("@startedutc", _Driver.ToDbDate(job.StartedUtc))
                .Add("@completedutc", _Driver.ToDbDate(job.CompletedUtc))
                .Add("@lastupdateutc", _Driver.ToDbDate(job.LastUpdateUtc));
        }

        private static Job FromRow(DataRow row)
        {
            return new Job
            {
                Id = RowReader.GetString(row, "id") ?? "unknown",
                MediaKind = RowReader.GetEnum<MediaKindEnum>(row, "mediakind", MediaKindEnum.Unknown),
                Status = RowReader.GetEnum<JobStatusEnum>(row, "status", JobStatusEnum.Failed),
                PrincipalName = RowReader.GetString(row, "principalname"),
                SourceUrl = RowReader.GetString(row, "sourceurl"),
                SourceSha256 = RowReader.GetString(row, "sourcesha256"),
                SizeBytes = RowReader.GetInt64(row, "sizebytes"),
                DurationMs = RowReader.GetInt64(row, "durationms"),
                ProgressStage = RowReader.GetString(row, "progressstage"),
                ProgressCompleted = (int)RowReader.GetInt64(row, "progresscompleted"),
                ProgressTotal = (int)RowReader.GetInt64(row, "progresstotal"),
                ProgressPercent = RowReader.GetDouble(row, "progresspercent"),
                Error = RowReader.GetString(row, "error"),
                CreatedUtc = RowReader.GetDate(row, "createdutc") ?? DateTime.UtcNow,
                StartedUtc = RowReader.GetDate(row, "startedutc"),
                CompletedUtc = RowReader.GetDate(row, "completedutc"),
                LastUpdateUtc = RowReader.GetDate(row, "lastupdateutc") ?? DateTime.UtcNow
            };
        }

        #endregion
    }
}
