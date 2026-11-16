namespace Test.Shared
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// End-to-end image and video analysis with the mock provider.
    /// </summary>
    public static class AnalysisSuite
    {
        private const string Id = "Analysis";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Analysis engine", new[]
            {
                new TestCaseDescriptor(Id, "ImageEndToEnd", "Image summary and typed fields with telemetry", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        ImageAnalysisRequest request = await ImageRequestAsync(ct).ConfigureAwait(false);
                        ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);

                        Check.Equal(true, response.Success, "success: " + string.Join(" ", response.Errors));
                        Check.True(response.Results["Summary"] is string s && s.StartsWith("Mock summary"), "summary");
                        Check.True(response.Results["hasCats"] is bool, "boolean typed");
                        Check.True(response.Results["dogCount"] is long n && n >= 0 && n <= 5, "integer typed and in range");
                        Check.True(response.Results["setting"] is string e && (e == "Indoors" || e == "Outdoors"), "enum");
                        Check.Equal(4, response.Fields.Count, "field details");
                        Check.True(response.Properties != null && response.Properties.Width == 1280, "properties");
                        Check.Equal(2, response.Telemetry.ModelCalls.Count, "summary plus one combined extract call");
                        Check.True(response.Telemetry.Totals.TotalTokens > 0, "token totals");
                        Check.True(response.Telemetry.Stages.Any(st => st.Name == "Normalize"), "normalize stage timed");
                        Check.True(response.Telemetry.ModelCalls.All(c => c.InputImages == 1 && c.InputImageBytes > 0), "image counted");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "PerFieldMode", "Per-field mode makes one call per field", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        ImageAnalysisRequest request = await ImageRequestAsync(ct).ConfigureAwait(false);
                        request.Functions.Summarize.Enable = false;
                        request.Functions.Extract.Mode = ExtractModeEnum.PerField;
                        ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);
                        Check.Equal(4, response.Telemetry.ModelCalls.Count, "one call per field");
                        Check.True(response.Telemetry.ModelCalls.All(c => c.Target != null), "targets recorded");
                        Check.Equal(true, response.Success, "success");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "RetryAndRepair", "Transient failures retry and malformed JSON is repaired", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MockInferenceClient mock = MockFor(services, settings);
                        mock.FailNextTransient(1);
                        mock.MalformNext(1);

                        ImageAnalysisRequest request = await ImageRequestAsync(ct).ConfigureAwait(false);
                        request.Functions.Summarize.Enable = false;
                        ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);

                        ModelCallTelemetry call = response.Telemetry.ModelCalls.Single();
                        Check.Equal(true, response.Success, "recovered: " + string.Join(" ", response.Errors));
                        Check.Equal(3, call.Attempts, "transient + malformed + good");
                        Check.Equal(1, call.RepairAttempts, "one repair");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "PermanentFailure", "A failed call reports errors and keeps field defaults", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MockFor(services, settings).FailNextPermanent(1);
                        ImageAnalysisRequest request = await ImageRequestAsync(ct).ConfigureAwait(false);
                        request.Functions.Summarize.Enable = false;
                        request.Functions.Extract.Fields[1].Default = 0;
                        ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);
                        Check.Equal(false, response.Success, "failed");
                        Check.True(response.Errors.Count > 0, "error reported");
                        Check.Equal((object)0L, response.Results["dogCount"], "default applied");
                        Check.Equal(1, response.Telemetry.Totals.FailedModelCalls, "failure counted");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "VideoScenes", "Video scenes, transcript, and whole-video results", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    settings.Media.TranscriptionChunkSeconds = 10;
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        string mp4 = await TestEnvironment.VideoAsync(ct).ConfigureAwait(false);
                        VideoAnalysisRequest request = new VideoAnalysisRequest
                        {
                            Video = new MediaSource { Data = await TestEnvironment.Base64Async(mp4, ct).ConfigureAwait(false) },
                            Context = new List<ContextItem> { new ContextItem { Label = "Event", Text = "Synthetic test footage." } },
                            IncludeKeyframeData = true
                        };
                        request.Functions.Summarize.Enable = true;
                        request.Functions.Extract.Enable = true;
                        request.Functions.Extract.Fields.Add(new FieldSpec { Key = "topics", Type = FieldTypeEnum.StringList, Prompt = "Topics?" });
                        request.Functions.Scenes.Enable = true;
                        request.Functions.Scenes.FramesPerScene = 2;
                        request.Functions.Scenes.Summarize.Enable = true;
                        request.Functions.Scenes.Extract.Enable = true;
                        request.Functions.Scenes.Extract.Fields.Add(new FieldSpec { Key = "isRed", Type = FieldTypeEnum.Boolean, Prompt = "Is the frame red?" });
                        request.Functions.Transcribe.Enable = true;

                        List<JobProgress> progress = new List<JobProgress>();
                        VideoAnalysisResponse response = await new VideoAnalyzer(services).AnalyzeAsync(request, p => { lock (progress) progress.Add(p); }, ct).ConfigureAwait(false);

                        Check.Equal(true, response.Success, "success: " + string.Join(" ", response.Errors));
                        Check.Equal(3, response.Scenes.Count, "three scenes");
                        Check.True(response.Scenes.All(s => s.Keyframes.Count == 2 && s.Keyframes.All(k => k.Data != null)), "keyframes with thumbnails");
                        Check.True(response.Scenes.All(s => s.Results.ContainsKey("Summary") && s.Results["isRed"] is bool), "scene results");
                        Check.True(response.Transcript != null && response.Transcript.Segments.Count > 0, "transcript");
                        Check.True(response.Scenes.All(s => !string.IsNullOrEmpty(s.TranscriptText)), "transcript sliced per scene");
                        Check.True(response.Results["Summary"] is string, "video summary");
                        Check.True(response.Results["topics"] is List<string>, "video field");
                        Check.Equal(3, response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.SceneSummarize), "scene summaries");
                        Check.Equal(2, response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.Transcribe), "two 10s audio chunks for 12s of audio");
                        Check.True(response.Telemetry.ModelCalls.Single(c => c.Purpose == ModelCallPurposeEnum.Summarize).InputImages > 0, "video summary sees frames");
                        Check.True(progress.Count > 0 && progress.Last().Percent == 100, "progress reported to 100");
                    }
                }, new[] { "ffmpeg", "slow" }, skip),

                new TestCaseDescriptor(Id, "SilentVideo", "Transcription is skipped with a warning when there is no audio", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        string mp4 = await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false);
                        VideoAnalysisRequest request = new VideoAnalysisRequest { Video = new MediaSource { Data = await TestEnvironment.Base64Async(mp4, ct).ConfigureAwait(false) } };
                        request.Functions.Transcribe.Enable = true;
                        request.Functions.Summarize.Enable = true;
                        request.SampleFrames = 3;
                        VideoAnalysisResponse response = await new VideoAnalyzer(services).AnalyzeAsync(request, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success");
                        Check.Equal(null, response.Transcript, "no transcript");
                        Check.True(response.Warnings.Any(w => w.Contains("no audio")), "warning");
                        Check.Equal(3, response.Telemetry.ModelCalls.Single().InputImages, "sampled frames");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "UnknownEndpoint", "An unknown endpoint is rejected before media work", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        ImageAnalysisRequest request = new ImageAnalysisRequest
                        {
                            Image = new MediaSource { Url = "https://example.com/a.jpg" },
                            Model = new ModelSelection { Endpoint = "nope" }
                        };
                        request.Functions.Summarize.Enable = true;
                        bool threw = false;
                        try
                        {
                            await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);
                        }
                        catch (Keyframe.Core.Exceptions.EndpointNotFoundException)
                        {
                            threw = true;
                        }

                        Check.True(threw, "EndpointNotFoundException");
                    }
                }, new[] { "fast" })
            });
        }

        private static async Task<ImageAnalysisRequest> ImageRequestAsync(System.Threading.CancellationToken ct)
        {
            string png = await TestEnvironment.PngAsync(ct).ConfigureAwait(false);
            ImageAnalysisRequest request = new ImageAnalysisRequest
            {
                Image = new MediaSource { Data = await TestEnvironment.Base64Async(png, ct).ConfigureAwait(false) },
                Context = new List<ContextItem> { new ContextItem { Text = "Generated test image." } }
            };
            request.Functions.Summarize.Enable = true;
            request.Functions.Extract.Enable = true;
            request.Functions.Extract.Fields = new List<FieldSpec>
            {
                new FieldSpec { Key = "hasCats", Type = FieldTypeEnum.Boolean, Prompt = "Does this image have cats?" },
                new FieldSpec { Key = "dogCount", Type = FieldTypeEnum.Integer, Prompt = "How many dogs?", Minimum = 0, Maximum = 5 },
                new FieldSpec { Key = "setting", Type = FieldTypeEnum.Enum, Prompt = "Where?", Options = new List<string> { "Indoors", "Outdoors" } },
                new FieldSpec { Key = "colors", Type = FieldTypeEnum.StringList, Prompt = "Colors?", MaxItems = 5 }
            };
            return request;
        }

        private static MockInferenceClient MockFor(AnalysisServices services, KeyframeSettings settings)
        {
            EndpointSettings endpoint = services.Clients.ResolveCompletionEndpoint(null, settings.Inference.DefaultVision);
            GatedInferenceClient gated = (GatedInferenceClient)services.Clients.GetCompletionClient(endpoint);
            return (MockInferenceClient)gated.Inner;
        }
    }
}
