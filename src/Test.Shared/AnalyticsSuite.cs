namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Analytics;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Models;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Analytics math and record building without a database: percentiles and distributions, bucket choice and
    /// gap fill, breakdowns by media kind, model, stage, and purpose, and the per-attempt rows built from a real
    /// mock analysis with retries and repairs.
    /// </summary>
    public static class AnalyticsSuite
    {
        private const string Id = "Analytics";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Analytics", new[]
            {
                new TestCaseDescriptor(Id, "Distribution", "Percentiles interpolate between ranks; empty input gives null", ct =>
                {
                    Distribution d = AnalyticsAggregator.Distribute(Enumerable.Range(1, 10).Select(i => (double)i))!;
                    Check.Equal(10L, d.Count, "count");
                    Check.Equal(1.0, d.Min, "min");
                    Check.Equal(10.0, d.Max, "max");
                    Check.Equal(55.0, d.Sum, "sum");
                    Check.Equal(5.5, d.Avg, "avg");
                    Check.Equal(5.5, d.P50, "p50");
                    Check.Equal(9.1, d.P90, "p90");
                    Check.Equal(9.55, d.P95, "p95");
                    Check.Equal(9.91, d.P99, "p99");
                    Check.Equal(2.872, d.StdDev, "population standard deviation");

                    Distribution one = AnalyticsAggregator.Distribute(new[] { 42.0 })!;
                    Check.True(one.P50 == 42 && one.P99 == 42 && one.StdDev == 0, "single sample");
                    Check.True(AnalyticsAggregator.Distribute(new double[0]) == null, "no samples is null");
                    Check.True(AnalyticsAggregator.Distribute(new[] { Double.NaN }) == null, "NaN ignored");
                    Check.Equal(25.0, AnalyticsAggregator.Percentile(new List<double> { 0, 100 }, 25), "two-point interpolation");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Buckets", "Bucket choice by range, alignment, and every bucket present", ct =>
                {
                    Check.Equal(AnalyticsBucketEnum.Minute, AnalyticsAggregator.ChooseBucket(TimeSpan.FromHours(2)), "2 hours is minute");
                    Check.Equal(AnalyticsBucketEnum.Hour, AnalyticsAggregator.ChooseBucket(TimeSpan.FromHours(2.01)), "just over 2 hours is hour");
                    Check.Equal(AnalyticsBucketEnum.Hour, AnalyticsAggregator.ChooseBucket(TimeSpan.FromDays(7)), "7 days is hour");
                    Check.Equal(AnalyticsBucketEnum.Day, AnalyticsAggregator.ChooseBucket(TimeSpan.FromDays(8)), "8 days is day");

                    DateTime from = new DateTime(2026, 11, 15, 10, 20, 0, DateTimeKind.Utc);
                    DateTime to = new DateTime(2026, 11, 15, 13, 0, 0, DateTimeKind.Utc);
                    List<AnalysisRecord> analyses = new List<AnalysisRecord>
                    {
                        Record("a1", MediaKindEnum.Image, new DateTime(2026, 11, 15, 10, 15, 0, DateTimeKind.Utc), true, 100),
                        Record("a2", MediaKindEnum.Video, new DateTime(2026, 11, 15, 12, 30, 0, DateTimeKind.Utc), false, 300),
                        Record("out", MediaKindEnum.Image, new DateTime(2026, 11, 15, 13, 0, 0, DateTimeKind.Utc), true, 999)
                    };

                    AnalyticsSummary summary = AnalyticsAggregator.Summarize(from, to, AnalyticsBucketEnum.Hour, analyses, new List<ModelCallRecord>());
                    Check.Equal(new DateTime(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc), summary.FromUtc, "from aligned down to the hour");
                    Check.Equal("hour", summary.Bucket, "bucket name");
                    Check.Equal(3, summary.Buckets.Count, "three hourly buckets");
                    Check.Equal(1L, summary.Buckets[0].Stats.Count, "first bucket");
                    Check.Equal(0L, summary.Buckets[1].Stats.Count, "gap bucket is present and empty");
                    Check.True(summary.Buckets[1].Stats.AnalysisMs == null && summary.Buckets[1].Stats.SuccessRate == 0, "empty bucket has null distributions");
                    Check.Equal(1L, summary.Buckets[2].Stats.Failed, "last bucket");
                    Check.Equal(2L, summary.Totals.Count, "the row at the exclusive end is excluded");
                    Check.Equal(0.5, summary.Totals.SuccessRate, "success rate");
                    Check.Equal(200.0, summary.Totals.AnalysisMs!.Avg, "average");
                    Check.True(summary.ByMediaKind.ContainsKey("Image") && summary.ByMediaKind.ContainsKey("Video") && summary.ByMediaKind.ContainsKey("Audio"), "all media kinds present");
                    Check.Equal(0L, summary.ByMediaKind["Audio"].Count, "audio empty");

                    Check.Throws<ArgumentOutOfRangeException>(() => AnalyticsAggregator.Summarize(from, from.AddDays(30), AnalyticsBucketEnum.Minute, analyses, new List<ModelCallRecord>()), "too many buckets");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Breakdowns", "Model, stage, purpose, realtime factor, and model-call distributions", ct =>
                {
                    DateTime t = new DateTime(2026, 11, 15, 10, 30, 0, DateTimeKind.Utc);
                    AnalysisRecord video = Record("v1", MediaKindEnum.Video, t, true, 6000);
                    video.MediaDurationMs = 12000;
                    video.RealtimeFactor = 0.5;
                    video.Stages = new List<StageTiming> { new StageTiming { Name = "Transcribe", DurationMs = 400 }, new StageTiming { Name = "Probe", DurationMs = 20 } };
                    AnalysisRecord audio = Record("u1", MediaKindEnum.Audio, t.AddMinutes(1), true, 3000);
                    audio.MediaDurationMs = 30000;
                    audio.RealtimeFactor = 0.1;
                    audio.Stages = new List<StageTiming> { new StageTiming { Name = "Transcribe", DurationMs = 800 }, new StageTiming { Name = "Custom", DurationMs = 5 } };

                    List<ModelCallRecord> calls = new List<ModelCallRecord>
                    {
                        Call("v1", "ollama-gemma3-4b", "gemma3:4b", ModelCallPurposeEnum.SceneSummarize, true, 1000, 100, 900, 50),
                        Call("v1", "ollama-gemma3-4b", "gemma3:4b", ModelCallPurposeEnum.SceneSummarize, false, 500, null, null, null),
                        Call("v1", "whisper", "Systran/faster-whisper-small", ModelCallPurposeEnum.Transcribe, true, 300, null, null, null),
                        Call("u1", "ollama-gpt-oss-20b", "gpt-oss:20b", ModelCallPurposeEnum.Summarize, true, 2000, 300, 1700, 40),
                        Call("missing", "x", "y", ModelCallPurposeEnum.Summarize, true, 1, 1, 1, 1)
                    };

                    AnalyticsSummary summary = AnalyticsAggregator.Summarize(t.AddHours(-1), t.AddHours(1), AnalyticsBucketEnum.Hour, new List<AnalysisRecord> { video, audio }, calls);
                    Check.Equal(3, summary.ByModel.Count, "three models (orphan call ignored)");
                    ModelStats gemma = summary.ByModel[0];
                    Check.Equal("ollama-gemma3-4b", gemma.Endpoint, "most calls first");
                    Check.Equal(2L, gemma.Calls, "gemma attempts");
                    Check.Equal(1L, gemma.Failed, "gemma failures");
                    Check.Equal(1L, gemma.TimeToFirstTokenMs!.Count, "ttft only where reported");
                    Check.Equal(750.0, gemma.DurationMs!.Avg, "duration over every attempt");

                    Check.Equal("Probe", summary.ByStage[0].Stage, "stages in pipeline order");
                    Check.Equal("Transcribe", summary.ByStage[1].Stage, "transcribe second");
                    Check.Equal(600.0, summary.ByStage[1].DurationMs!.Avg, "transcribe stage average across analyses");
                    Check.Equal("Custom", summary.ByStage[2].Stage, "unknown stage names last");

                    Check.Equal(ModelCallPurposeEnum.Summarize, summary.ByPurpose[0].Purpose, "purposes in enum order");
                    PurposeStats scene = summary.ByPurpose.Single(p => p.Purpose == ModelCallPurposeEnum.SceneSummarize);
                    Check.Equal(2L, scene.Calls, "scene attempts");

                    Check.Equal(4L, summary.Totals.ModelCallMs!.Count, "model call distribution over attempts");
                    Check.Equal(2L, summary.Totals.TokensPerSecond!.Count, "tokens per second where known");
                    Check.Equal(45.0, summary.Totals.TokensPerSecond.Avg, "tokens per second average");
                    Check.Equal(0.3, summary.Totals.RealtimeFactor!.Avg, "realtime factor average");
                    Check.Equal(2L, summary.Totals.MediaDurationMs!.Count, "media duration samples");
                    Check.Equal(1L, summary.ByMediaKind["Audio"].ModelCallMs!.Count, "per-kind model calls");
                    Check.Equal(3L, summary.ByMediaKind["Video"].ModelCallMs!.Count, "video model calls");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "RecordFactory", "A mock image analysis with a retry and a repair yields one row per attempt", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    List<AnalysisCompletion> completions = new List<AnalysisCompletion>();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Completed = c => { lock (completions) completions.Add(c); };
                        EndpointSettings endpoint = services.Clients.ResolveCompletionEndpoint(null, settings.Inference.DefaultVision);
                        MockInferenceClient mock = (MockInferenceClient)((GatedInferenceClient)services.Clients.GetCompletionClient(endpoint)).Inner;

                        string png = await TestEnvironment.PngAsync(ct).ConfigureAwait(false);
                        ImageAnalysisRequest request = new ImageAnalysisRequest { Image = new MediaSource { Data = await TestEnvironment.Base64Async(png, ct).ConfigureAwait(false) } };
                        request.Functions.Extract.Enable = true;
                        request.Functions.Extract.Fields.Add(new FieldSpec { Key = "hasCats", Type = FieldTypeEnum.Boolean, Prompt = "Cats?" });
                        mock.FailNextTransient(1);
                        mock.MalformNext(1);

                        AnalysisOrigin origin = new AnalysisOrigin { RequestId = "rqi_abc", PrincipalName = "tester" };
                        ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, origin, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));

                        AnalysisRecord record = AnalyticsRecordFactory.Build(completions.Single());
                        Check.Equal(response.Id, record.Id, "analysis id");
                        Check.Equal("rqi_abc", record.RequestId, "request id");
                        Check.Equal(null, record.JobId, "no job for images");
                        Check.Equal("tester", record.PrincipalName, "principal");
                        Check.Equal(MediaKindEnum.Image, record.MediaKind, "kind");
                        Check.Equal("mock", record.VisionEndpoint, "vision endpoint");
                        Check.Equal("mock-vision", record.VisionModel, "vision model");
                        Check.Equal(1, record.ModelCalls, "one logical call");
                        Check.Equal(1, record.Retries, "one retry");
                        Check.Equal(1, record.RepairAttempts, "one repair");
                        Check.Equal(3, record.Calls!.Count, "three attempts stored");
                        Check.True(record.Calls.Select(c => c.Attempt).SequenceEqual(new[] { 1, 2, 3 }), "attempt numbers");
                        Check.True(!record.Calls[0].Success && record.Calls[0].StatusCode == 503, "first attempt failed with 503");
                        Check.True(!record.Calls[1].Success && record.Calls[1].Error!.StartsWith("Unusable output"), "second attempt was repaired");
                        Check.True(record.Calls[2].Success && record.Calls[2].TimeToFirstTokenMs > 0, "third attempt succeeded with timing");
                        Check.True(record.Calls.All(c => c.Id.StartsWith("kfc_") && c.AnalysisId == record.Id && c.InputImages == 1), "row identity");
                        Check.Equal(1, record.FieldCount, "fields");
                        Check.Equal(0.0, record.QueueWaitMs, "no queue wait for images");
                        Check.True(record.RealtimeFactor == null && record.MediaDurationMs == null, "no media duration for images");
                        Check.True(record.Stages.Any(s => s.Name == "Normalize"), "stages copied");
                        Check.True(record.MediaBytes > 0 && record.TotalTokens > 0, "bytes and tokens");
                    }
                }, new[] { "ffmpeg" }, skip)
            });
        }

        private static AnalysisRecord Record(string id, MediaKindEnum kind, DateTime completed, bool success, double ms)
        {
            return new AnalysisRecord
            {
                Id = id,
                MediaKind = kind,
                StartedUtc = completed.AddMilliseconds(-ms),
                CompletedUtc = completed,
                Success = success,
                AnalysisMs = ms,
                MediaBytes = 1000,
                ModelCalls = 1,
                TotalTokens = 10
            };
        }

        private static ModelCallRecord Call(string analysisId, string endpoint, string model, ModelCallPurposeEnum purpose, bool success, double ms, double? ttft, double? generation, double? tps)
        {
            return new ModelCallRecord
            {
                Id = "kfc_" + Guid.NewGuid().ToString("N"),
                AnalysisId = analysisId,
                Endpoint = endpoint,
                Model = model,
                Provider = endpoint == "whisper" ? ProviderTypeEnum.OpenAiCompatible : ProviderTypeEnum.Ollama,
                Purpose = purpose,
                Success = success,
                DurationMs = ms,
                TimeToFirstTokenMs = ttft,
                GenerationMs = generation,
                TokensPerSecond = tps,
                CompletionTokens = 10
            };
        }
    }
}
