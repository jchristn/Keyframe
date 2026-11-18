namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Media;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Audio sections: the shared boundary builder, silence cut parsing, detection on generated tones, per-section
    /// functions on the transcript and by listening, the text-model reduce, section clips over the listen budget, and
    /// validation.
    /// </summary>
    public static class SectionSuite
    {
        private const string Id = "Sections";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Audio sections", new[]
            {
                new TestCaseDescriptor(Id, "BoundaryBuilder", "Scenes built through the shared builder match the direct builder, and section specs clamp", ct =>
                {
                    SceneDetectionSpec scene = new SceneDetectionSpec { MinSceneMs = 1000, MaxSceneMs = 10000, MaxScenes = 5 };
                    List<long> cuts = new List<long> { 300, 4000, 4100, 9000, 25000 };
                    List<SceneBoundary> a = SceneDetector.BuildScenes(cuts, 30000, scene);
                    List<SceneBoundary> b = BoundaryBuilder.Build(cuts, 30000, 1000, 10000, 5, 250);
                    Check.True(a.Count == b.Count && a.Zip(b, (x, y) => x.StartMs == y.StartMs && x.EndMs == y.EndMs).All(z => z), "same boundaries");
                    Check.True(a.First().StartMs == 0 && a.Last().EndMs == 30000, "covers the recording");

                    SectionDetectionSpec spec = new SectionDetectionSpec { SilenceDb = 0, MinSilenceMs = 1, MinSectionMs = -5, MaxSectionMs = 1, MaxSections = 0 };
                    Check.True(spec.SilenceDb == -10 && spec.MinSilenceMs == 100 && spec.MinSectionMs == 0 && spec.MaxSectionMs == 10000 && spec.MaxSections == 1, "minimums");
                    SectionDetectionSpec fresh = new SectionDetectionSpec();
                    Check.True(fresh.SilenceDb == -40 && fresh.MinSilenceMs == 1500 && fresh.MinSectionMs == 20000 && fresh.MaxSectionMs == 600000 && fresh.MaxSections == 100, "defaults");
                    Check.True(!new AudioAnalysisRequest().Functions.Sections.Enable, "sections off by default");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ParseCuts", "Silences inside the recording become midpoint cuts; silences at either end do not", ct =>
                {
                    List<long> cuts = SectionDetector.ParseCuts(TestEnvironment.ReadData("Ffmpeg/measure-ffmpeg6.txt"), 17000);
                    Check.Equal(1, cuts.Count, "the leading silence is not a cut");
                    Check.True(Math.Abs(cuts[0] - 10000) <= 1, "midpoint of 9 s to 11 s: " + cuts[0]);
                    Check.Equal(0, SectionDetector.ParseCuts(TestEnvironment.ReadData("Ffmpeg/mono-trailing-ffmpeg9.txt"), 7000).Count, "a trailing silence is not a cut");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Validation", "Section functions need Sections.Enable and a consistent detection range", ct =>
                {
                    AudioAnalysisRequest r = new AudioAnalysisRequest { Audio = new MediaSource { Url = "https://example.com/a.mp3" } };
                    r.Functions.Sections.Summarize.Enable = true;
                    Check.True(RequestValidator.Validate(r).Any(e => e.Contains("require Functions.Sections.Enable")), "section functions need sections");
                    r.Functions.Sections.Enable = true;
                    Check.Equal(0, RequestValidator.Validate(r).Count, "valid with sections on");
                    r.Functions.Sections.Detection.MinSectionMs = 600000;
                    r.Functions.Sections.Detection.MaxSectionMs = 10000;
                    Check.True(RequestValidator.Validate(r).Any(e => e.Contains("MaxSectionMs must be at least MinSectionMs")), "range checked");
                    r.Functions.Sections.Detection.MaxSectionMs = 600000;
                    r.Functions.Sections.Extract.Enable = true;
                    Check.True(RequestValidator.Validate(r).Any(e => e.StartsWith("Functions.Sections.Extract.Fields")), "section fields validated");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Detect", "Three tones separated by silences yield three sections cut within 250 ms of the silence midpoints", async ct =>
                {
                    SectionDetector detector = new SectionDetector(new FfmpegRunner());
                    List<SceneBoundary> sections = await detector.DetectAsync(await TestEnvironment.TonesWithSilencesAsync(ct).ConfigureAwait(false), 96000, new SectionDetectionSpec(), ct).ConfigureAwait(false);
                    Check.Equal(3, sections.Count, "three sections");
                    Check.True(Math.Abs(sections[0].EndMs - 31500) <= 250 && Math.Abs(sections[1].EndMs - 64500) <= 250, "cuts at 31.5 s and 64.5 s: " + sections[0].EndMs + ", " + sections[1].EndMs);
                    Check.True(sections[0].StartMs == 0 && sections[2].EndMs == 96000, "covers the recording");
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "TranscriptPath", "Without listening, sections run on transcript slices and the whole recording on the full transcript", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = SectionsRequest(await TestEnvironment.Base64Async(await TestEnvironment.TonesWithSilencesAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false));
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.True(response.Sections != null && response.Sections.Count == 3, "three sections");
                        Check.True(response.Sections!.All(s => !String.IsNullOrWhiteSpace(s.TranscriptText)), "transcript slices");
                        Check.True(response.Sections!.All(s => s.Results["Summary"] is string && s.Results.ContainsKey("mood")), "per-section summary and field");
                        Check.Equal(3, response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.SectionSummarize), "three section summaries");
                        Check.Equal(3, response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.SectionExtract), "three section extracts");
                        Check.True(response.Telemetry.ModelCalls.Where(c => c.Purpose == ModelCallPurposeEnum.SectionSummarize).Select(c => c.Target).OrderBy(t => t).SequenceEqual(new[] { "section:0", "section:1", "section:2" }), "section targets");
                        Check.True(response.Results["Summary"] is string, "whole-recording summary");
                        Check.True(response.Telemetry.Stages.Any(s => s.Name == "SectionDetection") && response.Telemetry.Stages.Any(s => s.Name == "Sections"), "stages timed");
                        Check.True(!response.Listening.Listened, "not listened");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "ListeningAndReduce", "Listening sections hear their own clip; with TextModel the whole recording is reduced from section results", async ct =>
                {
                    KeyframeSettings settings = ListenSuite.ListeningSettings();
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "mock-text", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "mock-llm", MaxConcurrentRequests = 4 });
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        string data = await TestEnvironment.Base64Async(await TestEnvironment.TonesWithSilencesAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        AudioAnalysisRequest request = SectionsRequest(data);
                        request.TextModel = new ModelSelection { Endpoint = "mock-text" };
                        new AudioAnalyzer(services).Preflight(request);
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        ListeningInfo l = response.Listening;
                        Check.True(l.Listened && l.Mode == ListeningModeEnum.Sections, "mode Sections");
                        Check.Equal(3, l.Clips.Count, "one clip per section, no recording clip");
                        Check.True(l.Clips.All(c => c.Scope == ListenClipScopeEnum.Section) && l.Clips.Select(c => c.Index).SequenceEqual(new int?[] { 0, 1, 2 }), "section clips in order");
                        Check.True(l.Clips.Zip(response.Sections!, (c, s) => c.StartMs == s.StartMs && c.EndMs == s.EndMs).All(x => x), "each clip is its whole section");
                        Check.Equal(96000L, l.HeardMs, "heard everything once");
                        Check.True(response.Telemetry.ModelCalls.Where(c => c.Purpose == ModelCallPurposeEnum.SectionSummarize).All(c => c.Endpoint == "mock-listen" && c.InputAudioBytes > 0), "sections listened");
                        ModelCallTelemetry whole = response.Telemetry.ModelCalls.Single(c => c.Purpose == ModelCallPurposeEnum.Summarize);
                        Check.True(whole.Endpoint == "mock-text" && whole.InputAudioBytes == 0, "whole summary reduced on the text model");
                        Check.True(response.Sections!.All(s => (s.Results["Summary"] as string ?? "").Contains("1 audio clip")), "each section heard one clip");

                        AudioAnalysisRequest noReduce = SectionsRequest(data);
                        AudioAnalysisResponse full = await new AudioAnalyzer(services).AnalyzeAsync(noReduce, null, null, ct).ConfigureAwait(false);
                        Check.Equal(ListeningModeEnum.Full, full.Listening.Mode, "without TextModel the whole recording is heard");
                        Check.Equal(1, full.Listening.Clips.Count(c => c.Scope == ListenClipScopeEnum.Recording), "one recording clip");
                        Check.Equal(3, full.Listening.Clips.Count(c => c.Scope == ListenClipScopeEnum.Section), "plus three section clips");
                        Check.Equal(96000L, full.Listening.HeardMs, "overlapping clips counted once");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "SectionBudget", "A section longer than the listen budget sends a centered excerpt", async ct =>
                {
                    KeyframeSettings settings = ListenSuite.ListeningSettings();
                    settings.Inference.Endpoints.Single(e => e.Name == "mock-listen").MaxListenSeconds = 10;
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = SectionsRequest(await TestEnvironment.Base64Async(await TestEnvironment.TonesWithSilencesAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false));
                        request.Functions.Summarize.Enable = false;
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.Equal(ListeningModeEnum.Sections, response.Listening.Mode, "only section functions ran");
                        foreach (SectionResult section in response.Sections!)
                        {
                            ListenClip clip = response.Listening.Clips.Single(c => c.Index == section.Index);
                            Check.Equal(10000L, clip.EndMs - clip.StartMs, "clip fits the budget");
                            long expectedStart = section.StartMs + (section.DurationMs - 10000) / 2;
                            Check.True(Math.Abs(clip.StartMs - expectedStart) <= 1, "centered");
                        }
                    }
                }, new[] { "ffmpeg" }, skip)
            });
        }

        private static AudioAnalysisRequest SectionsRequest(string data)
        {
            AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = data } };
            request.Functions.Sections.Enable = true;
            request.Functions.Sections.Summarize.Enable = true;
            request.Functions.Sections.Extract.Enable = true;
            request.Functions.Sections.Extract.Fields.Add(new FieldSpec { Key = "mood", Type = FieldTypeEnum.String, Prompt = "Mood of the section?" });
            return request;
        }
    }
}
