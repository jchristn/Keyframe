namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;
    using Keyframe.Sdk.Serialization;
    using Keyframe.Sdk.Settings;

    /// <summary>
    /// End-to-end SDK tests against a live Keyframe server running the mock provider.
    /// </summary>
    public class SdkTests
    {
        private readonly string _Url;
        private readonly string _AdminKey;
        private readonly string _AccessKey;
        private readonly Fixtures _Fixtures;
        private readonly KeyframeClient _Admin;
        private readonly KeyframeClient _User;
        private readonly KeyframeClient _Anonymous;
        private readonly CancellationToken _Token;

        /// <summary>
        /// Create the test set.
        /// </summary>
        /// <param name="url">Base URL.</param>
        /// <param name="adminKey">Admin key.</param>
        /// <param name="accessKey">Non-admin access key.</param>
        /// <param name="fixtures">Media fixtures.</param>
        /// <param name="admin">Admin client.</param>
        /// <param name="user">Access key client.</param>
        /// <param name="anonymous">Client without a key.</param>
        /// <param name="token">Cancellation token.</param>
        public SdkTests(string url, string adminKey, string accessKey, Fixtures fixtures, KeyframeClient admin, KeyframeClient user, KeyframeClient anonymous, CancellationToken token)
        {
            _Url = url;
            _AdminKey = adminKey;
            _AccessKey = accessKey;
            _Fixtures = fixtures;
            _Admin = admin;
            _User = user;
            _Anonymous = anonymous;
            _Token = token;
        }

        /// <summary>
        /// Run every test.
        /// </summary>
        /// <param name="runner">Runner.</param>
        /// <returns>Task.</returns>
        public async Task RunAllAsync(TestRunner runner)
        {
            Console.WriteLine("System");
            await runner.RunAsync("health anonymous", HealthAsync).ConfigureAwait(false);
            await runner.RunAsync("HEAD / liveness", AliveAsync).ConfigureAwait(false);
            await runner.RunAsync("raw request for /openapi.json", OpenApiAsync).ConfigureAwait(false);
            await runner.RunAsync("unreachable server raises HttpRequestException", UnreachableAsync).ConfigureAwait(false);
            await runner.RunAsync("pre-canceled token raises OperationCanceledException", CanceledTokenAsync).ConfigureAwait(false);
            await runner.RunAsync("ToString never contains the key", ToStringAsync).ConfigureAwait(false);

            Console.WriteLine("Auth");
            await runner.RunAsync("whoami without a key is 401", WhoAmINoKeyAsync).ConfigureAwait(false);
            await runner.RunAsync("whoami with a wrong key is 401", WhoAmIBadKeyAsync).ConfigureAwait(false);
            await runner.RunAsync("whoami access key (Bearer)", WhoAmIUserAsync).ConfigureAwait(false);
            await runner.RunAsync("whoami access key (x-api-key)", WhoAmIXApiKeyAsync).ConfigureAwait(false);
            await runner.RunAsync("whoami admin key", WhoAmIAdminAsync).ConfigureAwait(false);
            await runner.RunAsync("admin-only routes return 403 for an access key", AdminOnlyAsync).ConfigureAwait(false);

            Console.WriteLine("Image");
            await runner.RunAsync("context serializes as an item array", ContextSerializationAsync).ConfigureAwait(false);
            await runner.RunAsync("image analysis with context and typed fields", ImageAnalysisAsync).ConfigureAwait(false);
            await runner.RunAsync("image analysis PerField mode", ImagePerFieldAsync).ConfigureAwait(false);
            await runner.RunAsync("image validation error maps to 400", ImageValidationAsync).ConfigureAwait(false);
            await runner.RunAsync("undecodable image maps to 422", ImageUndecodableAsync).ConfigureAwait(false);
            await runner.RunAsync("unknown endpoint maps to 400", ImageUnknownEndpointAsync).ConfigureAwait(false);

            Console.WriteLine("Probe");
            await runner.RunAsync("probe image", ProbeImageAsync).ConfigureAwait(false);
            await runner.RunAsync("probe video", ProbeVideoAsync).ConfigureAwait(false);
            await runner.RunAsync("probe without media is 400", ProbeInvalidAsync).ConfigureAwait(false);

            Console.WriteLine("Video and jobs");
            await runner.RunAsync("video job lifecycle (202, poll, result, list, delete)", VideoLifecycleAsync).ConfigureAwait(false);
            await runner.RunAsync("video Wait returns the result directly", VideoWaitAsync).ConfigureAwait(false);
            await runner.RunAsync("AnalyzeAndWaitAsync", AnalyzeAndWaitAsync).ConfigureAwait(false);
            await runner.RunAsync("queued job: result 409, delete 409, cancel, cancel again 409", CancelQueuedAsync).ConfigureAwait(false);
            await runner.RunAsync("failed job raises KeyframeJobException", FailedJobAsync).ConfigureAwait(false);
            await runner.RunAsync("job list filters and paging", JobListAsync).ConfigureAwait(false);
            await runner.RunAsync("unknown job is 404", UnknownJobAsync).ConfigureAwait(false);

            Console.WriteLine("Endpoints");
            await runner.RunAsync("list endpoints", EndpointsAsync).ConfigureAwait(false);
            await runner.RunAsync("test endpoint (admin)", EndpointTestAsync).ConfigureAwait(false);
            await runner.RunAsync("test unknown endpoint is 404", EndpointUnknownAsync).ConfigureAwait(false);

            Console.WriteLine("Settings");
            await runner.RunAsync("settings read masks secrets", SettingsReadAsync).ConfigureAwait(false);
            await runner.RunAsync("settings live write round-trips masked secrets", SettingsWriteAsync).ConfigureAwait(false);

            Console.WriteLine("Request history");
            await runner.RunAsync("request history list and filter", HistoryListAsync).ConfigureAwait(false);
            await runner.RunAsync("request history summary", HistorySummaryAsync).ConfigureAwait(false);
            await runner.RunAsync("request history read redacts credentials", HistoryReadAsync).ConfigureAwait(false);
            await runner.RunAsync("request history delete (admin) and 404 after", HistoryDeleteAsync).ConfigureAwait(false);
            await runner.RunAsync("request history bulk delete with a no-match filter", HistoryBulkDeleteAsync).ConfigureAwait(false);
        }

        #region System

        private async Task HealthAsync()
        {
            HealthResponse health = await _Anonymous.Health.GetAsync(_Token).ConfigureAwait(false);
            Check.Equal("Keyframe", health.Product, "Product");
            Check.True(health.Status == "Healthy" || health.Status == "Degraded", "Status was " + health.Status);
            Check.True(!String.IsNullOrEmpty(health.Version), "Version is empty");
            Check.True(health.Endpoints.ContainsKey("mock"), "Endpoints missing 'mock'");
        }

        private async Task AliveAsync()
        {
            Check.True(await _Anonymous.Health.IsAliveAsync(_Token).ConfigureAwait(false), "HEAD / did not return 200");
        }

        private async Task OpenApiAsync()
        {
            RawResponse raw = await _Anonymous.SendRawAsync(HttpMethod.Get, "/openapi.json", null, _Token).ConfigureAwait(false);
            Check.Equal(200, raw.StatusCode, "status");
            Check.True(raw.Body.Contains("/v1.0/image"), "OpenAPI document does not list /v1.0/image");
        }

        private async Task UnreachableAsync()
        {
            using (KeyframeClient dead = new KeyframeClient("http://127.0.0.1:1", _AccessKey))
            {
                dead.TimeoutMs = 5000;
                Check.True(!await dead.Health.IsAliveAsync(_Token).ConfigureAwait(false), "IsAliveAsync should be false");
                bool threw = false;
                try
                {
                    await dead.Health.GetAsync(_Token).ConfigureAwait(false);
                }
                catch (HttpRequestException)
                {
                    threw = true;
                }

                Check.True(threw, "Expected HttpRequestException");
            }
        }

        private async Task CanceledTokenAsync()
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                bool threw = false;
                try
                {
                    await _User.Health.GetAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    threw = true;
                }

                Check.True(threw, "Expected OperationCanceledException");
            }
        }

        private Task ToStringAsync()
        {
            Check.True(!_Admin.ToString().Contains(_AdminKey), "ToString leaked the admin key");
            Check.True(_Admin.HasApiKey, "HasApiKey should be true");
            Check.True(!_Anonymous.HasApiKey, "HasApiKey should be false");
            return Task.CompletedTask;
        }

        #endregion

        #region Auth

        private async Task WhoAmINoKeyAsync()
        {
            KeyframeApiException e = await Check.ThrowsApiAsync(401, () => _Anonymous.WhoAmI.GetAsync(_Token)).ConfigureAwait(false);
            Check.Equal<ApiErrorEnum?>(ApiErrorEnum.AuthenticationFailed, e.Error, "Error");
            Check.True(e.ApiError != null && e.ApiError.StatusCode == 401, "ApiError.StatusCode");
            Check.True(!String.IsNullOrEmpty(e.RequestId), "RequestId missing");
            Check.Equal("GET", e.Method, "Method");
            Check.Equal("/v1.0/whoami", e.Path, "Path");
        }

        private async Task WhoAmIBadKeyAsync()
        {
            using (KeyframeClient bad = new KeyframeClient(_Url, "not-a-real-key-" + Guid.NewGuid().ToString("N")))
            {
                await Check.ThrowsApiAsync(401, () => bad.WhoAmI.GetAsync(_Token)).ConfigureAwait(false);
            }
        }

        private async Task WhoAmIUserAsync()
        {
            WhoAmIResponse me = await _User.WhoAmI.GetAsync(_Token).ConfigureAwait(false);
            Check.True(me.IsAuthenticated, "IsAuthenticated");
            Check.True(!me.IsAdmin, "access key reported as admin");
            Check.True(!String.IsNullOrEmpty(me.PrincipalName), "PrincipalName empty");
        }

        private async Task WhoAmIXApiKeyAsync()
        {
            using (KeyframeClient client = new KeyframeClient(_Url, _AccessKey))
            {
                client.AuthHeader = AuthHeaderEnum.XApiKey;
                WhoAmIResponse me = await client.WhoAmI.GetAsync(_Token).ConfigureAwait(false);
                Check.True(me.IsAuthenticated && !me.IsAdmin, "x-api-key access key not resolved");
            }
        }

        private async Task WhoAmIAdminAsync()
        {
            WhoAmIResponse me = await _Admin.WhoAmI.GetAsync(_Token).ConfigureAwait(false);
            Check.True(me.IsAdmin, "admin key not reported as admin");
        }

        private async Task AdminOnlyAsync()
        {
            KeyframeApiException e = await Check.ThrowsApiAsync(403, () => _User.Settings.GetAsync(_Token)).ConfigureAwait(false);
            Check.Equal<ApiErrorEnum?>(ApiErrorEnum.AuthorizationFailed, e.Error, "Error");
            await Check.ThrowsApiAsync(403, () => _User.Settings.UpdateAsync(new KeyframeSettings(), _Token)).ConfigureAwait(false);
            await Check.ThrowsApiAsync(403, () => _User.Settings.RestartAsync(_Token)).ConfigureAwait(false);
            await Check.ThrowsApiAsync(403, () => _User.Endpoints.TestAsync("mock", _Token)).ConfigureAwait(false);
            await Check.ThrowsApiAsync(403, () => _User.RequestHistory.DeleteAsync("req_doesnotexist", _Token)).ConfigureAwait(false);
            await Check.ThrowsApiAsync(403, () => _User.RequestHistory.DeleteManyAsync(new RequestHistoryFilter { PathContains = "/nothing" }, _Token)).ConfigureAwait(false);
        }

        #endregion

        #region Image

        private Task ContextSerializationAsync()
        {
            string json = "{\"Image\":{\"Url\":\"https://example.com/a.jpg\"},\"Context\":[\"plain\",{\"Label\":\"L\",\"Text\":\"labeled\"}]}";
            ImageAnalysisRequest? parsed = KeyframeSerializer.Deserialize<ImageAnalysisRequest>(json);
            Check.True(parsed != null && parsed.Context.Count == 2, "mixed Context did not parse into two items");
            Check.Equal("plain", parsed!.Context[0].Text, "Context[0].Text");
            Check.Equal("L", parsed.Context[1].Label, "Context[1].Label");
            ImageAnalysisRequest? single = KeyframeSerializer.Deserialize<ImageAnalysisRequest>("{\"Context\":\"just text\"}");
            Check.True(single != null && single.Context.Count == 1 && single.Context[0].Text == "just text", "string Context did not parse");
            string output = KeyframeSerializer.Serialize(parsed);
            Check.True(output.Contains("\"Context\":[{\"Label\":null,\"Text\":\"plain\"},{\"Label\":\"L\",\"Text\":\"labeled\"}]"), "Context did not serialize as items: " + output);
            return Task.CompletedTask;
        }

        private async Task<ImageAnalysisRequest> ImageRequestAsync(ExtractModeEnum mode)
        {
            RequireFixtures();
            return new ImageAnalysisRequest
            {
                Image = await MediaSources.FromFileAsync(_Fixtures.ImagePath, null, _Token).ConfigureAwait(false),
                Context = new List<ContextItem>
                {
                    new ContextItem { Label = "Source", Text = "Synthetic ffmpeg test pattern." },
                    new ContextItem { Text = "There are no animals in test patterns." }
                },
                Functions = new FunctionsSpec
                {
                    Summarize = new SummarizeSpec { Enable = true, MaxWords = 40, Style = SummaryStyleEnum.Caption },
                    Extract = new ExtractSpec
                    {
                        Enable = true,
                        Mode = mode,
                        Fields = new List<FieldSpec>
                        {
                            new FieldSpec { Key = "hasCats", Type = FieldTypeEnum.Boolean, Prompt = "Does this image have cats?" },
                            new FieldSpec { Key = "dogCount", Type = FieldTypeEnum.Integer, Prompt = "How many dogs?", Minimum = 0, Maximum = 20 },
                            new FieldSpec { Key = "setting", Type = FieldTypeEnum.Enum, Prompt = "Where was this taken?", Options = new List<string> { "Indoors", "Outdoors", "Unknown" } },
                            new FieldSpec { Key = "colors", Type = FieldTypeEnum.StringList, Prompt = "List the dominant colors.", MaxItems = 5 },
                            new FieldSpec { Key = "brightness", Type = FieldTypeEnum.Number, Prompt = "Brightness 0 to 100.", Minimum = 0, Maximum = 100 },
                            new FieldSpec { Key = "caption", Type = FieldTypeEnum.String, Prompt = "Short caption.", MaxLength = 80 },
                            new FieldSpec { Key = "details", Type = FieldTypeEnum.Object, Prompt = "Any structured details." }
                        }
                    }
                }
            };
        }

        private async Task ImageAnalysisAsync()
        {
            ImageAnalysisRequest request = await ImageRequestAsync(ExtractModeEnum.Combined).ConfigureAwait(false);
            ImageAnalysisResponse response = await _User.Image.AnalyzeAsync(request, _Token).ConfigureAwait(false);
            Check.True(response.Success, "Success false: " + String.Join("; ", response.Errors));
            Check.True(response.Id.StartsWith("kfa_", StringComparison.Ordinal), "Id prefix: " + response.Id);
            Check.Equal(MediaKindEnum.Image, response.MediaKind, "MediaKind");
            Check.True(response.Properties != null && response.Properties.Width == 640 && response.Properties.Height == 360, "Properties dimensions");
            Check.True(!String.IsNullOrEmpty(response.GetResult<string>("Summary")), "Summary missing");
            Check.Equal(7, response.Fields.Count, "Fields.Count");
            Check.True(response.Fields.All(f => f.Valid), "invalid field: " + String.Join(", ", response.Fields.Where(f => !f.Valid).Select(f => f.Key + "=" + f.Error)));

            bool hasCats = response.GetResult<bool>("hasCats");
            long dogs = response.GetResult<long>("dogCount");
            string? setting = response.GetResult<string>("setting");
            List<string>? colors = response.GetResult<List<string>>("colors");
            double brightness = response.GetResult<double>("brightness");
            Dictionary<string, object?>? details = response.GetResult<Dictionary<string, object?>>("details");
            Check.True(hasCats || !hasCats, "bool read");
            Check.True(dogs >= 0 && dogs <= 20, "dogCount out of range: " + dogs);
            Check.True(setting == "Indoors" || setting == "Outdoors" || setting == "Unknown", "setting not an option: " + setting);
            Check.True(colors != null && colors.Count <= 5, "colors missing or too long");
            Check.True(brightness >= 0 && brightness <= 100, "brightness out of range");
            Check.True(details != null, "details object missing");
            Check.Equal(FieldTypeEnum.Integer, response.Fields.First(f => f.Key == "dogCount").Type, "dogCount Type");
            Check.Equal(dogs, ResultValues.Convert<long>(response.Fields.First(f => f.Key == "dogCount").Value), "FieldResult.Value");

            Check.True(response.Telemetry.ModelCalls.Any(), "no model calls recorded");
            Check.True(response.Telemetry.Stages.Any(s => s.Name == "Extract"), "no Extract stage");
            Check.True(response.Telemetry.Totals.ModelCalls >= 2, "Totals.ModelCalls");
        }

        private async Task ImagePerFieldAsync()
        {
            ImageAnalysisRequest request = await ImageRequestAsync(ExtractModeEnum.PerField).ConfigureAwait(false);
            ImageAnalysisResponse response = await _User.Image.AnalyzeAsync(request, _Token).ConfigureAwait(false);
            Check.True(response.Success, "Success false");
            int extractCalls = response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.Extract);
            Check.Equal(7, extractCalls, "per-field Extract calls");
            Check.True(response.Telemetry.ModelCalls.Where(c => c.Purpose == ModelCallPurposeEnum.Extract).All(c => !String.IsNullOrEmpty(c.Target)), "per-field calls lack Target");
        }

        private async Task ImageValidationAsync()
        {
            ImageAnalysisRequest request = new ImageAnalysisRequest
            {
                Image = new MediaSource { Url = "https://example.com/a.jpg", Data = "AAAA" },
                Functions = new FunctionsSpec
                {
                    Extract = new ExtractSpec
                    {
                        Enable = true,
                        Fields = new List<FieldSpec> { new FieldSpec { Key = "bad key!", Type = FieldTypeEnum.Enum, Prompt = "" } }
                    }
                }
            };

            KeyframeApiException e = await Check.ThrowsApiAsync(400, () => _User.Image.AnalyzeAsync(request, _Token)).ConfigureAwait(false);
            Check.Equal<ApiErrorEnum?>(ApiErrorEnum.BadRequest, e.Error, "Error");
            Check.True(e.ApiError != null && e.ApiError.Details.Count >= 3, "expected several validation details, got: " + e.ResponseBody);
        }

        private async Task ImageUndecodableAsync()
        {
            ImageAnalysisRequest request = new ImageAnalysisRequest
            {
                Image = MediaSources.FromBytes(System.Text.Encoding.UTF8.GetBytes("this is not an image at all, just text bytes"), "image/jpeg"),
                Functions = new FunctionsSpec { Summarize = new SummarizeSpec { Enable = true } }
            };

            KeyframeApiException e = await Check.ThrowsApiAsync(422, () => _User.Image.AnalyzeAsync(request, _Token)).ConfigureAwait(false);
            Check.Equal<ApiErrorEnum?>(ApiErrorEnum.UnprocessableMedia, e.Error, "Error");
        }

        private async Task ImageUnknownEndpointAsync()
        {
            RequireFixtures();
            ImageAnalysisRequest request = new ImageAnalysisRequest
            {
                Image = await MediaSources.FromFileAsync(_Fixtures.ImagePath, null, _Token).ConfigureAwait(false),
                Model = new ModelSelection { Endpoint = "no-such-endpoint" },
                Functions = new FunctionsSpec { Summarize = new SummarizeSpec { Enable = true } }
            };

            await Check.ThrowsApiAsync(400, () => _User.Image.AnalyzeAsync(request, _Token)).ConfigureAwait(false);
        }

        #endregion

        #region Probe

        private async Task ProbeImageAsync()
        {
            RequireFixtures();
            MediaProperties props = await _User.Probe.ProbeAsync(await MediaSources.FromFileAsync(_Fixtures.ImagePath, null, _Token).ConfigureAwait(false), _Token).ConfigureAwait(false);
            Check.Equal(MediaKindEnum.Image, props.MediaKind, "MediaKind");
            Check.Equal<int?>(640, props.Width, "Width");
            Check.True(!String.IsNullOrEmpty(props.Sha256), "Sha256 missing");
        }

        private async Task ProbeVideoAsync()
        {
            RequireFixtures();
            MediaProperties props = await _User.Probe.ProbeAsync(await MediaSources.FromFileAsync(_Fixtures.VideoPath, null, _Token).ConfigureAwait(false), _Token).ConfigureAwait(false);
            Check.Equal(MediaKindEnum.Video, props.MediaKind, "MediaKind");
            Check.True(props.DurationMs.HasValue && Math.Abs(props.DurationMs.Value - 6000) < 500, "DurationMs " + props.DurationMs);
            Check.True(props.HasAudio, "HasAudio");
            Check.Equal("h264", props.VideoCodec, "VideoCodec");
        }

        private async Task ProbeInvalidAsync()
        {
            await Check.ThrowsApiAsync(400, () => _User.Probe.ProbeAsync(new ProbeRequest(), _Token)).ConfigureAwait(false);
        }

        #endregion

        #region Video

        private async Task<VideoAnalysisRequest> VideoRequestAsync(string path, bool wait)
        {
            RequireFixtures();
            return new VideoAnalysisRequest
            {
                Video = await MediaSources.FromFileAsync(path, null, _Token).ConfigureAwait(false),
                Context = new List<ContextItem> { new ContextItem { Label = "Event", Text = "Synthetic test video." } },
                Wait = wait,
                WaitTimeoutMs = 60000,
                Functions = new VideoFunctionsSpec
                {
                    Summarize = new SummarizeSpec { Enable = true },
                    Extract = new ExtractSpec
                    {
                        Enable = true,
                        Fields = new List<FieldSpec> { new FieldSpec { Key = "topics", Type = FieldTypeEnum.StringList, Prompt = "Main topics.", MaxItems = 4 } }
                    },
                    Scenes = new ScenesSpec
                    {
                        Enable = true,
                        FramesPerScene = 2,
                        Summarize = new SummarizeSpec { Enable = true, MaxWords = 20 },
                        Extract = new ExtractSpec
                        {
                            Enable = true,
                            Fields = new List<FieldSpec> { new FieldSpec { Key = "showsSlide", Type = FieldTypeEnum.Boolean, Prompt = "Is a slide visible?" } }
                        }
                    },
                    Transcribe = new TranscribeSpec { Enable = true, Language = "en" }
                }
            };
        }

        private static void CheckVideoResult(VideoAnalysisResponse result)
        {
            Check.True(result.Success, "Success false: " + String.Join("; ", result.Errors));
            Check.Equal(MediaKindEnum.Video, result.MediaKind, "MediaKind");
            Check.True(result.Scenes.Any(), "no scenes");
            Check.True(result.Scenes.All(s => s.Keyframes.Any()), "scene without keyframes");
            Check.True(result.Scenes[0].Results.ContainsKey("showsSlide"), "scene field missing");
            Check.True(result.Transcript != null && result.Transcript.Segments.Any(), "transcript missing");
            Check.True(result.GetResult<List<string>>("topics") != null, "topics missing");
            Check.True(result.Properties != null && result.Properties.HasAudio, "Properties.HasAudio");
        }

        private async Task VideoLifecycleAsync()
        {
            VideoSubmitResult submitted = await _User.Video.AnalyzeAsync(await VideoRequestAsync(_Fixtures.VideoPath, false).ConfigureAwait(false), _Token).ConfigureAwait(false);
            Check.Equal(202, submitted.StatusCode, "StatusCode");
            Check.True(submitted.Job != null && !submitted.HasResult, "202 should carry a job");
            Check.True(submitted.JobId.StartsWith("kfj_", StringComparison.Ordinal), "JobId prefix");
            Check.True(submitted.Job!.Status == JobStatusEnum.Queued || submitted.Job.Status == JobStatusEnum.Running, "initial status " + submitted.Job.Status);

            JobResponse done = await _User.Jobs.WaitAsync(submitted.JobId, 200, 120000, _Token).ConfigureAwait(false);
            Check.Equal(JobStatusEnum.Succeeded, done.Status, "terminal status");
            Check.True(done.ResultUrl != null && done.CompletedUtc.HasValue, "ResultUrl or CompletedUtc missing");
            Check.Equal(100.0, done.Progress.Percent, "Progress.Percent");

            VideoAnalysisResponse result = await _User.Jobs.GetResultAsync(submitted.JobId, _Token).ConfigureAwait(false);
            CheckVideoResult(result);

            EnumerationResult<JobResponse> page = await _User.Jobs.ListAsync(new JobFilter { Status = JobStatusEnum.Succeeded, MediaKind = MediaKindEnum.Video, PageSize = 100 }, _Token).ConfigureAwait(false);
            Check.True(page.Items.Any(j => j.Id == submitted.JobId), "job not in list");
            Check.True(page.Items.All(j => j.Status == JobStatusEnum.Succeeded), "status filter ignored");

            await _User.Jobs.DeleteAsync(submitted.JobId, _Token).ConfigureAwait(false);
            await Check.ThrowsApiAsync(404, () => _User.Jobs.GetAsync(submitted.JobId, _Token)).ConfigureAwait(false);
        }

        private async Task VideoWaitAsync()
        {
            VideoSubmitResult submitted = await _User.Video.AnalyzeAsync(await VideoRequestAsync(_Fixtures.VideoPath, true).ConfigureAwait(false), _Token).ConfigureAwait(false);
            Check.Equal(200, submitted.StatusCode, "StatusCode");
            Check.True(submitted.HasResult && submitted.Job == null, "Wait should return the result");
            Check.True(submitted.JobId.StartsWith("kfj_", StringComparison.Ordinal), "JobId from x-keyframe-job-id header: " + submitted.JobId);
            CheckVideoResult(submitted.Result!);
            await _User.Jobs.DeleteAsync(submitted.JobId, _Token).ConfigureAwait(false);
        }

        private async Task AnalyzeAndWaitAsync()
        {
            VideoAnalysisResponse result = await _User.Video.AnalyzeAndWaitAsync(await VideoRequestAsync(_Fixtures.VideoPath, false).ConfigureAwait(false), 200, 120000, _Token).ConfigureAwait(false);
            CheckVideoResult(result);
        }

        private async Task CancelQueuedAsync()
        {
            SettingsResponse settings = await _Admin.Settings.GetAsync(_Token).ConfigureAwait(false);
            int workers = settings.Settings.Jobs.MaxConcurrentJobs;
            VideoAnalysisRequest blocker = await VideoRequestAsync(_Fixtures.LongVideoPath, false).ConfigureAwait(false);
            blocker.Functions.Scenes.Detection = new SceneDetectionSpec { MinSceneMs = 1000, MaxSceneMs = 2000 };
            List<string> blockers = new List<string>();
            for (int i = 0; i < workers; i++)
            {
                VideoSubmitResult b = await _User.Video.AnalyzeAsync(blocker, _Token).ConfigureAwait(false);
                blockers.Add(b.JobId);
            }

            VideoSubmitResult target = await _User.Video.AnalyzeAsync(await VideoRequestAsync(_Fixtures.VideoPath, false).ConfigureAwait(false), _Token).ConfigureAwait(false);
            try
            {
                JobResponse before = await _User.Jobs.GetAsync(target.JobId, _Token).ConfigureAwait(false);
                if (before.Status != JobStatusEnum.Queued)
                    throw new HarnessSkipException("target job was " + before.Status + " rather than Queued; workers drained too fast");

                KeyframeApiException conflict = await Check.ThrowsApiAsync(409, () => _User.Jobs.GetResultAsync(target.JobId, _Token)).ConfigureAwait(false);
                Check.Equal<ApiErrorEnum?>(ApiErrorEnum.Conflict, conflict.Error, "Error");
                await Check.ThrowsApiAsync(409, () => _User.Jobs.DeleteAsync(target.JobId, _Token)).ConfigureAwait(false);

                JobResponse canceled = await _User.Jobs.CancelAsync(target.JobId, _Token).ConfigureAwait(false);
                Check.Equal(JobStatusEnum.Canceled, canceled.Status, "status after cancel");
                Check.True(canceled.IsTerminal, "IsTerminal");
                await Check.ThrowsApiAsync(409, () => _User.Jobs.CancelAsync(target.JobId, _Token)).ConfigureAwait(false);
                await Check.ThrowsApiAsync(409, () => _User.Jobs.GetResultAsync(target.JobId, _Token)).ConfigureAwait(false);
                await _User.Jobs.DeleteAsync(target.JobId, _Token).ConfigureAwait(false);
            }
            finally
            {
                foreach (string id in blockers)
                {
                    await _User.Jobs.WaitAsync(id, 250, 180000, _Token).ConfigureAwait(false);
                    await _User.Jobs.DeleteAsync(id, _Token).ConfigureAwait(false);
                }
            }
        }

        private async Task FailedJobAsync()
        {
            RequireFixtures();
            VideoAnalysisRequest request = await VideoRequestAsync(_Fixtures.VideoPath, false).ConfigureAwait(false);
            request.Video = await MediaSources.FromFileAsync(_Fixtures.ImagePath, "video/mp4", _Token).ConfigureAwait(false);
            try
            {
                VideoAnalysisResponse unexpected = await _User.Video.AnalyzeAndWaitAsync(request, 200, 60000, _Token).ConfigureAwait(false);
                throw new HarnessAssertionException("an image submitted as video unexpectedly succeeded with " + unexpected.Scenes.Count + " scenes");
            }
            catch (KeyframeJobException e)
            {
                Check.Equal(JobStatusEnum.Failed, e.Job.Status, "job status");
                Check.True(!String.IsNullOrEmpty(e.Job.Error), "job Error empty");
                await _User.Jobs.DeleteAsync(e.Job.Id, _Token).ConfigureAwait(false);
            }
            catch (KeyframeApiException e) when (e.StatusCode == 422 || e.StatusCode == 400)
            {
                throw new HarnessSkipException("server rejected the mismatched media at submit time (" + e.StatusCode + "), so no failed job exists");
            }
        }

        private async Task JobListAsync()
        {
            EnumerationResult<JobResponse> one = await _User.Jobs.ListAsync(new JobFilter { PageSize = 1 }, _Token).ConfigureAwait(false);
            Check.Equal(1, one.PageSize, "PageSize");
            Check.True(one.Items.Count <= 1, "more than one item on a page of 1");
            EnumerationResult<JobResponse> none = await _User.Jobs.ListAsync(new JobFilter { FromUtc = DateTime.UtcNow.AddDays(1) }, _Token).ConfigureAwait(false);
            Check.Equal(0, none.Items.Count, "future FromUtc should match nothing");
            RawResponse bogus = await _User.SendRawAsync(HttpMethod.Get, "/v1.0/jobs?status=Bogus", null, _Token).ConfigureAwait(false);
            Check.Equal(400, bogus.StatusCode, "invalid status filter");
        }

        private async Task UnknownJobAsync()
        {
            KeyframeApiException e = await Check.ThrowsApiAsync(404, () => _User.Jobs.GetAsync("kfj_doesnotexist", _Token)).ConfigureAwait(false);
            Check.Equal<ApiErrorEnum?>(ApiErrorEnum.NotFound, e.Error, "Error");
            await Check.ThrowsApiAsync(404, () => _User.Jobs.CancelAsync("kfj_doesnotexist", _Token)).ConfigureAwait(false);
            await Check.ThrowsApiAsync(404, () => _User.Jobs.DeleteAsync("kfj_doesnotexist", _Token)).ConfigureAwait(false);
        }

        #endregion

        #region Endpoints

        private async Task EndpointsAsync()
        {
            List<EndpointResponse> endpoints = await _User.Endpoints.ListAsync(_Token).ConfigureAwait(false);
            EndpointResponse? mock = endpoints.FirstOrDefault(e => e.Name == "mock");
            Check.True(mock != null, "mock endpoint missing");
            Check.Equal(ProviderTypeEnum.Mock, mock!.Provider, "Provider");
            Check.True(mock.IsDefault, "mock should be the default vision endpoint");
            Check.True(endpoints.Any(e => e.Kind == EndpointKindEnum.Transcription), "no transcription endpoint");
        }

        private async Task EndpointTestAsync()
        {
            EndpointTestResponse result = await _Admin.Endpoints.TestAsync("mock", _Token).ConfigureAwait(false);
            Check.True(result.Success, "mock endpoint test failed: " + result.Error);
            Check.Equal("mock", result.Name, "Name");
        }

        private async Task EndpointUnknownAsync()
        {
            await Check.ThrowsApiAsync(404, () => _Admin.Endpoints.TestAsync("no-such-endpoint", _Token)).ConfigureAwait(false);
        }

        #endregion

        #region Settings

        private async Task SettingsReadAsync()
        {
            SettingsResponse response = await _Admin.Settings.GetAsync(_Token).ConfigureAwait(false);
            KeyframeSettings s = response.Settings;
            Check.True(s.Auth.AdminApiKey.StartsWith("****", StringComparison.Ordinal), "admin key not masked");
            Check.True(s.Auth.AdminApiKey != _AdminKey, "admin key returned in clear");
            Check.True(s.Auth.AccessKeys.All(k => k.Key.StartsWith("****", StringComparison.Ordinal)), "access key not masked");
            Check.True(s.Inference.Endpoints.All(e => e.ApiKey == null || e.ApiKey.StartsWith("****", StringComparison.Ordinal)), "endpoint key not masked");
            Check.True(response.Fields.Any(f => f.Path == "Auth.AdminApiKey" && f.Secret), "Secret metadata missing");
            Check.True(response.Fields.Any(f => f.ApplyMode == "Restart") && response.Fields.Any(f => f.ApplyMode == "Live"), "ApplyMode metadata missing");
        }

        private async Task SettingsWriteAsync()
        {
            SettingsResponse original = await _Admin.Settings.GetAsync(_Token).ConfigureAwait(false);
            int originalDays = original.Settings.RequestHistory.RetentionDays;
            string maskedAdmin = original.Settings.Auth.AdminApiKey;
            int changedDays = originalDays >= 3650 ? originalDays - 1 : originalDays + 1;

            KeyframeSettings edited = original.Settings;
            edited.RequestHistory.RetentionDays = changedDays;
            SettingsResponse saved = await _Admin.Settings.UpdateAsync(edited, _Token).ConfigureAwait(false);
            try
            {
                Check.Equal(changedDays, saved.Settings.RequestHistory.RetentionDays, "RetentionDays after write");
                Check.Equal(maskedAdmin, saved.Settings.Auth.AdminApiKey, "masked admin key changed");
                Check.True(!saved.RestartRequired || original.RestartRequired, "a live field should not require a restart");

                WhoAmIResponse admin = await _Admin.WhoAmI.GetAsync(_Token).ConfigureAwait(false);
                Check.True(admin.IsAdmin, "admin key stopped working after the round trip");
                WhoAmIResponse user = await _User.WhoAmI.GetAsync(_Token).ConfigureAwait(false);
                Check.True(user.IsAuthenticated, "access key stopped working after the round trip");

                SettingsResponse reread = await _Admin.Settings.GetAsync(_Token).ConfigureAwait(false);
                Check.Equal(changedDays, reread.Settings.RequestHistory.RetentionDays, "RetentionDays after re-read");
            }
            finally
            {
                KeyframeSettings restore = (await _Admin.Settings.GetAsync(_Token).ConfigureAwait(false)).Settings;
                restore.RequestHistory.RetentionDays = originalDays;
                SettingsResponse restored = await _Admin.Settings.UpdateAsync(restore, _Token).ConfigureAwait(false);
                Check.Equal(originalDays, restored.Settings.RequestHistory.RetentionDays, "RetentionDays restored");
            }

            await Check.ThrowsApiAsync(400, () => _Admin.Settings.UpdateAsync(WithBadKey(original.Settings), _Token)).ConfigureAwait(false);
        }

        private static KeyframeSettings WithBadKey(KeyframeSettings source)
        {
            KeyframeSettings copy = KeyframeSerializer.Copy(source);
            copy.Auth.AccessKeys.Add(new AccessKeySettings { Name = "sdk-harness-invalid", Key = "****nope", Active = true });
            return copy;
        }

        #endregion

        #region Request history

        private async Task HistoryListAsync()
        {
            await _User.WhoAmI.GetAsync(_Token).ConfigureAwait(false);
            EnumerationResult<RequestHistoryEntry> page = await _User.RequestHistory.ListAsync(new RequestHistoryFilter { PathContains = "/v1.0/whoami", PageSize = 10 }, _Token).ConfigureAwait(false);
            Check.True(page.Items.Any(), "no whoami entries");
            Check.True(page.Items.All(e => e.Path.Contains("/v1.0/whoami")), "pathContains filter ignored");
            Check.True(page.Items.All(e => e.RequestBody == null && e.ResponseBody == null), "list should omit bodies");
            EnumerationResult<RequestHistoryEntry> failures = await _User.RequestHistory.ListAsync(new RequestHistoryFilter { StatusCode = 401, PageSize = 5 }, _Token).ConfigureAwait(false);
            Check.True(failures.Items.Any() && failures.Items.All(e => e.StatusCode == 401 && !e.Success), "statusCode filter");
        }

        private async Task HistorySummaryAsync()
        {
            RequestHistorySummary summary = await _User.RequestHistory.SummaryAsync(new RequestHistoryFilter { BucketMinutes = 30 }, _Token).ConfigureAwait(false);
            Check.Equal(30, summary.BucketMinutes, "BucketMinutes");
            Check.True(summary.Buckets.Count >= 48, "expected gap-filled buckets, got " + summary.Buckets.Count);
            Check.True(summary.TotalCount > 0 && summary.TotalCount == summary.TotalSuccess + summary.TotalFailure, "totals inconsistent");
        }

        private async Task HistoryReadAsync()
        {
            EnumerationResult<RequestHistoryEntry> page = await _User.RequestHistory.ListAsync(new RequestHistoryFilter { PathContains = "/v1.0/settings", Method = "GET", StatusCode = 200, PageSize = 1 }, _Token).ConfigureAwait(false);
            Check.True(page.Items.Any(), "no settings read entry");
            RequestHistoryEntry entry = await _User.RequestHistory.GetAsync(page.Items[0].Id, _Token).ConfigureAwait(false);
            Check.Equal(page.Items[0].Id, entry.Id, "Id");
            Check.True(entry.RequestHeaders != null && entry.RequestHeaders.Any(), "headers missing");
            string headerText = String.Join("\n", entry.RequestHeaders!.Select(kvp => kvp.Key + ": " + kvp.Value));
            Check.True(!headerText.Contains(_AdminKey), "admin key stored in request history headers");
            Check.True(entry.ResponseBody == null || !entry.ResponseBody.Contains("\"" + _AdminKey + "\""), "admin key stored in response body");
            await Check.ThrowsApiAsync(404, () => _User.RequestHistory.GetAsync("req_doesnotexist", _Token)).ConfigureAwait(false);
        }

        private async Task HistoryDeleteAsync()
        {
            EnumerationResult<RequestHistoryEntry> page = await _Admin.RequestHistory.ListAsync(new RequestHistoryFilter { PathContains = "/openapi.json", PageSize = 1 }, _Token).ConfigureAwait(false);
            if (!page.Items.Any()) throw new HarnessSkipException("no /openapi.json entry to delete");
            string id = page.Items[0].Id;
            await _Admin.RequestHistory.DeleteAsync(id, _Token).ConfigureAwait(false);
            await Check.ThrowsApiAsync(404, () => _Admin.RequestHistory.GetAsync(id, _Token)).ConfigureAwait(false);
            await Check.ThrowsApiAsync(404, () => _Admin.RequestHistory.DeleteAsync(id, _Token)).ConfigureAwait(false);
        }

        private async Task HistoryBulkDeleteAsync()
        {
            DeleteResult result = await _Admin.RequestHistory.DeleteManyAsync(new RequestHistoryFilter { PathContains = "/no-such-path-" + Guid.NewGuid().ToString("N") }, _Token).ConfigureAwait(false);
            Check.Equal(0, result.DeletedCount, "DeletedCount");
        }

        #endregion

        private void RequireFixtures()
        {
            if (!_Fixtures.Available) throw new HarnessSkipException(_Fixtures.Reason ?? "fixtures unavailable");
        }
    }
}
