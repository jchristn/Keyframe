namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Database;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Helpers;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Touchstone.Core;

    /// <summary>
    /// Shared database contract tests, run once per enabled provider.
    /// </summary>
    public static class DatabaseSuite
    {
        /// <summary>
        /// Build one suite per enabled provider.
        /// </summary>
        /// <returns>Suites.</returns>
        public static IEnumerable<TestSuiteDescriptor> Create()
        {
            foreach (DatabaseTypeEnum type in DatabaseTargets.Enabled())
            {
                yield return CreateFor(type);
            }
        }

        private static TestSuiteDescriptor CreateFor(DatabaseTypeEnum type)
        {
            string id = "Database-" + type;
            return new TestSuiteDescriptor(id, "Database (" + type + ")", new[]
            {
                new TestCaseDescriptor(id, "Migrations", "First boot applies migrations; a second boot is a no-op", async ct =>
                {
                    DatabaseSettings settings = DatabaseTargets.Settings(type);
                    using (DatabaseDriverBase first = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, ct).ConfigureAwait(false))
                    {
                        Check.Equal(2, first.SchemaVersion, "schema version after first boot");
                        object? tables = await first.ExecuteScalarAsync("SELECT COUNT(*) FROM analyses", null, ct).ConfigureAwait(false);
                        Check.Equal(0L, Convert.ToInt64(tables), "analyses table exists and is empty");
                        object? calls = await first.ExecuteScalarAsync("SELECT COUNT(*) FROM modelcalls", null, ct).ConfigureAwait(false);
                        Check.Equal(0L, Convert.ToInt64(calls), "modelcalls table exists and is empty");
                    }

                    using (DatabaseDriverBase second = await DatabaseDriverFactory.CreateAndInitializeAsync(settings, ct).ConfigureAwait(false))
                    {
                        Check.Equal(2, second.SchemaVersion, "schema version after second boot");
                        object? count = await second.ExecuteScalarAsync("SELECT COUNT(*) FROM schema_migrations", null, ct).ConfigureAwait(false);
                        Check.Equal(2L, Convert.ToInt64(count), "two migration rows");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "RequestHistoryCrud", "Create, read with headers, list without bodies, delete", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        RequestHistoryEntry entry = Entry("POST", "/v1.0/image", 200, DateTime.UtcNow, "default");
                        entry.RequestHeaders!["Content-Type"] = "application/json";
                        entry.RequestHeaders["Authorization"] = "[redacted]";
                        entry.ResponseHeaders!["x-request-id"] = "rqi_test";
                        entry.RequestBody = "{\"Image\":{}}";
                        entry.ResponseBody = "{\"Success\":true}";
                        await db.RequestHistory.CreateAsync(entry, ct).ConfigureAwait(false);

                        RequestHistoryEntry? read = await db.RequestHistory.ReadAsync(entry.Id, ct).ConfigureAwait(false);
                        Check.True(read != null, "entry read back");
                        Check.Equal("POST", read!.Method, "method");
                        Check.Equal(2, read.RequestHeaders!.Count, "request headers");
                        Check.Equal("[redacted]", read.RequestHeaders["authorization"], "header lookup is case-insensitive");
                        Check.Equal("rqi_test", read.ResponseHeaders!["x-request-id"], "response header");
                        Check.Equal("{\"Success\":true}", read.ResponseBody, "response body");
                        Check.True(Math.Abs((read.CreatedUtc - entry.CreatedUtc).TotalMilliseconds) < 5, "timestamp round trip");

                        EnumerationResult<RequestHistoryEntry> page = await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter(), ct).ConfigureAwait(false);
                        Check.Equal(1L, page.TotalCount, "total");
                        Check.True(page.Items[0].RequestBody == null && page.Items[0].RequestHeaders == null, "list omits bodies and headers");

                        Check.True(await db.RequestHistory.DeleteAsync(entry.Id, ct).ConfigureAwait(false), "deleted");
                        Check.True(!await db.RequestHistory.DeleteAsync(entry.Id, ct).ConfigureAwait(false), "second delete reports missing");
                        Check.True(await db.RequestHistory.ReadAsync(entry.Id, ct).ConfigureAwait(false) == null, "gone");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "RequestHistoryFilterPaging", "Filters and paging, newest first", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        DateTime start = DateTime.UtcNow.AddMinutes(-30);
                        for (int i = 0; i < 12; i++)
                        {
                            string method = i % 3 == 0 ? "GET" : "POST";
                            int status = i % 4 == 0 ? 500 : 200;
                            string path = i % 2 == 0 ? "/v1.0/image" : "/v1.0/jobs/{id}";
                            await db.RequestHistory.CreateAsync(Entry(method, path, status, start.AddMinutes(i), i < 6 ? "alpha" : "beta"), ct).ConfigureAwait(false);
                        }

                        EnumerationResult<RequestHistoryEntry> page1 = await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PageSize = 5 }, ct).ConfigureAwait(false);
                        Check.Equal(12L, page1.TotalCount, "total");
                        Check.Equal(3, page1.TotalPages, "pages");
                        Check.Equal(5, page1.Items.Count, "page size");
                        Check.True(page1.Items[0].CreatedUtc > page1.Items[4].CreatedUtc, "newest first");

                        EnumerationResult<RequestHistoryEntry> page3 = await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PageSize = 5, PageNumber = 3 }, ct).ConfigureAwait(false);
                        Check.Equal(2, page3.Items.Count, "last page");

                        Check.Equal(4L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { Method = "get" }, ct).ConfigureAwait(false)).TotalCount, "method filter");
                        Check.Equal(3L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { StatusCode = 500 }, ct).ConfigureAwait(false)).TotalCount, "status filter");
                        Check.Equal(6L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PathContains = "{id}" }, ct).ConfigureAwait(false)).TotalCount, "path filter");
                        Check.Equal(6L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PrincipalName = "beta" }, ct).ConfigureAwait(false)).TotalCount, "principal filter");
                        Check.Equal(0L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { PathContains = "100%_" }, ct).ConfigureAwait(false)).TotalCount, "LIKE wildcards are escaped");
                        Check.Equal(5L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter { FromUtc = start.AddMinutes(7) }, ct).ConfigureAwait(false)).TotalCount, "from filter");

                        int deleted = await db.RequestHistory.DeleteManyAsync(new RequestHistoryFilter { PrincipalName = "alpha" }, ct).ConfigureAwait(false);
                        Check.Equal(6, deleted, "bulk delete count");
                        Check.Equal(6L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter(), ct).ConfigureAwait(false)).TotalCount, "remaining");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "RequestHistorySummary", "Summary fills every bucket, including empty ones", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        DateTime from = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
                        await db.RequestHistory.CreateAsync(Entry("GET", "/a", 200, from.AddMinutes(5), null, 10), ct).ConfigureAwait(false);
                        await db.RequestHistory.CreateAsync(Entry("GET", "/a", 404, from.AddMinutes(10), null, 30), ct).ConfigureAwait(false);
                        await db.RequestHistory.CreateAsync(Entry("GET", "/a", 200, from.AddMinutes(50), null, 20), ct).ConfigureAwait(false);

                        RequestHistorySummary summary = await db.RequestHistory.SummarizeAsync(new RequestHistoryFilter
                        {
                            FromUtc = from,
                            ToUtc = from.AddHours(1),
                            BucketMinutes = 15
                        }, ct).ConfigureAwait(false);

                        Check.Equal(4, summary.Buckets.Count, "four 15 minute buckets");
                        Check.Equal(3L, summary.TotalCount, "total");
                        Check.Equal(1L, summary.TotalFailure, "failures");
                        Check.Equal(1L, summary.Buckets[0].SuccessCount, "bucket 0 success");
                        Check.Equal(1L, summary.Buckets[0].FailureCount, "bucket 0 failure");
                        Check.Equal(20.0, summary.Buckets[0].AverageDurationMs, "bucket 0 average");
                        Check.Equal(0L, summary.Buckets[1].SuccessCount + summary.Buckets[1].FailureCount, "empty bucket present");
                        Check.Equal(1L, summary.Buckets[3].SuccessCount, "bucket 3");
                        Check.Equal(20.0, summary.AverageDurationMs, "overall average");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "RequestHistoryPrune", "Prune removes rows older than the cutoff", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        RequestHistoryEntry old = Entry("GET", "/old", 200, DateTime.UtcNow.AddDays(-40), null);
                        old.RequestHeaders!["Accept"] = "*/*";
                        await db.RequestHistory.CreateAsync(old, ct).ConfigureAwait(false);
                        await db.RequestHistory.CreateAsync(Entry("GET", "/new", 200, DateTime.UtcNow, null), ct).ConfigureAwait(false);

                        int pruned = await db.RequestHistory.PruneAsync(DateTime.UtcNow.AddDays(-30), ct).ConfigureAwait(false);
                        Check.Equal(1, pruned, "pruned");
                        object? headers = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM requesthistoryheaders", null, ct).ConfigureAwait(false);
                        Check.Equal(0L, Convert.ToInt64(headers), "child headers pruned");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "JobsLifecycle", "Create, update, result storage, enumerate, delete", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        Job job = new Job { PrincipalName = "default", SourceUrl = "https://example.com/a.mp4" };
                        await db.Jobs.CreateAsync(job, ct).ConfigureAwait(false);

                        Job? read = await db.Jobs.ReadAsync(job.Id, ct).ConfigureAwait(false);
                        Check.True(read != null && read.Status == JobStatusEnum.Queued, "queued");
                        Check.Equal(1L, await db.Jobs.CountByStatusAsync(JobStatusEnum.Queued, ct).ConfigureAwait(false), "queued count");

                        read!.Status = JobStatusEnum.Running;
                        read.StartedUtc = DateTime.UtcNow;
                        read.ProgressStage = "Scenes";
                        read.ProgressCompleted = 3;
                        read.ProgressTotal = 10;
                        read.ProgressPercent = 42.5;
                        await db.Jobs.UpdateAsync(read, ct).ConfigureAwait(false);

                        Job? running = await db.Jobs.ReadAsync(job.Id, ct).ConfigureAwait(false);
                        Check.Equal(JobStatusEnum.Running, running!.Status, "running");
                        Check.Equal(42.5, running.ProgressPercent, "progress");
                        Check.True(running.StartedUtc != null, "started");

                        await db.Jobs.StoreResultAsync(new JobResult { JobId = job.Id, ResultJson = "{\"Success\":true}" }, ct).ConfigureAwait(false);
                        await db.Jobs.StoreResultAsync(new JobResult { JobId = job.Id, ResultJson = "{\"Success\":true,\"Replaced\":1}" }, ct).ConfigureAwait(false);
                        JobResult? result = await db.Jobs.ReadResultAsync(job.Id, ct).ConfigureAwait(false);
                        Check.True(result != null && result.ResultJson.Contains("Replaced"), "result replaced");
                        Check.True(result!.ResultBytes > 10, "result bytes recorded");

                        Job second = new Job { PrincipalName = "other", MediaKind = MediaKindEnum.Video };
                        await db.Jobs.CreateAsync(second, ct).ConfigureAwait(false);
                        EnumerationResult<Job> all = await db.Jobs.EnumerateAsync(new JobFilter(), ct).ConfigureAwait(false);
                        Check.Equal(2L, all.TotalCount, "two jobs");
                        EnumerationResult<Job> runningOnly = await db.Jobs.EnumerateAsync(new JobFilter { Status = JobStatusEnum.Running }, ct).ConfigureAwait(false);
                        Check.Equal(1L, runningOnly.TotalCount, "status filter");

                        Check.True(await db.Jobs.DeleteAsync(job.Id, ct).ConfigureAwait(false), "deleted");
                        Check.True(await db.Jobs.ReadResultAsync(job.Id, ct).ConfigureAwait(false) == null, "result deleted with job");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "JobsInterruptAndPrune", "Startup interruption and retention pruning", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        Job queued = await db.Jobs.CreateAsync(new Job(), ct).ConfigureAwait(false);
                        Job running = await db.Jobs.CreateAsync(new Job { Status = JobStatusEnum.Running }, ct).ConfigureAwait(false);
                        Job oldDone = await db.Jobs.CreateAsync(new Job { Status = JobStatusEnum.Succeeded, CreatedUtc = DateTime.UtcNow.AddDays(-10) }, ct).ConfigureAwait(false);
                        await db.Jobs.StoreResultAsync(new JobResult { JobId = oldDone.Id, ResultJson = "{}" }, ct).ConfigureAwait(false);

                        int interrupted = await db.Jobs.MarkInterruptedAsync("Server restarted.", ct).ConfigureAwait(false);
                        Check.Equal(2, interrupted, "interrupted count");
                        Job? q = await db.Jobs.ReadAsync(queued.Id, ct).ConfigureAwait(false);
                        Check.True(q!.Status == JobStatusEnum.Interrupted && q.Error == "Server restarted." && q.CompletedUtc != null, "queued job interrupted");

                        int pruned = await db.Jobs.PruneAsync(DateTime.UtcNow.AddDays(-7), ct).ConfigureAwait(false);
                        Check.Equal(1, pruned, "old terminal job pruned");
                        Check.True(await db.Jobs.ReadResultAsync(oldDone.Id, ct).ConfigureAwait(false) == null, "pruned result removed");
                        Check.True(await db.Jobs.ReadAsync(running.Id, ct).ConfigureAwait(false) != null, "recent job kept");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "ConcurrentWrites", "Parallel inserts all land", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        List<Task> tasks = new List<Task>();
                        for (int i = 0; i < 40; i++)
                        {
                            RequestHistoryEntry e = Entry("GET", "/c", 200, DateTime.UtcNow, "p" + (i % 4));
                            e.RequestHeaders!["Accept"] = "application/json";
                            tasks.Add(db.RequestHistory.CreateAsync(e, ct));
                        }

                        await Task.WhenAll(tasks).ConfigureAwait(false);
                        Check.Equal(40L, (await db.RequestHistory.EnumerateAsync(new RequestHistoryFilter(), ct).ConfigureAwait(false)).TotalCount, "all rows present");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "AnalyticsCrud", "Analyses with model call attempts: create, read, list with filters and paging, delete", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        DateTime start = DateTime.UtcNow.AddMinutes(-20);
                        List<AnalysisRecord> created = new List<AnalysisRecord>();
                        for (int i = 0; i < 7; i++)
                        {
                            MediaKindEnum kind = i % 3 == 0 ? MediaKindEnum.Audio : (i % 3 == 1 ? MediaKindEnum.Image : MediaKindEnum.Video);
                            AnalysisRecord record = Analysis(kind, start.AddMinutes(i), i != 4, 100 + i);
                            record.Calls = new List<ModelCallRecord> { Attempt(record.Id, 1, false, 50, null), Attempt(record.Id, 2, true, 70, 20.5) };
                            await db.Analytics.CreateAsync(record, ct).ConfigureAwait(false);
                            created.Add(record);
                        }

                        AnalysisRecord first = created[0];
                        AnalysisRecord? read = await db.Analytics.ReadAsync(first.Id, ct).ConfigureAwait(false);
                        Check.True(read != null, "read back");
                        Check.Equal(MediaKindEnum.Audio, read!.MediaKind, "kind");
                        Check.Equal("kfj_job", read.JobId, "job id");
                        Check.Equal(25000L, read.MediaDurationMs, "nullable long");
                        Check.Equal(0.004, read.RealtimeFactor, "nullable double");
                        Check.Equal(2, read.Stages.Count, "stages JSON");
                        Check.Equal("Transcribe", read.Stages[1].Name, "stage order");
                        Check.Equal("whisper", read.TranscriptionEndpoint, "endpoint column");
                        Check.Equal(3, read.Retries, "counts");
                        Check.True(Math.Abs((read.CompletedUtc - first.CompletedUtc).TotalMilliseconds) < 5, "timestamp round trip");
                        Check.Equal(2, read.Calls!.Count, "calls");
                        Check.True(read.Calls[0].Attempt == 1 && !read.Calls[0].Success && read.Calls[0].TimeToFirstTokenMs == null, "first attempt with null timing");
                        Check.True(read.Calls[1].Success && read.Calls[1].TimeToFirstTokenMs == 20.5 && read.Calls[1].StatusCode == 200, "second attempt");
                        Check.Equal(ModelCallPurposeEnum.Summarize, read.Calls[1].Purpose, "purpose enum");
                        Check.Equal(ProviderTypeEnum.Ollama, read.Calls[1].Provider, "provider enum");
                        Check.Equal(null, read.Calls[0].PromptTokens, "null tokens stay null");

                        EnumerationResult<AnalysisRecord> all = await db.Analytics.EnumerateAsync(new AnalyticsFilter(), ct).ConfigureAwait(false);
                        Check.Equal(7L, all.TotalCount, "total");
                        Check.Equal(created[6].Id, all.Items[0].Id, "newest first");
                        Check.True(all.Items.All(r => r.Calls == null), "list omits calls");

                        EnumerationResult<AnalysisRecord> audio = await db.Analytics.EnumerateAsync(new AnalyticsFilter { MediaKind = MediaKindEnum.Audio }, ct).ConfigureAwait(false);
                        Check.Equal(3L, audio.TotalCount, "media kind filter");
                        EnumerationResult<AnalysisRecord> failed = await db.Analytics.EnumerateAsync(new AnalyticsFilter { Success = false }, ct).ConfigureAwait(false);
                        Check.Equal(1L, failed.TotalCount, "success filter");
                        EnumerationResult<AnalysisRecord> page = await db.Analytics.EnumerateAsync(new AnalyticsFilter { Skip = 2, MaxResults = 2 }, ct).ConfigureAwait(false);
                        Check.Equal(2, page.Items.Count, "page size");
                        Check.Equal(created[4].Id, page.Items[0].Id, "skip");
                        Check.Equal(2, page.PageNumber, "page number from skip");
                        Check.Equal(4, page.TotalPages, "total pages");
                        EnumerationResult<AnalysisRecord> window = await db.Analytics.EnumerateAsync(new AnalyticsFilter { FromUtc = start.AddMinutes(2), ToUtc = start.AddMinutes(4) }, ct).ConfigureAwait(false);
                        Check.Equal(2L, window.TotalCount, "time window");

                        Check.True(await db.Analytics.DeleteAsync(first.Id, ct).ConfigureAwait(false), "deleted");
                        Check.True(!await db.Analytics.DeleteAsync(first.Id, ct).ConfigureAwait(false), "second delete reports missing");
                        Check.True(await db.Analytics.ReadAsync(first.Id, ct).ConfigureAwait(false) == null, "gone");
                        object? orphans = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM modelcalls WHERE analysisid = @id", new Dictionary<string, object?> { { "@id", first.Id } }, ct).ConfigureAwait(false);
                        Check.Equal(0L, Convert.ToInt64(orphans), "calls deleted with the analysis");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "AnalyticsSummary", "Stored rows summarize with percentiles, gap fill, and endpoint and media filters", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        DateTime hour = new DateTime(2026, 1, 5, 9, 0, 0, DateTimeKind.Utc);
                        double[] durations = new double[] { 100, 200, 300, 400, 1000 };
                        for (int i = 0; i < durations.Length; i++)
                        {
                            AnalysisRecord record = Analysis(MediaKindEnum.Image, hour.AddMinutes(10 + i), true, durations[i]);
                            record.VisionEndpoint = i < 3 ? "ollama-gemma3-4b" : "ollama-qwen3-vl-8b";
                            record.Calls = new List<ModelCallRecord> { Attempt(record.Id, 1, true, durations[i] / 2, durations[i] / 10) };
                            record.Calls[0].Endpoint = record.VisionEndpoint;
                            await db.Analytics.CreateAsync(record, ct).ConfigureAwait(false);
                        }

                        AnalysisRecord late = Analysis(MediaKindEnum.Audio, hour.AddHours(2).AddMinutes(5), false, 5000);
                        late.Calls = new List<ModelCallRecord>();
                        await db.Analytics.CreateAsync(late, ct).ConfigureAwait(false);

                        AnalyticsSummary summary = await db.Analytics.SummarizeAsync(new AnalyticsSummaryFilter { FromUtc = hour, ToUtc = hour.AddHours(3), Bucket = AnalyticsBucketEnum.Hour }, ct).ConfigureAwait(false);
                        Check.Equal(3, summary.Buckets.Count, "three buckets");
                        Check.Equal(5L, summary.Buckets[0].Stats.Count, "first hour");
                        Check.Equal(0L, summary.Buckets[1].Stats.Count, "gap hour present");
                        Check.Equal(1L, summary.Buckets[2].Stats.Count, "third hour");
                        Check.Equal(6L, summary.Totals.Count, "total");
                        Distribution image = summary.ByMediaKind["Image"].AnalysisMs!;
                        Check.Equal(300.0, image.P50, "median");
                        Check.Equal(760.0, image.P90, "p90 interpolated");
                        Check.Equal(400.0, image.Avg, "average");
                        Check.Equal(5L, summary.ByMediaKind["Image"].TimeToFirstTokenMs!.Count, "ttft samples per call");
                        Check.Equal(2, summary.ByModel.Count, "two models");
                        Check.Equal("ollama-gemma3-4b", summary.ByModel[0].Endpoint, "most calls first");

                        AnalyticsSummary filtered = await db.Analytics.SummarizeAsync(new AnalyticsSummaryFilter { FromUtc = hour, ToUtc = hour.AddHours(3), Endpoint = "ollama-qwen3-vl-8b" }, ct).ConfigureAwait(false);
                        Check.Equal(2L, filtered.Totals.Count, "endpoint filter on analyses");
                        Check.Equal(1, filtered.ByModel.Count, "endpoint filter on calls");
                        Check.Equal("ollama-qwen3-vl-8b", filtered.Endpoint, "filter echoed");
                        Check.Equal("hour", filtered.Bucket, "a 3 hour range defaults to hourly buckets");
                    }
                }, new[] { "database" }),

                new TestCaseDescriptor(id, "AnalyticsPrune", "Prune removes analyses and their calls older than the cutoff", async ct =>
                {
                    using (DatabaseDriverBase db = await OpenAsync(type, ct).ConfigureAwait(false))
                    {
                        AnalysisRecord old = Analysis(MediaKindEnum.Video, DateTime.UtcNow.AddDays(-100), true, 10);
                        old.Calls = new List<ModelCallRecord> { Attempt(old.Id, 1, true, 5, 1) };
                        AnalysisRecord fresh = Analysis(MediaKindEnum.Video, DateTime.UtcNow.AddMinutes(-1), true, 10);
                        fresh.Calls = new List<ModelCallRecord> { Attempt(fresh.Id, 1, true, 5, 1) };
                        await db.Analytics.CreateAsync(old, ct).ConfigureAwait(false);
                        await db.Analytics.CreateAsync(fresh, ct).ConfigureAwait(false);

                        Check.Equal(1, await db.Analytics.PruneAsync(DateTime.UtcNow.AddDays(-90), ct).ConfigureAwait(false), "one pruned");
                        Check.Equal(0, await db.Analytics.PruneAsync(DateTime.UtcNow.AddDays(-90), ct).ConfigureAwait(false), "idempotent");
                        object? calls = await db.ExecuteScalarAsync("SELECT COUNT(*) FROM modelcalls", null, ct).ConfigureAwait(false);
                        Check.Equal(1L, Convert.ToInt64(calls), "pruned analysis calls removed");
                        Check.True(await db.Analytics.ReadAsync(fresh.Id, ct).ConfigureAwait(false) != null, "fresh kept");
                    }
                }, new[] { "database" })
            });
        }

        private static async Task<DatabaseDriverBase> OpenAsync(DatabaseTypeEnum type, CancellationToken token)
        {
            return await DatabaseDriverFactory.CreateAndInitializeAsync(DatabaseTargets.Settings(type), token).ConfigureAwait(false);
        }

        private static AnalysisRecord Analysis(MediaKindEnum kind, DateTime completed, bool success, double ms)
        {
            return new AnalysisRecord
            {
                Id = IdGenerator.GenerateAnalysisId(),
                RequestId = "rqi_test",
                JobId = kind == MediaKindEnum.Image ? null : "kfj_job",
                MediaKind = kind,
                PrincipalName = "tester",
                Success = success,
                Error = success ? null : "failed",
                StartedUtc = completed.AddMilliseconds(-ms),
                CompletedUtc = completed,
                AnalysisMs = ms,
                QueueWaitMs = kind == MediaKindEnum.Image ? 0 : 12.5,
                MediaDurationMs = kind == MediaKindEnum.Image ? null : 25000,
                MediaBytes = 4096,
                RealtimeFactor = kind == MediaKindEnum.Image ? null : Math.Round(ms / 25000.0, 4),
                VisionEndpoint = kind == MediaKindEnum.Audio ? null : "ollama-gemma3-4b",
                VisionModel = kind == MediaKindEnum.Audio ? null : "gemma3:4b",
                TextEndpoint = "ollama-gpt-oss-20b",
                TextModel = "gpt-oss:20b",
                TranscriptionEndpoint = kind == MediaKindEnum.Image ? null : "whisper",
                TranscriptionModel = kind == MediaKindEnum.Image ? null : "Systran/faster-whisper-small",
                ModelCalls = 2,
                FailedModelCalls = success ? 0 : 1,
                Retries = 3,
                RepairAttempts = 1,
                PromptTokens = 100,
                CompletionTokens = 20,
                TotalTokens = 120,
                InputAudioBytes = kind == MediaKindEnum.Image ? 0 : 2048,
                TranscriptCharacters = kind == MediaKindEnum.Image ? 0 : 300,
                Stages = new List<StageTiming> { new StageTiming { Name = "Resolve", DurationMs = 1.5 }, new StageTiming { Name = "Transcribe", DurationMs = 40 } }
            };
        }

        private static ModelCallRecord Attempt(string analysisId, int attempt, bool success, double ms, double? ttft)
        {
            return new ModelCallRecord
            {
                Id = IdGenerator.GenerateModelCallId(),
                AnalysisId = analysisId,
                Purpose = ModelCallPurposeEnum.Summarize,
                Endpoint = "ollama-gpt-oss-20b",
                Provider = ProviderTypeEnum.Ollama,
                Model = "gpt-oss:20b",
                Attempt = attempt,
                StartUtc = DateTime.UtcNow.AddSeconds(attempt),
                Success = success,
                Error = success ? null : "transient",
                StatusCode = success ? 200 : 503,
                DurationMs = ms,
                QueueMs = 1.25,
                TimeToFirstTokenMs = ttft,
                GenerationMs = ttft.HasValue ? ms - ttft.Value : null,
                TokensPerSecond = ttft.HasValue ? 42.5 : null,
                PromptTokens = success ? 100 : null,
                CompletionTokens = success ? 20 : null,
                TotalTokens = success ? 120 : null,
                FinishReason = success ? "stop" : null
            };
        }

        private static RequestHistoryEntry Entry(string method, string path, int status, DateTime created, string? principal, double durationMs = 12.5)
        {
            return new RequestHistoryEntry
            {
                Method = method,
                Path = path,
                Url = "http://127.0.0.1:8700" + path,
                StatusCode = status,
                DurationMs = durationMs,
                PrincipalName = principal,
                SourceIp = "127.0.0.1",
                CreatedUtc = created,
                CompletedUtc = created.AddMilliseconds(durationMs)
            };
        }
    }
}
