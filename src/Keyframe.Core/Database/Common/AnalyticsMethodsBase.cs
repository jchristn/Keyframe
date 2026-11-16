namespace Keyframe.Core.Database.Common
{
    using System;
    using System.Collections.Generic;
    using System.Data;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analytics;
    using Keyframe.Core.Database.Interfaces;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;

    /// <summary>
    /// Analytics methods shared by every provider. The statements are portable; dialect differences (paging,
    /// timestamps) come from the driver. Summaries load the filtered rows and aggregate them in memory with
    /// <see cref="AnalyticsAggregator"/>, so percentiles are exact and identical on every provider.
    /// </summary>
    public abstract class AnalyticsMethodsBase : IAnalyticsMethods
    {
        #region Private-Members

        private const string AnalysisColumns =
            "id, requestid, jobid, mediakind, principalname, success, error, startedutc, completedutc, analysisms, queuewaitms, "
            + "mediadurationms, mediabytes, realtimefactor, visionendpoint, visionmodel, textendpoint, textmodel, transcriptionendpoint, "
            + "transcriptionmodel, modelcalls, failedmodelcalls, retries, repairattempts, prompttokens, completiontokens, totaltokens, "
            + "cachedtokens, reasoningtokens, inputimages, inputimagebytes, inputaudiobytes, scenes, transcriptcharacters, fieldcount, "
            + "invalidfields, stagesjson";

        private const string CallColumns =
            "id, analysisid, purpose, target, endpoint, provider, model, attempt, startutc, success, error, statuscode, durationms, "
            + "queuems, ttftms, generationms, tokenspersecond, prompttokens, completiontokens, totaltokens, cachedtokens, "
            + "reasoningtokens, inputimages, inputimagebytes, inputaudiobytes, finishreason";

        private readonly DatabaseDriverBase _Driver;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="driver">Owning driver.</param>
        /// <exception cref="ArgumentNullException">Thrown when driver is null.</exception>
        protected AnalyticsMethodsBase(DatabaseDriverBase driver)
        {
            _Driver = driver ?? throw new ArgumentNullException(nameof(driver));
        }

        #endregion

        #region Public-Methods

        /// <inheritdoc />
        public async Task CreateAsync(AnalysisRecord record, CancellationToken token = default)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));

            List<DbCommandSpec> specs = new List<DbCommandSpec>();
            DbCommandSpec insert = new DbCommandSpec(
                "INSERT INTO analyses (" + AnalysisColumns + ") VALUES ("
                + "@id, @requestid, @jobid, @mediakind, @principalname, @success, @error, @startedutc, @completedutc, @analysisms, @queuewaitms, "
                + "@mediadurationms, @mediabytes, @realtimefactor, @visionendpoint, @visionmodel, @textendpoint, @textmodel, @transcriptionendpoint, "
                + "@transcriptionmodel, @modelcalls, @failedmodelcalls, @retries, @repairattempts, @prompttokens, @completiontokens, @totaltokens, "
                + "@cachedtokens, @reasoningtokens, @inputimages, @inputimagebytes, @inputaudiobytes, @scenes, @transcriptcharacters, @fieldcount, "
                + "@invalidfields, @stagesjson)");
            insert.Add("@id", record.Id)
                .Add("@requestid", record.RequestId)
                .Add("@jobid", record.JobId)
                .Add("@mediakind", record.MediaKind.ToString())
                .Add("@principalname", record.PrincipalName)
                .Add("@success", record.Success)
                .Add("@error", record.Error)
                .Add("@startedutc", _Driver.ToDbDate(record.StartedUtc))
                .Add("@completedutc", _Driver.ToDbDate(record.CompletedUtc))
                .Add("@analysisms", record.AnalysisMs)
                .Add("@queuewaitms", record.QueueWaitMs)
                .Add("@mediadurationms", record.MediaDurationMs)
                .Add("@mediabytes", record.MediaBytes)
                .Add("@realtimefactor", record.RealtimeFactor)
                .Add("@visionendpoint", record.VisionEndpoint)
                .Add("@visionmodel", record.VisionModel)
                .Add("@textendpoint", record.TextEndpoint)
                .Add("@textmodel", record.TextModel)
                .Add("@transcriptionendpoint", record.TranscriptionEndpoint)
                .Add("@transcriptionmodel", record.TranscriptionModel)
                .Add("@modelcalls", record.ModelCalls)
                .Add("@failedmodelcalls", record.FailedModelCalls)
                .Add("@retries", record.Retries)
                .Add("@repairattempts", record.RepairAttempts)
                .Add("@prompttokens", record.PromptTokens)
                .Add("@completiontokens", record.CompletionTokens)
                .Add("@totaltokens", record.TotalTokens)
                .Add("@cachedtokens", record.CachedTokens)
                .Add("@reasoningtokens", record.ReasoningTokens)
                .Add("@inputimages", record.InputImages)
                .Add("@inputimagebytes", record.InputImageBytes)
                .Add("@inputaudiobytes", record.InputAudioBytes)
                .Add("@scenes", record.Scenes)
                .Add("@transcriptcharacters", record.TranscriptCharacters)
                .Add("@fieldcount", record.FieldCount)
                .Add("@invalidfields", record.InvalidFields)
                .Add("@stagesjson", KeyframeSerializer.Serialize(record.Stages, false));
            specs.Add(insert);

            foreach (ModelCallRecord call in record.Calls ?? new List<ModelCallRecord>())
            {
                DbCommandSpec row = new DbCommandSpec(
                    "INSERT INTO modelcalls (" + CallColumns + ") VALUES ("
                    + "@id, @analysisid, @purpose, @target, @endpoint, @provider, @model, @attempt, @startutc, @success, @error, @statuscode, @durationms, "
                    + "@queuems, @ttftms, @generationms, @tokenspersecond, @prompttokens, @completiontokens, @totaltokens, @cachedtokens, "
                    + "@reasoningtokens, @inputimages, @inputimagebytes, @inputaudiobytes, @finishreason)");
                row.Add("@id", call.Id)
                    .Add("@analysisid", record.Id)
                    .Add("@purpose", call.Purpose.ToString())
                    .Add("@target", call.Target)
                    .Add("@endpoint", call.Endpoint)
                    .Add("@provider", call.Provider.ToString())
                    .Add("@model", call.Model)
                    .Add("@attempt", call.Attempt)
                    .Add("@startutc", _Driver.ToDbDate(call.StartUtc))
                    .Add("@success", call.Success)
                    .Add("@error", call.Error)
                    .Add("@statuscode", call.StatusCode)
                    .Add("@durationms", call.DurationMs)
                    .Add("@queuems", call.QueueMs)
                    .Add("@ttftms", call.TimeToFirstTokenMs)
                    .Add("@generationms", call.GenerationMs)
                    .Add("@tokenspersecond", call.TokensPerSecond)
                    .Add("@prompttokens", call.PromptTokens)
                    .Add("@completiontokens", call.CompletionTokens)
                    .Add("@totaltokens", call.TotalTokens)
                    .Add("@cachedtokens", call.CachedTokens)
                    .Add("@reasoningtokens", call.ReasoningTokens)
                    .Add("@inputimages", call.InputImages)
                    .Add("@inputimagebytes", call.InputImageBytes)
                    .Add("@inputaudiobytes", call.InputAudioBytes)
                    .Add("@finishreason", call.FinishReason);
                specs.Add(row);
            }

            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public async Task<AnalysisRecord?> ReadAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@id", id } };
            DataTable table = await _Driver.ExecuteQueryAsync("SELECT " + AnalysisColumns + " FROM analyses WHERE id = @id", p, token).ConfigureAwait(false);
            if (table.Rows.Count < 1) return null;

            AnalysisRecord record = AnalysisFromRow(table.Rows[0]);
            DataTable calls = await _Driver.ExecuteQueryAsync(
                "SELECT " + CallColumns + " FROM modelcalls WHERE analysisid = @id ORDER BY startutc, attempt, id", p, token).ConfigureAwait(false);
            record.Calls = new List<ModelCallRecord>();
            foreach (DataRow row in calls.Rows) record.Calls.Add(CallFromRow(row));
            return record;
        }

        /// <inheritdoc />
        public async Task<EnumerationResult<AnalysisRecord>> EnumerateAsync(AnalyticsFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            SqlWhere where = new SqlWhere();
            if (filter.MediaKind != null) where.Add("mediakind = @mediakind", "@mediakind", filter.MediaKind.Value.ToString());
            if (filter.Success != null) where.Add("success = @success", "@success", filter.Success.Value);
            if (filter.FromUtc != null) where.Add("completedutc >= @fromutc", "@fromutc", _Driver.ToDbDate(filter.FromUtc.Value));
            if (filter.ToUtc != null) where.Add("completedutc < @toutc", "@toutc", _Driver.ToDbDate(filter.ToUtc.Value));

            object? countObj = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM analyses" + where.ToSql(), where.Parameters, token).ConfigureAwait(false);
            long total = countObj == null ? 0 : Convert.ToInt64(countObj);

            Dictionary<string, object?> p = where.CopyParameters();
            p["@skip"] = filter.Skip;
            p["@take"] = filter.MaxResults;
            DataTable table = await _Driver.ExecuteQueryAsync(
                "SELECT " + AnalysisColumns + " FROM analyses" + where.ToSql() + " ORDER BY completedutc DESC, id DESC" + _Driver.PageClause("@skip", "@take"),
                p, token).ConfigureAwait(false);

            EnumerationResult<AnalysisRecord> result = new EnumerationResult<AnalysisRecord>
            {
                PageNumber = filter.Skip / filter.MaxResults + 1,
                PageSize = filter.MaxResults,
                TotalCount = total,
                TotalPages = (int)Math.Ceiling(total / (double)filter.MaxResults)
            };

            foreach (DataRow row in table.Rows) result.Items.Add(AnalysisFromRow(row));
            return result;
        }

        /// <inheritdoc />
        public async Task<AnalyticsSummary> SummarizeAsync(AnalyticsSummaryFilter filter, CancellationToken token = default)
        {
            if (filter == null) throw new ArgumentNullException(nameof(filter));

            DateTime to = (filter.ToUtc ?? DateTime.UtcNow).ToUniversalTime();
            DateTime from = (filter.FromUtc ?? to.AddHours(-24)).ToUniversalTime();
            AnalyticsBucketEnum bucket = filter.Bucket ?? AnalyticsAggregator.ChooseBucket(to > from ? to - from : TimeSpan.Zero);
            if (from >= to) from = to.Add(-AnalyticsAggregator.Width(bucket));
            DateTime alignedFrom = AnalyticsAggregator.Align(from, bucket);

            long buckets = (long)Math.Ceiling((to - alignedFrom).Ticks / (double)AnalyticsAggregator.Width(bucket).Ticks);
            if (buckets > AnalyticsAggregator.MaxBuckets)
                throw new ArgumentOutOfRangeException(nameof(filter), "The range needs " + buckets + " " + bucket.ToString().ToLowerInvariant() + " buckets, over the limit of " + AnalyticsAggregator.MaxBuckets + "; choose a wider bucket or a shorter range.");

            SqlWhere where = new SqlWhere();
            where.Add("completedutc >= @fromutc", "@fromutc", _Driver.ToDbDate(alignedFrom));
            where.Add("completedutc < @toutc", "@toutc", _Driver.ToDbDate(to));
            if (filter.MediaKind != null) where.Add("mediakind = @mediakind", "@mediakind", filter.MediaKind.Value.ToString());
            if (!String.IsNullOrWhiteSpace(filter.Endpoint))
                where.Add("(visionendpoint = @endpoint OR textendpoint = @endpoint OR transcriptionendpoint = @endpoint)", "@endpoint", filter.Endpoint.Trim());
            if (!String.IsNullOrWhiteSpace(filter.Model))
                where.Add("(visionmodel = @model OR textmodel = @model OR transcriptionmodel = @model)", "@model", filter.Model.Trim());

            DataTable analysisRows = await _Driver.ExecuteQueryAsync("SELECT " + AnalysisColumns + " FROM analyses" + where.ToSql(), where.Parameters, token).ConfigureAwait(false);
            List<AnalysisRecord> analyses = new List<AnalysisRecord>();
            foreach (DataRow row in analysisRows.Rows) analyses.Add(AnalysisFromRow(row));

            string callSql = "SELECT " + CallColumns + " FROM modelcalls WHERE analysisid IN (SELECT id FROM analyses" + where.ToSql() + ")";
            if (!String.IsNullOrWhiteSpace(filter.Endpoint)) callSql += " AND endpoint = @endpoint";
            if (!String.IsNullOrWhiteSpace(filter.Model)) callSql += " AND model = @model";
            DataTable callRows = await _Driver.ExecuteQueryAsync(callSql, where.Parameters, token).ConfigureAwait(false);
            List<ModelCallRecord> calls = new List<ModelCallRecord>();
            foreach (DataRow row in callRows.Rows) calls.Add(CallFromRow(row));

            AnalyticsSummary summary = AnalyticsAggregator.Summarize(alignedFrom, to, bucket, analyses, calls);
            summary.MediaKind = filter.MediaKind;
            summary.Endpoint = String.IsNullOrWhiteSpace(filter.Endpoint) ? null : filter.Endpoint.Trim();
            summary.Model = String.IsNullOrWhiteSpace(filter.Model) ? null : filter.Model.Trim();
            return summary;
        }

        /// <inheritdoc />
        public async Task<bool> DeleteAsync(string id, CancellationToken token = default)
        {
            if (String.IsNullOrWhiteSpace(id)) throw new ArgumentNullException(nameof(id));

            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@id", id } };
            object? exists = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM analyses WHERE id = @id", p, token).ConfigureAwait(false);
            if (exists == null || Convert.ToInt64(exists) < 1) return false;

            List<DbCommandSpec> specs = new List<DbCommandSpec>
            {
                new DbCommandSpec("DELETE FROM modelcalls WHERE analysisid = @id").Add("@id", id),
                new DbCommandSpec("DELETE FROM analyses WHERE id = @id").Add("@id", id)
            };

            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
            return true;
        }

        /// <inheritdoc />
        public async Task<int> PruneAsync(DateTime olderThanUtc, CancellationToken token = default)
        {
            object cutoff = _Driver.ToDbDate(olderThanUtc);
            Dictionary<string, object?> p = new Dictionary<string, object?> { { "@cutoff", cutoff } };
            object? countObj = await _Driver.ExecuteScalarAsync("SELECT COUNT(*) FROM analyses WHERE completedutc < @cutoff", p, token).ConfigureAwait(false);
            int count = countObj == null ? 0 : Convert.ToInt32(countObj);
            if (count < 1) return 0;

            List<DbCommandSpec> specs = new List<DbCommandSpec>
            {
                new DbCommandSpec("DELETE FROM modelcalls WHERE analysisid IN (SELECT id FROM analyses WHERE completedutc < @cutoff)").Add("@cutoff", cutoff),
                new DbCommandSpec("DELETE FROM analyses WHERE completedutc < @cutoff").Add("@cutoff", cutoff)
            };

            await _Driver.ExecuteTransactionAsync(specs, token).ConfigureAwait(false);
            return count;
        }

        #endregion

        #region Private-Methods

        private static AnalysisRecord AnalysisFromRow(DataRow row)
        {
            AnalysisRecord record = new AnalysisRecord
            {
                Id = RowReader.GetString(row, "id") ?? String.Empty,
                RequestId = RowReader.GetString(row, "requestid"),
                JobId = RowReader.GetString(row, "jobid"),
                MediaKind = RowReader.GetEnum(row, "mediakind", MediaKindEnum.Unknown),
                PrincipalName = RowReader.GetString(row, "principalname"),
                Success = RowReader.GetBoolean(row, "success"),
                Error = RowReader.GetString(row, "error"),
                StartedUtc = RowReader.GetDate(row, "startedutc") ?? DateTime.UtcNow,
                CompletedUtc = RowReader.GetDate(row, "completedutc") ?? DateTime.UtcNow,
                AnalysisMs = RowReader.GetDouble(row, "analysisms"),
                QueueWaitMs = RowReader.GetDouble(row, "queuewaitms"),
                MediaDurationMs = RowReader.GetNullableInt64(row, "mediadurationms"),
                MediaBytes = RowReader.GetInt64(row, "mediabytes"),
                RealtimeFactor = RowReader.GetNullableDouble(row, "realtimefactor"),
                VisionEndpoint = RowReader.GetString(row, "visionendpoint"),
                VisionModel = RowReader.GetString(row, "visionmodel"),
                TextEndpoint = RowReader.GetString(row, "textendpoint"),
                TextModel = RowReader.GetString(row, "textmodel"),
                TranscriptionEndpoint = RowReader.GetString(row, "transcriptionendpoint"),
                TranscriptionModel = RowReader.GetString(row, "transcriptionmodel"),
                ModelCalls = (int)RowReader.GetInt64(row, "modelcalls"),
                FailedModelCalls = (int)RowReader.GetInt64(row, "failedmodelcalls"),
                Retries = (int)RowReader.GetInt64(row, "retries"),
                RepairAttempts = (int)RowReader.GetInt64(row, "repairattempts"),
                PromptTokens = RowReader.GetInt64(row, "prompttokens"),
                CompletionTokens = RowReader.GetInt64(row, "completiontokens"),
                TotalTokens = RowReader.GetInt64(row, "totaltokens"),
                CachedTokens = RowReader.GetInt64(row, "cachedtokens"),
                ReasoningTokens = RowReader.GetInt64(row, "reasoningtokens"),
                InputImages = (int)RowReader.GetInt64(row, "inputimages"),
                InputImageBytes = RowReader.GetInt64(row, "inputimagebytes"),
                InputAudioBytes = RowReader.GetInt64(row, "inputaudiobytes"),
                Scenes = (int)RowReader.GetInt64(row, "scenes"),
                TranscriptCharacters = RowReader.GetInt64(row, "transcriptcharacters"),
                FieldCount = (int)RowReader.GetInt64(row, "fieldcount"),
                InvalidFields = (int)RowReader.GetInt64(row, "invalidfields")
            };

            string? stages = RowReader.GetString(row, "stagesjson");
            if (!String.IsNullOrWhiteSpace(stages))
            {
                try
                {
                    record.Stages = KeyframeSerializer.Deserialize<List<StageTiming>>(stages) ?? new List<StageTiming>();
                }
                catch (System.Text.Json.JsonException)
                {
                    record.Stages = new List<StageTiming>();
                }
            }

            return record;
        }

        private static ModelCallRecord CallFromRow(DataRow row)
        {
            long? status = RowReader.GetNullableInt64(row, "statuscode");
            return new ModelCallRecord
            {
                Id = RowReader.GetString(row, "id") ?? String.Empty,
                AnalysisId = RowReader.GetString(row, "analysisid") ?? String.Empty,
                Purpose = RowReader.GetEnum(row, "purpose", ModelCallPurposeEnum.Summarize),
                Target = RowReader.GetString(row, "target"),
                Endpoint = RowReader.GetString(row, "endpoint") ?? String.Empty,
                Provider = RowReader.GetEnum(row, "provider", ProviderTypeEnum.Mock),
                Model = RowReader.GetString(row, "model"),
                Attempt = (int)RowReader.GetInt64(row, "attempt"),
                StartUtc = RowReader.GetDate(row, "startutc") ?? DateTime.UtcNow,
                Success = RowReader.GetBoolean(row, "success"),
                Error = RowReader.GetString(row, "error"),
                StatusCode = status.HasValue ? (int)status.Value : null,
                DurationMs = RowReader.GetDouble(row, "durationms"),
                QueueMs = RowReader.GetDouble(row, "queuems"),
                TimeToFirstTokenMs = RowReader.GetNullableDouble(row, "ttftms"),
                GenerationMs = RowReader.GetNullableDouble(row, "generationms"),
                TokensPerSecond = RowReader.GetNullableDouble(row, "tokenspersecond"),
                PromptTokens = RowReader.GetNullableInt64(row, "prompttokens"),
                CompletionTokens = RowReader.GetNullableInt64(row, "completiontokens"),
                TotalTokens = RowReader.GetNullableInt64(row, "totaltokens"),
                CachedTokens = RowReader.GetNullableInt64(row, "cachedtokens"),
                ReasoningTokens = RowReader.GetNullableInt64(row, "reasoningtokens"),
                InputImages = (int)RowReader.GetInt64(row, "inputimages"),
                InputImageBytes = RowReader.GetInt64(row, "inputimagebytes"),
                InputAudioBytes = RowReader.GetInt64(row, "inputaudiobytes"),
                FinishReason = RowReader.GetString(row, "finishreason")
            };
        }

        #endregion
    }
}
