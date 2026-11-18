namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Route, authentication, job lifecycle, settings, and request history tests against an in-process server.
    /// </summary>
    public static class ServerSuite
    {
        private const string Id = "Server";
        private const string Admin = ServerFixture.AdminKey;
        private const string Key = ServerFixture.AccessKey;

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Server", new[]
            {
                new TestCaseDescriptor(Id, "AnonymousRoutes", "Health, root, favicon, and OpenAPI need no key", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    HealthResponse health = await f.JsonAsync<HealthResponse>(HttpMethod.Get, "/v1.0/health", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("Sqlite", health.Database, "database");
                    Check.True(health.Endpoints.ContainsKey("mock"), "endpoint health listed");

                    using (HttpResponseMessage head = await f.SendAsync(HttpMethod.Head, "/", null, null, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, head.StatusCode, "HEAD /");
                        Check.True(head.Headers.Contains("x-request-id"), "x-request-id on every response");
                    }

                    using (HttpResponseMessage root = await f.SendAsync(HttpMethod.Get, "/", null, null, ct).ConfigureAwait(false))
                    {
                        string html = await root.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        Check.True(html.Contains("Keyframe") && html.Contains("/openapi.json"), "root page");
                    }

                    using (HttpResponseMessage icon = await f.SendAsync(HttpMethod.Get, "/favicon.ico", null, null, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.OK, icon.StatusCode, "favicon served from assets/glyph.ico");
                    }

                    using (HttpResponseMessage openApi = await f.SendAsync(HttpMethod.Get, "/openapi.json", null, null, ct).ConfigureAwait(false))
                    {
                        string doc = await openApi.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.OK, openApi.StatusCode, "openapi status");
                        foreach (string path in new[] { "/v1.0/image", "/v1.0/video", "/v1.0/jobs/{id}/result", "/v1.0/api/request-history/summary", "/v1.0/settings" })
                            Check.True(doc.Contains("\"" + path + "\""), "OpenAPI documents " + path);
                    }
                }, new[] { "server" }),

                new TestCaseDescriptor(Id, "Authentication", "401 without a key, 401 for inactive keys, 403 for admin routes, bearer and x-api-key both work", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    ApiErrorResponse missing = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/whoami", null, null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                    Check.Equal(ApiErrorEnum.AuthenticationFailed, missing.Error, "error code");
                    Check.True(!String.IsNullOrEmpty(missing.RequestId), "request id in error");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/whoami", "disabled-key-0000", null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);

                    WhoAmIResponse me = await f.JsonAsync<WhoAmIResponse>(HttpMethod.Get, "/v1.0/whoami", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("tester", me.PrincipalName, "principal");
                    Check.Equal(false, me.IsAdmin, "not admin");

                    using (HttpRequestMessage bearer = new HttpRequestMessage(HttpMethod.Get, "/v1.0/whoami"))
                    {
                        bearer.Headers.Add("Authorization", "Bearer " + Admin);
                        using (HttpResponseMessage response = await f.Http.SendAsync(bearer, ct).ConfigureAwait(false))
                        {
                            WhoAmIResponse admin = KeyframeSerializer.Deserialize<WhoAmIResponse>(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))!;
                            Check.True(admin.IsAdmin && admin.PrincipalName == "admin", "bearer admin");
                        }
                    }

                    ApiErrorResponse forbidden = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/settings", Key, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    Check.Equal(ApiErrorEnum.AuthorizationFailed, forbidden.Error, "403 code");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/nothing-here", Key, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                }, new[] { "server" }),

                new TestCaseDescriptor(Id, "ImageRoute", "Image analysis over HTTP with context list and typed fields", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.JpegAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    string body = "{\"Image\":{\"Data\":\"" + data + "\"},\"Context\":[\"Shelter intake photo.\",{\"Label\":\"Location\",\"Text\":\"Yard\"}],"
                        + "\"Functions\":{\"Summarize\":{\"Enable\":true},\"Extract\":{\"Enable\":true,\"Fields\":["
                        + "{\"Key\":\"hasCats\",\"Type\":\"Boolean\",\"Prompt\":\"Cats?\"},{\"Key\":\"dogCount\",\"Type\":\"Integer\",\"Prompt\":\"Dogs?\"}]}}}";
                    ImageAnalysisResponse response = await f.JsonAsync<ImageAnalysisResponse>(HttpMethod.Post, "/v1.0/image", Key, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(true, response.Success, "success");
                    Check.True(response.Results.ContainsKey("Summary") && response.Results.ContainsKey("hasCats") && response.Results.ContainsKey("dogCount"), "result keys");
                    Check.Equal(2, response.Fields.Count, "field details");
                    Check.True(response.Telemetry.ModelCalls.Count == 2 && response.Telemetry.Totals.TotalTokens > 0, "telemetry");

                    ApiErrorResponse invalid = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/image", Key, "{\"Functions\":{}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.True(invalid.Details.Count > 0, "validation details");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/image", Key, "{not json", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/image", Key, "{\"Image\":{\"Data\":\"aGVsbG8gd29ybGQ=\"},\"Functions\":{\"Summarize\":{\"Enable\":true}}}", (HttpStatusCode)422, ct).ConfigureAwait(false);
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/image", Key, "{\"Image\":{\"Data\":\"" + data + "\"},\"Model\":{\"Endpoint\":\"nope\"},\"Functions\":{\"Summarize\":{\"Enable\":true}}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "ProbeRoute", "Probe returns deterministic properties", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    MediaProperties props = await f.JsonAsync<MediaProperties>(HttpMethod.Post, "/v1.0/probe", Key, "{\"Media\":{\"Data\":\"" + data + "\"}}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(MediaKindEnum.Video, props.MediaKind, "kind");
                    Check.True(props.DurationMs > 11000 && props.Width == 640, "duration and width");
                    Check.True(!String.IsNullOrEmpty(props.Sha256), "hash");
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "VideoJobLifecycle", "202, progress, result, list, delete", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    string body = "{\"Video\":{\"Data\":\"" + data + "\"},\"Functions\":{\"Summarize\":{\"Enable\":true},\"Scenes\":{\"Enable\":true},\"Transcribe\":{\"Enable\":true}}}";

                    JobResponse accepted = await f.JsonAsync<JobResponse>(HttpMethod.Post, "/v1.0/video", Key, body, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.True(accepted.Id.StartsWith("kfj_"), "job id");
                    Check.Equal("tester", accepted.PrincipalName, "principal recorded");

                    JobResponse job = await WaitForAsync(f, accepted.Id, ct).ConfigureAwait(false);
                    Check.Equal(JobStatusEnum.Succeeded, job.Status, "succeeded: " + job.Error);
                    Check.True(job.DurationMs > 11000 && job.SizeBytes > 0, "media stats on job");
                    Check.Equal("/v1.0/jobs/" + job.Id + "/result", job.ResultUrl, "result url");

                    VideoAnalysisResponse result = await f.JsonAsync<VideoAnalysisResponse>(HttpMethod.Get, job.ResultUrl!, Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(result.Scenes.Count >= 2, "scenes");
                    Check.True(result.Transcript != null, "transcript");
                    Check.True(result.Results.ContainsKey("Summary"), "whole-video summary");

                    EnumerationResult<JobResponse> list = await f.JsonAsync<EnumerationResult<JobResponse>>(HttpMethod.Get, "/v1.0/jobs?status=Succeeded&pageSize=100", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(list.Items.Any(j => j.Id == job.Id), "listed");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/jobs?status=Bogus", Key, null, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);

                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/jobs/" + job.Id + "/cancel", Key, null, HttpStatusCode.Conflict, ct).ConfigureAwait(false);
                    using (HttpResponseMessage deleted = await f.SendAsync(HttpMethod.Delete, "/v1.0/jobs/" + job.Id, Key, null, ct).ConfigureAwait(false))
                    {
                        Check.Equal(HttpStatusCode.NoContent, deleted.StatusCode, "deleted");
                    }

                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/jobs/" + job.Id, Key, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "VideoWait", "Wait returns the full result synchronously", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    string body = "{\"Video\":{\"Data\":\"" + data + "\"},\"Wait\":true,\"WaitTimeoutMs\":60000,\"Functions\":{\"Scenes\":{\"Enable\":true}}}";
                    VideoAnalysisResponse result = await f.JsonAsync<VideoAnalysisResponse>(HttpMethod.Post, "/v1.0/video", Key, body, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(result.Scenes.Count >= 1, "scenes returned inline");
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "AudioRoute", "Audio jobs: 202 and poll, Wait, MediaKind filter, validation", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    string body = "{\"Audio\":{\"Data\":\"" + data + "\",\"Filename\":\"tone.mp3\"},\"Context\":[\"A test tone.\"],"
                        + "\"Functions\":{\"Transcribe\":{\"Enable\":true,\"Language\":\"en\"},\"Summarize\":{\"Enable\":true,\"MaxWords\":50},"
                        + "\"Extract\":{\"Enable\":true,\"Fields\":[{\"Key\":\"speakerCount\",\"Type\":\"Integer\",\"Prompt\":\"Speakers?\"}]}}}";

                    JobResponse accepted = await f.JsonAsync<JobResponse>(HttpMethod.Post, "/v1.0/audio", Key, body, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                    Check.Equal(MediaKindEnum.Audio, accepted.MediaKind, "audio job");
                    JobResponse job = await WaitForAsync(f, accepted.Id, ct).ConfigureAwait(false);
                    Check.Equal(JobStatusEnum.Succeeded, job.Status, "succeeded: " + job.Error);
                    AudioAnalysisResponse result = await f.JsonAsync<AudioAnalysisResponse>(HttpMethod.Get, job.ResultUrl!, Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(MediaKindEnum.Audio, result.MediaKind, "result kind");
                    Check.True(result.Transcript != null && result.Transcript.Segments.Count > 0, "transcript");
                    Check.True(result.Results["Summary"] != null && result.Results.ContainsKey("speakerCount"), "summary and field");
                    Check.True(result.RequestId != null && result.RequestId.Length > 0, "request id recorded");
                    Check.True(result.Telemetry.QueuedMs.HasValue && result.Telemetry.QueueWaitMs >= 0, "queue wait recorded");

                    EnumerationResult<JobResponse> list = await f.JsonAsync<EnumerationResult<JobResponse>>(HttpMethod.Get, "/v1.0/jobs?mediaKind=Audio&pageSize=100", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(list.Items.Any(j => j.Id == job.Id) && list.Items.All(j => j.MediaKind == MediaKindEnum.Audio), "media kind filter");

                    string wait = "{\"Audio\":{\"Data\":\"" + data + "\"},\"Wait\":true,\"WaitTimeoutMs\":60000}";
                    AudioAnalysisResponse inline = await f.JsonAsync<AudioAnalysisResponse>(HttpMethod.Post, "/v1.0/audio", Key, wait, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(inline.Success && inline.Results.ContainsKey("Summary") && inline.Transcript != null, "defaults transcribe and summarize");

                    ApiErrorResponse invalid = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/audio", Key,
                        "{\"Audio\":{\"Data\":\"" + data + "\"},\"Functions\":{\"Transcribe\":{\"Enable\":false},\"Summarize\":{\"Enable\":true}}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.True(invalid.Details.Any(d => d.Contains("Transcribe")), "summary needs transcription");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/audio", Key, "{\"Audio\":{\"Data\":\"" + data + "\"},\"TranscriptionModel\":{\"Endpoint\":\"nope\"}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/audio", null, wait, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "AnalyticsRoutes", "Image and audio analyses are recorded; summary, list, read, delete need admin", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(null, ct).ConfigureAwait(false))
                    {
                        string png = await TestEnvironment.Base64Async(await TestEnvironment.PngAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        ImageAnalysisResponse image = await f.JsonAsync<ImageAnalysisResponse>(HttpMethod.Post, "/v1.0/image", Key,
                            "{\"Image\":{\"Data\":\"" + png + "\"},\"Functions\":{\"Summarize\":{\"Enable\":true}}}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(!String.IsNullOrEmpty(image.RequestId), "image request id");
                        Check.Equal(0.0, image.Telemetry.QueueWaitMs, "no queue wait for images");

                        string mp3 = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        AudioAnalysisResponse audio = await f.JsonAsync<AudioAnalysisResponse>(HttpMethod.Post, "/v1.0/audio", Key,
                            "{\"Audio\":{\"Data\":\"" + mp3 + "\"},\"Wait\":true}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/image", Key, "{\"Image\":{\"Data\":\"aGVsbG8gd29ybGQ=\"},\"Functions\":{\"Summarize\":{\"Enable\":true}}}", (HttpStatusCode)422, ct).ConfigureAwait(false);
                        Check.True(await f.Server.Context.Analytics.FlushAsync(10000, ct).ConfigureAwait(false), "analytics written");

                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/analytics/summary", Key, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                        AnalyticsSummary summary = await f.JsonAsync<AnalyticsSummary>(HttpMethod.Get, "/v1.0/analytics/summary", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal("hour", summary.Bucket, "24 hour default range uses hourly buckets");
                        Check.True(summary.Buckets.Count >= 24 && summary.Buckets.Count <= 25, "every hour present: " + summary.Buckets.Count);
                        Check.Equal(3L, summary.Totals.Count, "three analyses");
                        Check.Equal(1L, summary.Totals.Failed, "the undecodable image counts as a failure");
                        Check.Equal(2L, summary.ByMediaKind["Image"].Count, "two images");
                        Check.Equal(1L, summary.ByMediaKind["Audio"].Count, "one audio");
                        Check.True(summary.Totals.TimeToFirstTokenMs != null && summary.Totals.TimeToFirstTokenMs.Count >= 2, "time to first token");
                        Check.True(summary.Totals.TokensPerSecond != null && summary.Totals.GenerationMs != null, "generation stats");
                        Check.True(summary.ByMediaKind["Audio"].RealtimeFactor != null && summary.ByMediaKind["Audio"].MediaDurationMs!.Avg > 7000, "audio realtime factor and duration");
                        Check.True(summary.ByModel.Any(m => m.Endpoint == "mock") && summary.ByModel.Any(m => m.Endpoint == "mock-stt"), "by model");
                        Check.True(summary.ByStage.Any(s => s.Stage == "Transcribe") && summary.ByStage.Any(s => s.Stage == "Normalize"), "by stage");
                        Check.True(summary.ByPurpose.Any(p => p.Purpose == ModelCallPurposeEnum.Transcribe), "by purpose");

                        AnalyticsSummary minute = await f.JsonAsync<AnalyticsSummary>(HttpMethod.Get, "/v1.0/analytics/summary?bucket=minute&mediaKind=Audio&fromUtc=" + Uri.EscapeDataString(DateTime.UtcNow.AddMinutes(-30).ToString("o")), Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(minute.Bucket == "minute" && minute.Buckets.Count >= 30 && minute.Totals.Count == 1 && minute.MediaKind == MediaKindEnum.Audio, "minute buckets and media filter");
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/analytics/summary?bucket=minute&fromUtc=2020-01-01T00:00:00Z", Admin, null, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/analytics/summary?bucket=week", Admin, null, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);

                        EnumerationResult<AnalysisRecord> list = await f.JsonAsync<EnumerationResult<AnalysisRecord>>(HttpMethod.Get, "/v1.0/analytics/analyses?maxResults=2", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(3L, list.TotalCount, "listed");
                        Check.Equal(2, list.Items.Count, "maxResults");
                        EnumerationResult<AnalysisRecord> failures = await f.JsonAsync<EnumerationResult<AnalysisRecord>>(HttpMethod.Get, "/v1.0/analytics/analyses?success=false", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(failures.TotalCount == 1 && failures.Items[0].Error != null, "failure listed with error");

                        AnalysisRecord audioRecord = await f.JsonAsync<AnalysisRecord>(HttpMethod.Get, "/v1.0/analytics/analyses/" + audio.Id, Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(MediaKindEnum.Audio, audioRecord.MediaKind, "audio record");
                        Check.True(audioRecord.JobId != null && audioRecord.JobId.StartsWith("kfj_"), "job id");
                        Check.Equal(audio.RequestId, audioRecord.RequestId, "request id");
                        Check.Equal("tester", audioRecord.PrincipalName, "principal");
                        Check.Equal("mock-stt", audioRecord.TranscriptionEndpoint, "transcription endpoint");
                        Check.Equal("mock", audioRecord.TextEndpoint, "text endpoint");
                        Check.True(audioRecord.Calls != null && audioRecord.Calls.Count == audio.Telemetry.ModelCalls.Count, "one row per attempt");
                        Check.True(audioRecord.Calls!.All(c => c.Id.StartsWith("kfc_")), "call ids");
                        Check.True(audioRecord.TranscriptCharacters > 0 && audioRecord.InputAudioBytes > 0, "transcript and audio bytes");

                        DeleteResult one = await f.JsonAsync<DeleteResult>(HttpMethod.Delete, "/v1.0/analytics/analyses/" + image.Id, Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(1, one.DeletedCount, "deleted one");
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/analytics/analyses/" + image.Id, Admin, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Delete, "/v1.0/analytics/analyses/" + image.Id, Admin, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                        DeleteResult none = await f.JsonAsync<DeleteResult>(HttpMethod.Delete, "/v1.0/analytics/analyses?olderThanUtc=2020-01-01T00:00:00Z", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(0, none.DeletedCount, "nothing older than 2020");
                        DeleteResult rest = await f.JsonAsync<DeleteResult>(HttpMethod.Delete, "/v1.0/analytics/analyses", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(2, rest.DeletedCount, "deleted the rest");

                        f.Settings.Settings.Analytics.Enable = false;
                        await f.JsonAsync<ImageAnalysisResponse>(HttpMethod.Post, "/v1.0/image", Key, "{\"Image\":{\"Data\":\"" + png + "\"},\"Functions\":{\"Summarize\":{\"Enable\":true}}}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                        await f.Server.Context.Analytics.FlushAsync(5000, ct).ConfigureAwait(false);
                        EnumerationResult<AnalysisRecord> disabled = await f.JsonAsync<EnumerationResult<AnalysisRecord>>(HttpMethod.Get, "/v1.0/analytics/analyses", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(0L, disabled.TotalCount, "nothing written while disabled");
                    }
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "JobCancel", "A queued job can be canceled", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(s => s.Jobs.MaxConcurrentJobs = 1, ct).ConfigureAwait(false))
                    {
                        string data = await TestEnvironment.Base64Async(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        string body = "{\"Video\":{\"Data\":\"" + data + "\"},\"Functions\":{\"Scenes\":{\"Enable\":true}}}";
                        JobResponse first = await f.JsonAsync<JobResponse>(HttpMethod.Post, "/v1.0/video", Key, body, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                        JobResponse second = await f.JsonAsync<JobResponse>(HttpMethod.Post, "/v1.0/video", Key, body, HttpStatusCode.Accepted, ct).ConfigureAwait(false);
                        JobResponse canceled = await f.JsonAsync<JobResponse>(HttpMethod.Post, "/v1.0/jobs/" + second.Id + "/cancel", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(JobStatusEnum.Canceled, canceled.Status, "canceled");
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/jobs/" + second.Id + "/result", Key, null, HttpStatusCode.Conflict, ct).ConfigureAwait(false);
                        JobResponse done = await WaitForAsync(f, first.Id, ct).ConfigureAwait(false);
                        Check.Equal(JobStatusEnum.Succeeded, done.Status, "first job unaffected");
                    }
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Endpoints", "Endpoints are listed with masked keys; tests need admin", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    List<EndpointResponse> endpoints = await f.JsonAsync<List<EndpointResponse>>(HttpMethod.Get, "/v1.0/endpoints", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(endpoints.Any(e => e.Name == "mock" && e.IsDefault && e.ProviderAvailable), "mock endpoint");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/endpoints/mock/test", Key, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                    EndpointTestResponse test = await f.JsonAsync<EndpointTestResponse>(HttpMethod.Post, "/v1.0/endpoints/mock/test", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal(true, test.Success, "mock test");
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/endpoints/missing/test", Admin, null, HttpStatusCode.NotFound, ct).ConfigureAwait(false);
                }, new[] { "server" }),

                new TestCaseDescriptor(Id, "Settings", "Masked read, masked write-back keeps secrets, live apply, validation", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(null, ct).ConfigureAwait(false))
                    {
                        SettingsResponse read = await f.JsonAsync<SettingsResponse>(HttpMethod.Get, "/v1.0/settings", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal("****1234", read.Settings.Auth.AdminApiKey, "admin key masked");
                        Check.True(read.Settings.Auth.AccessKeys.All(k => k.Key.StartsWith("****")), "access keys masked");
                        Check.True(read.Fields.Any(x => x.Path == "Rest.Port" && x.ApplyMode == "Restart"), "restart metadata");
                        Check.True(read.Fields.Any(x => x.Path == "Jobs.MaxConcurrentJobs" && x.ApplyMode == "Live"), "live metadata");

                        KeyframeSettings edit = read.Settings;
                        edit.Jobs.MaxQueuedJobs = 7;
                        SettingsResponse saved = await f.JsonAsync<SettingsResponse>(HttpMethod.Put, "/v1.0/settings", Admin, edit, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(7, saved.Settings.Jobs.MaxQueuedJobs, "saved");
                        Check.Equal(false, saved.RestartRequired, "live change does not need a restart");
                        Check.Equal(7, f.Settings.Settings.Jobs.MaxQueuedJobs, "applied live");
                        Check.Equal(ServerFixture.AdminKey, f.Settings.Settings.Auth.AdminApiKey, "masked admin key kept");
                        Check.Equal(ServerFixture.AccessKey, f.Settings.Settings.Auth.AccessKeys.First(k => k.Name == "tester").Key, "masked access key kept");
                        await f.JsonAsync<WhoAmIResponse>(HttpMethod.Get, "/v1.0/whoami", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);

                        edit.Rest.Port = edit.Rest.Port == 65000 ? 65001 : 65000;
                        SettingsResponse restart = await f.JsonAsync<SettingsResponse>(HttpMethod.Put, "/v1.0/settings", Admin, edit, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(true, restart.RestartRequired, "port change needs a restart");

                        edit.Inference.Endpoints.Add(new EndpointSettings { Name = "mock" });
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Put, "/v1.0/settings", Admin, edit, HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Put, "/v1.0/settings", Admin, "{\"Rest\":{\"Port\":0}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    }
                }, new[] { "server" }),

                new TestCaseDescriptor(Id, "RequestHistory", "Requests are captured with redacted headers; list, summary, read, delete", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(null, ct).ConfigureAwait(false))
                    {
                        await f.JsonAsync<WhoAmIResponse>(HttpMethod.Get, "/v1.0/whoami", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/whoami", "wrong-key", null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Put, "/v1.0/settings", Admin, "{\"Auth\":{\"AdminApiKey\":\"supersecret\"},\"Rest\":{\"Port\":0}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                        Check.True(await f.Server.Context.RequestHistory.FlushAsync(5000, ct).ConfigureAwait(false), "capture drained");

                        EnumerationResult<RequestHistoryEntry> page = await f.JsonAsync<EnumerationResult<RequestHistoryEntry>>(HttpMethod.Get, "/v1.0/api/request-history?pathContains=whoami", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(2L, page.TotalCount, "two whoami calls captured");
                        RequestHistoryEntry ok = page.Items.First(i => i.StatusCode == 200);
                        Check.Equal("tester", ok.PrincipalName, "principal");

                        RequestHistoryEntry full = await f.JsonAsync<RequestHistoryEntry>(HttpMethod.Get, "/v1.0/api/request-history/" + ok.Id, Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal("[redacted]", full.RequestHeaders!["x-api-key"], "x-api-key redacted");
                        Check.True(full.ResponseHeaders!.ContainsKey("x-request-id"), "response headers kept");
                        Check.True(full.ResponseBody != null && full.ResponseBody.Contains("tester"), "response body kept");

                        EnumerationResult<RequestHistoryEntry> settingsCalls = await f.JsonAsync<EnumerationResult<RequestHistoryEntry>>(HttpMethod.Get, "/v1.0/api/request-history?pathContains=settings", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        RequestHistoryEntry put = await f.JsonAsync<RequestHistoryEntry>(HttpMethod.Get, "/v1.0/api/request-history/" + settingsCalls.Items[0].Id, Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal("[redacted]", put.RequestBody, "settings request body redacted");

                        RequestHistorySummary summary = await f.JsonAsync<RequestHistorySummary>(HttpMethod.Get, "/v1.0/api/request-history/summary?bucketMinutes=15", Key, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(summary.Buckets.Count == 96 || summary.Buckets.Count == 97, "24 hours of aligned 15 minute buckets, gap filled");
                        Check.True(summary.TotalFailure >= 2, "failures counted");

                        await f.JsonAsync<ApiErrorResponse>(HttpMethod.Delete, "/v1.0/api/request-history/" + ok.Id, Key, null, HttpStatusCode.Forbidden, ct).ConfigureAwait(false);
                        using (HttpResponseMessage del = await f.SendAsync(HttpMethod.Delete, "/v1.0/api/request-history/" + ok.Id, Admin, null, ct).ConfigureAwait(false))
                        {
                            Check.Equal(HttpStatusCode.NoContent, del.StatusCode, "deleted");
                        }

                        DeleteResult bulk = await f.JsonAsync<DeleteResult>(HttpMethod.Delete, "/v1.0/api/request-history?pathContains=whoami", Admin, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.Equal(1, bulk.DeletedCount, "bulk delete count");
                    }
                }, new[] { "server" }),

                new TestCaseDescriptor(Id, "AuthRateLimit", "Repeated bad keys from one address get 429", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(s => s.Auth.MaxFailedAttemptsPerMinute = 5, ct).ConfigureAwait(false))
                    {
                        for (int i = 0; i < 5; i++)
                            await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/jobs", "bad-key", null, HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                        ApiErrorResponse limited = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Get, "/v1.0/jobs", Key, null, (HttpStatusCode)429, ct).ConfigureAwait(false);
                        Check.Equal(ApiErrorEnum.TooManyRequests, limited.Error, "429 code");
                        await f.JsonAsync<HealthResponse>(HttpMethod.Get, "/v1.0/health", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    }
                }, new[] { "server" }),

                new TestCaseDescriptor(Id, "AuthDisabled", "With authentication disabled every route is open", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(s => s.Auth.RequireAuthentication = false, ct).ConfigureAwait(false))
                    {
                        WhoAmIResponse me = await f.JsonAsync<WhoAmIResponse>(HttpMethod.Get, "/v1.0/whoami", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(me.IsAdmin && me.PrincipalName == "anonymous" && !me.AuthenticationRequired, "anonymous admin");
                        await f.JsonAsync<SettingsResponse>(HttpMethod.Get, "/v1.0/settings", null, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    }
                }, new[] { "server" })
            });
        }

        private static async Task<JobResponse> WaitForAsync(ServerFixture f, string id, CancellationToken token)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                JobResponse job = await f.JsonAsync<JobResponse>(HttpMethod.Get, "/v1.0/jobs/" + id, Key, null, HttpStatusCode.OK, token).ConfigureAwait(false);
                if (job.Status != JobStatusEnum.Queued && job.Status != JobStatusEnum.Running) return job;
                await Task.Delay(100, token).ConfigureAwait(false);
            }

            throw new TimeoutException("Job " + id + " did not finish in 90 seconds.");
        }
    }
}
