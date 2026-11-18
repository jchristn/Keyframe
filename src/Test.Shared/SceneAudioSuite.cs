namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Video scene audio: defaults, the scene listening plan (never a rejection), per-scene clips on an audio-capable
    /// vision endpoint, and the NotRequested, NoAudioTrack, and NoModelFunctions cases.
    /// </summary>
    public static class SceneAudioSuite
    {
        private const string Id = "SceneAudio";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Video scene audio", new[]
            {
                new TestCaseDescriptor(Id, "Defaults", "Scene audio defaults on with null rendition members and parses from JSON", ct =>
                {
                    ScenesSpec spec = new ScenesSpec();
                    Check.True(spec.Audio.Enable, "on by default");
                    Check.True(spec.Audio.Rendition.Format == null && spec.Audio.Rendition.Channels == null, "rendition defaults");
                    spec.Audio = null!;
                    Check.True(spec.Audio != null && spec.Audio.Enable, "null becomes default");
                    VideoAnalysisRequest parsed = KeyframeSerializer.Deserialize<VideoAnalysisRequest>(
                        "{\"Video\":{\"Url\":\"https://example.com/v.mp4\"},\"Functions\":{\"Scenes\":{\"Enable\":true,\"Audio\":{\"Enable\":false,\"Rendition\":{\"Channels\":2}}}}}")!;
                    Check.True(!parsed.Functions.Scenes.Audio.Enable && parsed.Functions.Scenes.Audio.Rendition.Channels == 2, "parsed");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Planner", "Scene listening falls back for incapable, Flac-refusing, unhealthy, and silent cases and never rejects", ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    FakeHealthLookup health = new FakeHealthLookup();
                    EndpointSettings ollama = new EndpointSettings { Name = "ollama", Provider = ProviderTypeEnum.Ollama, Kind = EndpointKindEnum.Completion };
                    EndpointSettings compat = new EndpointSettings { Name = "compat", Provider = ProviderTypeEnum.OpenAiCompatible, Kind = EndpointKindEnum.Completion };
                    EndpointSettings mock = new EndpointSettings { Name = "mock", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion };

                    ListenPlan a = ListenPlanner.ResolveScenes(true, null, ollama, true, settings, health);
                    Check.True(a.Reason == ListenSkipReasonEnum.EndpointLacksAudioInput && a.Rejection == null, "Ollama vision cannot listen");
                    ListenPlan b = ListenPlanner.ResolveScenes(true, new AudioRenditionSpec { Format = ListenFormatEnum.Flac }, compat, true, settings, health);
                    Check.True(b.Reason == ListenSkipReasonEnum.EndpointLacksAudioInput && b.Rejection == null, "Flac to OpenAI-compatible falls back");
                    Check.True(ListenPlanner.ResolveScenes(true, null, compat, true, settings, health).Listens, "OpenAI-compatible listens");
                    Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoAudioTrack, ListenPlanner.ResolveScenes(true, null, mock, false, settings, health).Reason, "silent video");
                    Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NotRequested, ListenPlanner.ResolveScenes(false, null, mock, true, settings, health).Reason, "not requested");
                    health.Set("mock", false);
                    Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.EndpointUnhealthy, ListenPlanner.ResolveScenes(true, null, mock, true, settings, health).Reason, "unhealthy");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ScenesListen", "With an audio-capable vision endpoint each scene call carries that scene's audio", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        VideoAnalysisRequest request = ScenesRequest(await TestEnvironment.Base64Async(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false));
                        VideoAnalysisResponse response = await new VideoAnalyzer(services).AnalyzeAsync(request, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        ListeningInfo l = response.Listening!;
                        Check.True(l.Listened && l.Mode == ListeningModeEnum.Scenes && l.Endpoint == "mock", "scenes listened");
                        Check.Equal(response.Scenes.Count, l.Clips.Count, "one clip per scene");
                        Check.True(l.Clips.All(c => c.Scope == ListenClipScopeEnum.Scene) && l.Clips.Select(c => c.Index).SequenceEqual(response.Scenes.Select(s => (int?)s.Index)), "scene clips in order");
                        Check.True(l.Clips.Zip(response.Scenes, (c, s) => c.StartMs == s.StartMs && c.EndMs == s.EndMs).All(x => x), "each clip covers its scene");
                        List<ModelCallTelemetry> calls = response.Telemetry.ModelCalls.Where(c => c.Purpose == ModelCallPurposeEnum.SceneSummarize).ToList();
                        Check.True(calls.Count == response.Scenes.Count && calls.All(c => c.InputAudioBytes > 0 && c.InputImages > 0), "frames and audio on every scene call");
                        Check.True(response.Scenes.All(s => (s.Results["Summary"] as string ?? "").Contains("1 audio clip")), "mock heard one clip per scene");

                        VideoAnalysisRequest off = ScenesRequest(request.Video!.Data!);
                        off.Functions.Scenes.Audio.Enable = false;
                        VideoAnalysisResponse quiet = await new VideoAnalyzer(services).AnalyzeAsync(off, null, ct).ConfigureAwait(false);
                        Check.True(quiet.Listening != null && !quiet.Listening.Listened && quiet.Listening.Reason == ListenSkipReasonEnum.NotRequested, "not requested");
                        Check.True(quiet.Telemetry.ModelCalls.Where(c => c.Purpose != ModelCallPurposeEnum.Transcribe).All(c => c.InputAudioBytes == 0), "no audio sent");

                        VideoAnalysisRequest noScenes = new VideoAnalysisRequest { Video = request.Video };
                        noScenes.Functions.Summarize.Enable = true;
                        VideoAnalysisResponse plain = await new VideoAnalyzer(services).AnalyzeAsync(noScenes, null, ct).ConfigureAwait(false);
                        Check.Equal(null, plain.Listening, "no Listening without scenes");

                        VideoAnalysisRequest bare = new VideoAnalysisRequest { Video = request.Video };
                        bare.Functions.Scenes.Enable = true;
                        VideoAnalysisResponse boundariesOnly = await new VideoAnalyzer(services).AnalyzeAsync(bare, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoModelFunctions, boundariesOnly.Listening!.Reason, "scenes without scene functions");
                    }
                }, new[] { "ffmpeg", "slow" }, skip),

                new TestCaseDescriptor(Id, "SilentVideo", "A silent video reports NoAudioTrack and still analyzes scenes from frames", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        VideoAnalysisRequest request = ScenesRequest(await TestEnvironment.Base64Async(await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false));
                        request.Functions.Transcribe.Enable = false;
                        VideoAnalysisResponse response = await new VideoAnalyzer(services).AnalyzeAsync(request, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoAudioTrack, response.Listening!.Reason, "no audio track");
                        Check.True(response.Scenes.Count > 0 && response.Scenes.All(s => s.Results.ContainsKey("Summary")), "scenes still analyzed");
                    }
                }, new[] { "ffmpeg" }, skip)
            });
        }

        private static VideoAnalysisRequest ScenesRequest(string data)
        {
            VideoAnalysisRequest request = new VideoAnalysisRequest { Video = new MediaSource { Data = data } };
            request.Functions.Scenes.Enable = true;
            request.Functions.Scenes.FramesPerScene = 1;
            request.Functions.Scenes.Summarize.Enable = true;
            return request;
        }
    }
}
