namespace Keyframe.Core.Database.Common
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database.Interfaces;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Request history methods shared by every provider. Provider classes derive from this and supply nothing
    /// beyond the driver, because the statements are portable; dialect differences (paging, timestamps) come
    /// from the driver.
    /// </summary>
    public abstract class RequestHistoryMethodsBase : IRequestHistoryMethods
    {
        #region Private-Members

        private const string ListColumns = "id, requestid, principalname, method, path, url, statuscode, durationms, sourceip, requestcontenttype, responsecontenttype, requestbodybytes, requestbodytruncated, responsebodybytes, responsebodytruncated, createdutc, completedutc";
        private const string DirectionRequest = "Request";
        private const string DirectionResponse = "Response";

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        protected RequestHistoryMethodsBase(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task CreateAsync(RequestHistoryEntry entry, CancellationToken token = default)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));

            List<DbCommandSpec> specs = new List<DbCommandSpec>();
            DbCommandSpec insert = new DbCommandSpec(
                "INSERT INTO requesthistory (id, requestid, principalname, method, path, url, statuscode, success, durationms, sourceip, "
                + "requestcontenttype, responsecontenttype, requestbody, requestbodybytes, requestbodytruncated, "
                + "responsebody, responsebodybytes, responsebodytruncated, createdutc, completedutc) VALUES "
                + "(@id, @requestid, @principalname, @method, @path, @url, @statuscode, @success, @durationms, @sourceip, "
                + "@requestcontenttype, @responsecontenttype, @requestbody, @requestbodybytes, @requestbodytruncated, "
                + "@responsebody, @responsebodybytes, @responsebodytruncated, @createdutc, @completedutc)");
            insert.Add("@id", entry.Id)
                .Add("@requestid", entry.RequestId)
                .Add("@principalname", entry.PrincipalName)
                .Add("@method", (entry.Method ?? String.Empty).ToUpperInvariant())
                .Add("@path", entry.Path ?? String.Empty)
                .Add("@url", entry.Url ?? String.Empty)
                .Add("@statuscode", entry.StatusCode)
                .Add("@success", entry.Success)
                .Add("@durationms", entry.DurationMs)
                .Add("@sourceip", entry.SourceIp)
                .Add("@requestcontenttype", entry.RequestContentType)
                .Add("@responsecontenttype", entry.ResponseContentType)
                .Add("@requestbody", entry.RequestBody)
                .Add("@requestbodybytes", entry.RequestBodyBytes)
                .Add("@requestbodytruncated", entry.RequestBodyTruncated)
                .Add("@responsebody", entry.ResponseBody)
                .Add("@responsebodybytes", entry.ResponseBodyBytes)
                .Add("@responsebodytruncated", entry.ResponseBodyTruncated)
                .Add("@createdutc", _Driver.ToDbDate(entry.CreatedUtc))
                .Add("@completedutc", _Driver.ToDbDate(entry.CompletedUtc));
            specs.Add(insert);

            AddHeaderInserts(specs, entry.Id, DirectionRequest, entry.RequestHeaders);
            AddHeaderInserts(specs, entry.Id, DirectionResponse, entry.ResponseHeaders);

            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<RequestHistoryEntry?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@id", id } };
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT " + ListColumns + ", requestbody, responsebody FROM requesthistory WHERE id = @id", p, token).ConfigureAwait(false);
            if (table.Rows.Count < 1) return null;

            RequestHistoryEntry entry = FromRow(table.Rows[0]);
            entry.RequestBody = RowReader.GetString(table.Rows[0], "requestbody");
            entry.ResponseBody = RowReader.GetString(table.Rows[0], "responsebody");

            DataTable headers = await _Driver.ExecuteQueryAsync(
                "SELECT direction, name, value FROM requesthistoryheaders WHERE entryid = @id ORDER BY ordinal", p, token).ConfigureAwait(false);
            foreach (DataRow row in headers.Rows)
            {
                string direction = RowReader.GetString(row, "direction") ?? DirectionRequest;
                string name = RowReader.GetString(row, "name") ?? String.Empty;
                string value = RowReader.GetString(row, "value") ?? String.Empty;
                Dictionary<string, string> target = direction == DirectionResponse ? entry.ResponseHeaders! : entry.RequestHeaders!;
                target[name] = value;
            }

            return entry;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<RequestHistoryEntry>> EnumerateAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            SqlWhere where = BuildWhere(filter);
            object? countObj = await _Driver.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM requesthistory" + where.ToSql(), where.Parameters, token).ConfigureAwait(false);
            long total = countObj == null ? 0 : Convert.ToInt64(countObj);

            Dictionary<string, object?> p = where.CopyParameters();
            p["@skip"] = (filter.PageNumber - 1) * filter.PageSize;
            p["@take"] = filter.PageSize;

            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT " + ListColumns + " FROM requesthistory" + where.ToSql() + " ORDER BY createdutc DESC, id DESC"
                + _Driver.PageClause("@skip", "@take"), p, token).ConfigureAwait(false);

            EnumerationResult<RequestHistoryEntry> result = new EnumerationResult<RequestHistoryEntry>
            {
                PageNumber = filter.PageNumber,
                PageSize = filter.PageSize,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)filter.PageSize)
            };

            foreach (DataRow row in table.Rows)
            {
                RequestHistoryEntry entry = FromRow(row);
                entry.RequestHeaders = null;
                entry.ResponseHeaders = null;
                result.Items.Add(entry);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<RequestHistorySummary> SummarizeAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            DateTime to = (filter.ToUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime from = (filter.FromUtc ?? to.AddHours(-24)).ToUniversalTime();
            if (from >= to) from = to.AddMinutes(-filter.BucketMinutes);

            TimeSpan width = TimeSpan.FromMinutes(filter.BucketMinutes);
            DateTime alignedFrom = new DateTime(from.Ticks - (from.Ticks % width.Ticks), DateTimeKind.Utc);
            int bucketCount = (int)Math.Ceiling((to - alignedFrom).Ticks / (double)width.Ticks);
            if (bucketCount > 10000) throw new ArgumentOutOfRangeException(nameof(filter), "The range produces more than 10000 buckets; widen BucketMinutes.");

            RequestHistoryFilter scoped = new RequestHistoryFilter
            {
                PrincipalName = filter.PrincipalName,
                Method = filter.Method,
                StatusCode = filter.StatusCode,
                PathContains = filter.PathContains,
                FromUtc = alignedFrom,
                ToUtc = to
            };

            SqlWhere where = BuildWhere(scoped);
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT createdutc, statuscode, durationms FROM requesthistory" + where.ToSql(), where.Parameters, token).ConfigureAwait(false);

            RequestHistorySummary summary = new RequestHistorySummary { BucketMinutes = filter.BucketMinutes };
            long[] success = new long[bucketCount];
            long[] failure = new long[bucketCount];
            double[] duration = new double[bucketCount];
            double totalDuration = 0;

            foreach (DataRow row in table.Rows)
            {
                DateTime? created = RowReader.GetDate(row, "createdutc");
                if (created == null) continue;
                int index = (int)((created.Value - alignedFrom).Ticks / width.Ticks);
                if (index < 0 || index >= bucketCount) continue;

                long status = RowReader.GetInt64(row, "statuscode");
                double ms = RowReader.GetDouble(row, "durationms");
                if (status > 0 && status < 400) success[index]++;
                else failure[index]++;
                duration[index] += ms;
                totalDuration += ms;
            }

            for (int i = 0; i < bucketCount; i++)
            {
                long count = success[i] + failure[i];
                summary.Buckets.Add(new RequestHistorySummaryBucket
                {
                    BucketStartUtc = alignedFrom.Add(TimeSpan.FromTicks(width.Ticks * i)),
                    BucketEndUtc = alignedFrom.Add(TimeSpan.FromTicks(width.Ticks * (i + 1))),
                    SuccessCount = success[i],
                    FailureCount = failure[i],
                    AverageDurationMs = count > 0 ? Math.Round(duration[i] / count, 2) : 0
                });
                summary.TotalSuccess += success[i];
                summary.TotalFailure += failure[i];
            }

            summary.TotalCount = summary.TotalSuccess + summary.TotalFailure;
            summary.AverageDurationMs = summary.TotalCount > 0 ? Math.Round(totalDuration / summary.TotalCount, 2) : 0;
            return summary;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            List<DbCommandSpec> specs = new List<DbCommandSpec>
            {
                new DbCommandSpec("DELETE FROM requesthistoryheaders WHERE entryid = @id").Add("@id", id),
                new DbCommandSpec("DELETE FROM requesthistory WHERE id = @id").Add("@id", id)
            };

            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@id", id } };
            object? exists = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM requesthistory WHERE id = @id", p, token).ConfigureAwait(false);
            if (exists == null || Convert.ToInt64(exists) < 1) return false;

            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> DeleteManyAsync(RequestHistoryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            SqlWhere where = BuildWhere(filter);
            return await DeleteWhereAsync(where, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            SqlWhere where = new SqlWhere();
            where.Add("createdutc < @cutoff", "@cutoff", _Driver.ToDbDate(olderThanUtc));
            return await DeleteWhereAsync(where, token).ConfigureAwait(false);
        }

        #endregion

        #region Private-Methods

        private async Task<int> DeleteWhereAsync(SqlWhere where, CancellationToken token)
        {
            object? countObj = await _Driver.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM requesthistory" + where.ToSql(), where.Parameters, token).ConfigureAwait(false);
            int count = countObj == null ? 0 : Convert.ToInt32(countObj);
            if (count < 1) return 0;

            DbCommandSpec headers = new DbCommandSpec(
                "DELETE FROM requesthistoryheaders WHERE entryid IN (SELECT id FROM requesthistory" + where.ToSql() + ")");
            DbCommandSpec entries = new DbCommandSpec("DELETE FROM requesthistory" + where.ToSql());
            foreach (KeyValuePair<string, object?> kvp in where.Parameters)
            {
                headers.Add(kvp.Key, kvp.Value);
                entries.Add(kvp.Key, kvp.Value);
            }

            await _Driver.ExecuteTransactionAsync(new List<DbCommandSpec> { headers, entries }, token).ConfigureAwait(false);
            return count;
        }

        private SqlWhere BuildWhere(RequestHistoryFilter filter)
        {
            SqlWhere where = new SqlWhere();
            if (!String.IsNullOrWhiteSpace(filter.PrincipalName)) where.Add("principalname = @principalname", "@principalname", filter.PrincipalName);
            if (!String.IsNullOrWhiteSpace(filter.Method)) where.Add("method = @method", "@method", filter.Method.Trim().ToUpperInvariant());
            if (filter.StatusCode != null) where.Add("statuscode = @statuscode", "@statuscode", filter.StatusCode.Value);
            if (!String.IsNullOrWhiteSpace(filter.PathContains)) where.Add("path LIKE @pathcontains ESCAPE '!'", "@pathcontains", "%" + SqlWhere.EscapeLike(filter.PathContains) + "%");
            if (filter.FromUtc != null) where.Add("createdutc >= @fromutc", "@fromutc", _Driver.ToDbDate(filter.FromUtc.Value));
            if (filter.ToUtc != null) where.Add("createdutc < @toutc", "@toutc", _Driver.ToDbDate(filter.ToUtc.Value));
            return where;
        }

        private static void AddHeaderInserts(List<DbCommandSpec> specs, string entryId, string direction, Dictionary<string, string>? headers)
        {
            if (headers == null) return;
            int ordinal = 0;
            foreach (KeyValuePair<string, string> kvp in headers.OrderBy(h => h.Key, StringComparer.OrdinalIgnoreCase))
            {
                specs.Add(new DbCommandSpec(
                    "INSERT INTO requesthistoryheaders (entryid, direction, ordinal, name, value) VALUES (@entryid, @direction, @ordinal, @name, @value)")
                    .Add("@entryid", entryId)
                    .Add("@direction", direction)
                    .Add("@ordinal", ordinal++)
                    .Add("@name", kvp.Key)
                    .Add("@value", kvp.Value ?? String.Empty));
            }
        }

        private static RequestHistoryEntry FromRow(DataRow row)
        {
            return new RequestHistoryEntry
            {
                Id = RowReader.GetString(row, "id") ?? "unknown",
                RequestId = RowReader.GetString(row, "requestid"),
                PrincipalName = RowReader.GetString(row, "principalname"),
                Method = RowReader.GetString(row, "method") ?? String.Empty,
                Path = RowReader.GetString(row, "path") ?? String.Empty,
                Url = RowReader.GetString(row, "url") ?? String.Empty,
                StatusCode = (int)RowReader.GetInt64(row, "statuscode"),
                DurationMs = RowReader.GetDouble(row, "durationms"),
                SourceIp = RowReader.GetString(row, "sourceip"),
                RequestContentType = RowReader.GetString(row, "requestcontenttype"),
                ResponseContentType = RowReader.GetString(row, "responsecontenttype"),
                RequestBodyBytes = RowReader.GetInt64(row, "requestbodybytes"),
                RequestBodyTruncated = RowReader.GetBoolean(row, "requestbodytruncated"),
                ResponseBodyBytes = RowReader.GetInt64(row, "responsebodybytes"),
                ResponseBodyTruncated = RowReader.GetBoolean(row, "responsebodytruncated"),
                CreatedUtc = RowReader.GetDate(row, "createdutc") ?? DateTime.UtcNow,
                CompletedUtc = RowReader.GetDate(row, "completedutc")
            };
        }

        #endregion
    }
}
