namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Inference.Providers;
    using Keyframe.Core.Media;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Keyframe.Server.Services;
    using SyslogLogging;
    using Touchstone.Core;

    /// <summary>
    /// Listening to audio: request and settings defaults and clamps, rendition resolution and extraction, excerpt
    /// sampling, the listening plan (fallbacks, rejections, health, run-time resolution), the Full and Excerpts modes,
    /// inline size limits, listening failures, and the OpenAI-compatible and Ollama wire behavior.
    /// </summary>
    public static class ListenSuite
    {
        private const string Id = "Listen";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Listening to audio", new[]
            {
                new TestCaseDescriptor(Id, "RequestDefaults", "Listen defaults on with null rendition members, and every new member clamps", ct =>
                {
                    AudioAnalysisRequest fresh = new AudioAnalysisRequest();
                    Check.True(fresh.Listen.Enable, "Listen on by default");
                    Check.True(fresh.Listen.Model == null, "no listening model by default");
                    Check.True(fresh.Listen.Rendition.Format == null && fresh.Listen.Rendition.SampleRate == null && fresh.Listen.Rendition.Channels == null && fresh.Listen.Rendition.BitrateKbps == null, "rendition members null");
                    Check.Equal(4, fresh.Listen.SampleClips, "sample clips");
                    Check.Equal(30, fresh.Listen.ClipSeconds, "clip seconds");

                    ListenSpec spec = new ListenSpec { SampleClips = 0, ClipSeconds = 1 };
                    Check.Equal(1, spec.SampleClips, "sample clips minimum");
                    Check.Equal(5, spec.ClipSeconds, "clip seconds minimum");
                    spec.SampleClips = 99;
                    spec.ClipSeconds = 9999;
                    Check.Equal(32, spec.SampleClips, "sample clips maximum");
                    Check.Equal(300, spec.ClipSeconds, "clip seconds maximum");
                    spec.Rendition = null!;
                    Check.True(spec.Rendition != null, "null rendition becomes empty");

                    AudioRenditionSpec r = new AudioRenditionSpec { SampleRate = 1000, Channels = 0, BitrateKbps = 1 };
                    Check.Equal(8000, r.SampleRate, "sample rate minimum");
                    Check.Equal(1, r.Channels, "channels minimum");
                    Check.Equal(16, r.BitrateKbps, "bitrate minimum");
                    r.SampleRate = 96000;
                    r.Channels = 6;
                    r.BitrateKbps = 999;
                    Check.Equal(48000, r.SampleRate, "sample rate maximum");
                    Check.Equal(2, r.Channels, "channels maximum");
                    Check.Equal(320, r.BitrateKbps, "bitrate maximum");

                    AudioAnalysisRequest parsed = Keyframe.Core.Serialization.KeyframeSerializer.Deserialize<AudioAnalysisRequest>(
                        "{\"Audio\":{\"Url\":\"https://example.com/a.mp3\"},\"Listen\":{\"Enable\":false,\"Rendition\":{\"Format\":\"Wav\",\"Channels\":2}}}")!;
                    Check.True(!parsed.Listen.Enable, "Enable parsed");
                    Check.Equal((ListenFormatEnum?)ListenFormatEnum.Wav, parsed.Listen.Rendition.Format, "format parsed as a string enum");
                    Check.Equal((int?)2, parsed.Listen.Rendition.Channels, "channels parsed");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Validation", "Summarize and Extract need Transcribe or Listen", ct =>
                {
                    AudioAnalysisRequest listenOnly = new AudioAnalysisRequest { Audio = new MediaSource { Url = "https://example.com/a.mp3" } };
                    listenOnly.Functions.Transcribe.Enable = false;
                    Check.Equal(0, RequestValidator.Validate(listenOnly).Count, "listening alone is enough evidence");

                    listenOnly.Listen.Enable = false;
                    Check.True(RequestValidator.Validate(listenOnly).Any(e => e.Contains("enable Functions.Transcribe or Listen")), "no evidence at all is rejected");

                    AudioAnalysisRequest badModel = new AudioAnalysisRequest { Audio = new MediaSource { Url = "https://example.com/a.mp3" } };
                    badModel.Listen.Model = new ModelSelection { Endpoint = "" };
                    Check.True(RequestValidator.Validate(badModel).Any(e => e.StartsWith("Listen.Model.Endpoint")), "listen model validated");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "SettingsDefaults", "Listening, measurement, and probe settings default and clamp as documented", ct =>
                {
                    KeyframeSettings s = new KeyframeSettings();
                    Check.Equal(ListenFormatEnum.Mp3, s.Media.ListenFormat, "format");
                    Check.Equal(16000, s.Media.ListenSampleRate, "sample rate");
                    Check.Equal(1, s.Media.ListenChannels, "channels");
                    Check.Equal(64, s.Media.ListenBitrateKbps, "bitrate");
                    Check.Equal(1200, s.Media.MaxListenSeconds, "listen budget");
                    Check.Equal(-50.0, s.Media.MeasureSilenceDb, "measure silence");
                    Check.Equal(500, s.Media.MeasureMinSilenceMs, "measure min silence");
                    Check.Equal(-0.1, s.Media.ClippingTruePeakDbtp, "clipping true peak");
                    Check.Equal(10.0, s.Media.ClippingMinPeaksPerMinute, "clipping peaks");
                    Check.Equal(-40.0, s.Media.MonoMaxSideDb, "mono threshold");
                    Check.Equal(null, s.Inference.DefaultListening, "no default listening endpoint in code");
                    Check.Equal(300000, s.Inference.HealthProbeIntervalMs, "healthy probe interval");
                    Check.Equal(15000, s.Inference.UnhealthyProbeIntervalMs, "unhealthy probe interval");

                    s.Media.ListenSampleRate = 1;
                    s.Media.ListenChannels = 9;
                    s.Media.ListenBitrateKbps = 1000;
                    s.Media.MaxListenSeconds = 1;
                    s.Media.MeasureSilenceDb = 0;
                    s.Media.MeasureMinSilenceMs = 1;
                    s.Media.ClippingTruePeakDbtp = -50;
                    s.Media.ClippingMinPeaksPerMinute = -1;
                    s.Media.MonoMaxSideDb = 10;
                    s.Inference.HealthProbeIntervalMs = 1;
                    s.Inference.UnhealthyProbeIntervalMs = 99999999;
                    Check.Equal(8000, s.Media.ListenSampleRate, "sample rate clamps");
                    Check.Equal(2, s.Media.ListenChannels, "channels clamp");
                    Check.Equal(320, s.Media.ListenBitrateKbps, "bitrate clamps");
                    Check.Equal(10, s.Media.MaxListenSeconds, "budget clamps");
                    Check.Equal(-10.0, s.Media.MeasureSilenceDb, "silence clamps");
                    Check.Equal(100, s.Media.MeasureMinSilenceMs, "min silence clamps");
                    Check.Equal(-6.0, s.Media.ClippingTruePeakDbtp, "true peak clamps");
                    Check.Equal(0.0, s.Media.ClippingMinPeaksPerMinute, "peaks clamp");
                    Check.Equal(0.0, s.Media.MonoMaxSideDb, "mono threshold clamps");
                    Check.Equal(10000, s.Inference.HealthProbeIntervalMs, "healthy interval clamps");
                    Check.Equal(600000, s.Inference.UnhealthyProbeIntervalMs, "unhealthy interval clamps");

                    EndpointSettings e = new EndpointSettings();
                    Check.Equal(20971520L, e.MaxInlineMediaBytes, "inline default");
                    Check.Equal(null, e.MaxListenSeconds, "endpoint budget default");
                    e.MaxInlineMediaBytes = 1;
                    e.MaxListenSeconds = 1;
                    Check.Equal(1048576L, e.MaxInlineMediaBytes, "inline clamps");
                    Check.Equal((int?)10, e.MaxListenSeconds, "endpoint budget clamps");
                    e.MaxListenSeconds = null;
                    Check.Equal(null, e.MaxListenSeconds, "endpoint budget can be cleared");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "RenditionResolver", "Request rendition values override Media.Listen* defaults and nulls fall through", ct =>
                {
                    MediaSettings media = new MediaSettings { ListenFormat = ListenFormatEnum.Wav, ListenSampleRate = 22050, ListenChannels = 2, ListenBitrateKbps = 96 };
                    AudioRenditionSpec all = RenditionResolver.Resolve(null, media);
                    Check.True(all.Format == ListenFormatEnum.Wav && all.SampleRate == 22050 && all.Channels == 2 && all.BitrateKbps == 96, "defaults fill every member");
                    AudioRenditionSpec mixed = RenditionResolver.Resolve(new AudioRenditionSpec { Format = ListenFormatEnum.Mp3, Channels = 1 }, media);
                    Check.True(mixed.Format == ListenFormatEnum.Mp3 && mixed.Channels == 1 && mixed.SampleRate == 22050 && mixed.BitrateKbps == 96, "request wins, nulls fall through");
                    Check.Equal("audio/mpeg", RenditionResolver.MediaType(ListenFormatEnum.Mp3), "mp3 media type");
                    Check.Equal("audio/wav", RenditionResolver.MediaType(ListenFormatEnum.Wav), "wav media type");
                    Check.Equal("audio/flac", RenditionResolver.MediaType(ListenFormatEnum.Flac), "flac media type");
                    Check.True(RenditionResolver.EstimateBytes(new AudioRenditionSpec { Format = ListenFormatEnum.Mp3, BitrateKbps = 64 }, 10000) >= 80000, "mp3 estimate from bitrate");
                    Check.True(RenditionResolver.EstimateBytes(new AudioRenditionSpec { Format = ListenFormatEnum.Wav, SampleRate = 16000, Channels = 1 }, 10000) >= 320000, "wav estimate from PCM");
                    Check.Equal(1400L, RenditionResolver.WireBytes(1000), "1000 bytes encode to 1336 base64 characters plus 64");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ClipSampler", "Excerpts are evenly spaced, never overlap, and collapse to the whole recording when they would cover it", ct =>
                {
                    List<ClipWindow> four = ClipSampler.Evenly(100000, 4, 10000);
                    Check.Equal(4, four.Count, "four windows");
                    Check.Equal(0L, four[0].StartMs, "first at the start");
                    Check.Equal(100000L, four[3].EndMs, "last at the end");
                    Check.Equal(30000L, four[1].StartMs, "even spacing");
                    Check.True(four.All(w => w.DurationMs == 10000), "equal lengths");
                    Check.True(four.Zip(four.Skip(1), (a, b) => a.EndMs <= b.StartMs).All(x => x), "no overlap");
                    List<ClipWindow> whole = ClipSampler.Evenly(30000, 4, 10000);
                    Check.True(whole.Count == 1 && whole[0].StartMs == 0 && whole[0].EndMs == 30000, "covering windows collapse to the whole");
                    List<ClipWindow> one = ClipSampler.Evenly(100000, 1, 10000);
                    Check.True(one.Count == 1 && one[0].StartMs == 45000 && one[0].EndMs == 55000, "single window is centered");
                    ClipWindow centered = ClipSampler.Centered(10000, 70000, 20000);
                    Check.True(centered.StartMs == 30000 && centered.EndMs == 50000, "centered excerpt");
                    ClipWindow fits = ClipSampler.Centered(10000, 20000, 20000);
                    Check.True(fits.StartMs == 10000 && fits.EndMs == 20000, "a span that fits is unchanged");
                    Check.Throws<ArgumentOutOfRangeException>(() => ClipSampler.Evenly(0, 1, 1), "zero duration rejected");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "RenditionExtraction", "A rendered clip has the requested format, sample rate, channels, and window", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    using (MediaWorkspace workspace = new MediaWorkspace(settings.Media.TempDirectory))
                    {
                        string wav = await TestEnvironment.WavAsync(ct).ConfigureAwait(false);
                        AudioRenditionSpec stereo = new AudioRenditionSpec { Format = ListenFormatEnum.Mp3, SampleRate = 44100, Channels = 2, BitrateKbps = 128 };
                        InferenceAudio clip = await services.Listen.RenderAsync(wav, new ClipWindow(2000, 7000), stereo, workspace, "label", ct).ConfigureAwait(false);
                        Check.Equal("audio/mpeg", clip.MediaType, "mp3 media type");
                        Check.Equal("label", clip.Label, "label kept");
                        Check.True(clip.StartMs == 2000 && clip.EndMs == 7000, "window kept");
                        Check.True(clip.Sha256 != null && clip.Sha256.Length == 64, "hash computed");
                        string path = workspace.PathFor("check.mp3");
                        await File.WriteAllBytesAsync(path, clip.Data, ct).ConfigureAwait(false);
                        MediaProperties p = await services.Prober.ProbeAsync(path, ct).ConfigureAwait(false);
                        Check.Equal((int?)2, p.AudioChannels, "stereo");
                        Check.Equal((int?)44100, p.AudioSampleRate, "44.1 kHz");
                        Check.True(p.DurationMs >= 4800 && p.DurationMs <= 5300, "five seconds: " + p.DurationMs);

                        InferenceAudio mono = await services.Listen.RenderAsync(wav, new ClipWindow(0, 3000), RenditionResolver.Resolve(new AudioRenditionSpec { Format = ListenFormatEnum.Wav }, settings.Media), workspace, null, ct).ConfigureAwait(false);
                        Check.Equal("audio/wav", mono.MediaType, "wav media type");
                        Check.True(mono.Data.Length > 90000 && mono.Data.Length < 100000, "3 s of 16 kHz mono PCM: " + mono.Data.Length);
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Planner", "Named, default, incapable, unhealthy, and Flac cases resolve to the documented plan", ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "ollama", Provider = ProviderTypeEnum.Ollama, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1", Model = "m" });
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "compat", Provider = ProviderTypeEnum.OpenAiCompatible, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1/v1", Model = "m" });
                    FakeHealthLookup health = new FakeHealthLookup();

                    Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NotRequested, ListenPlanner.Resolve(new ListenSpec { Enable = false }, true, settings, health).Reason, "not requested");
                    Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoModelFunctions, ListenPlanner.Resolve(new ListenSpec(), false, settings, health).Reason, "no model functions");
                    ListenPlan ok = ListenPlanner.Resolve(new ListenSpec(), true, settings, health);
                    Check.True(ok.Listens && ok.Endpoint!.Name == "mock-listen", "default listening endpoint");
                    Check.True(ok.Rendition.Format == ListenFormatEnum.Mp3 && ok.Rendition.SampleRate == 16000, "effective rendition");

                    ListenPlan named = ListenPlanner.Resolve(new ListenSpec { Model = new ModelSelection { Endpoint = "ollama" } }, true, settings, health);
                    Check.True(named.Rejection != null && named.Rejection.Contains("cannot take audio input"), "named incapable endpoint rejected");
                    ListenPlan missing = ListenPlanner.Resolve(new ListenSpec { Model = new ModelSelection { Endpoint = "nope" } }, true, settings, health);
                    Check.True(missing.Rejection != null, "named missing endpoint rejected");
                    ListenPlan flac = ListenPlanner.Resolve(new ListenSpec { Model = new ModelSelection { Endpoint = "compat" }, Rendition = new AudioRenditionSpec { Format = ListenFormatEnum.Flac } }, true, settings, health);
                    Check.True(flac.Rejection != null && flac.Rejection.Contains("Mp3 or Wav"), "flac to an OpenAI-compatible endpoint rejected");
                    ListenPlan compatWav = ListenPlanner.Resolve(new ListenSpec { Model = new ModelSelection { Endpoint = "compat", Name = "override" }, Rendition = new AudioRenditionSpec { Format = ListenFormatEnum.Wav } }, true, settings, health);
                    Check.True(compatWav.Listens && compatWav.Model == "override", "wav to an OpenAI-compatible endpoint is fine, model override kept");

                    settings.Inference.DefaultListening = "ollama";
                    ListenPlan incapable = ListenPlanner.Resolve(new ListenSpec(), true, settings, health);
                    Check.True(incapable.Rejection == null && incapable.Reason == ListenSkipReasonEnum.EndpointLacksAudioInput && incapable.IsFallback, "incapable default falls back");
                    settings.Inference.DefaultListening = null;
                    Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoEndpointConfigured, ListenPlanner.Resolve(new ListenSpec(), true, settings, health).Reason, "no default");
                    settings.Inference.DefaultListening = "mock-listen";

                    health.Set("mock-listen", false);
                    ListenPlan sick = ListenPlanner.Resolve(new ListenSpec(), true, settings, health);
                    Check.True(sick.Reason == ListenSkipReasonEnum.EndpointUnhealthy && !sick.Listens && sick.Rejection == null, "unhealthy default falls back");
                    ListenPlan sickNamed = ListenPlanner.Resolve(new ListenSpec { Model = new ModelSelection { Endpoint = "mock-listen" } }, true, settings, health);
                    Check.True(sickNamed.Reason == ListenSkipReasonEnum.EndpointUnhealthy && sickNamed.Rejection == null, "unhealthy named endpoint falls back, never a 400");
                    health.Set("mock-listen", true);
                    Check.True(ListenPlanner.Resolve(new ListenSpec(), true, settings, health).Listens, "recovered");

                    Check.True(PolyPromptClientBuilder.SupportsAudioInput(new EndpointSettings { Provider = ProviderTypeEnum.Gemini }), "Gemini listens");
                    Check.True(!PolyPromptClientBuilder.SupportsAudioInput(new EndpointSettings { Provider = ProviderTypeEnum.Anthropic }), "Anthropic does not");
                    Check.True(!PolyPromptClientBuilder.SupportsAudioInput(new EndpointSettings { Provider = ProviderTypeEnum.OpenAi }), "the Responses-API OpenAI client does not");
                    Check.True(!PolyPromptClientBuilder.SupportsAudioInput(new EndpointSettings { Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Transcription }), "transcription endpoints never listen");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Preflight", "Submission rejects incapable named endpoints, Flac to OpenAI-compatible, and requests with nothing to analyze", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "ollama", Provider = ProviderTypeEnum.Ollama, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1", Model = "m" });
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "compat", Provider = ProviderTypeEnum.OpenAiCompatible, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1/v1", Model = "m" });
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalyzer analyzer = new AudioAnalyzer(services);
                        AudioAnalysisRequest named = Request("https://example.com/a.mp3");
                        named.Listen.Model = new ModelSelection { Endpoint = "ollama" };
                        Check.Throws<RequestValidationException>(() => analyzer.Preflight(named), "named Ollama listening endpoint rejected");

                        AudioAnalysisRequest flac = Request("https://example.com/a.mp3");
                        flac.Listen.Model = new ModelSelection { Endpoint = "compat" };
                        flac.Listen.Rendition.Format = ListenFormatEnum.Flac;
                        Check.Throws<RequestValidationException>(() => analyzer.Preflight(flac), "flac to OpenAI-compatible rejected");

                        settings.Inference.DefaultListening = null;
                        AudioAnalysisRequest nothing = Request("https://example.com/a.mp3");
                        nothing.Functions.Transcribe.Enable = false;
                        Check.Throws<RequestValidationException>(() => analyzer.Preflight(nothing), "transcribe off with no listening endpoint has nothing to analyze");

                        settings.Inference.DefaultListening = "ollama";
                        analyzer.Preflight(Request("https://example.com/a.mp3"));

                        settings.Inference.DefaultListening = "mock-listen";
                        AudioAnalysisRequest listenOnly = Request("https://example.com/a.mp3");
                        listenOnly.Functions.Transcribe.Enable = false;
                        analyzer.Preflight(listenOnly);

                        AudioAnalysisRequest badText = Request("https://example.com/a.mp3");
                        badText.TextModel = new ModelSelection { Endpoint = "typo" };
                        Check.Throws<EndpointNotFoundException>(() => analyzer.Preflight(badText), "a misspelled TextModel is rejected even while listening is healthy");
                    }

                    await Task.CompletedTask.ConfigureAwait(false);
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "FallbackNoEndpoint", "With no listening endpoint the analysis runs on the transcript, flags it, and matches a Listen-off request", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        string data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = data } };
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.True(!response.Listening.Listened && response.Listening.Mode == ListeningModeEnum.None, "not listened");
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoEndpointConfigured, response.Listening.Reason, "reason");
                        Check.True(response.Warnings.Any(w => w.Contains("results come from the transcript only")), "fallback warning");
                        Check.True(response.Listening.Clips.Count == 0 && response.Listening.HeardMs == 0 && response.Listening.Endpoint == null, "nothing heard");
                        Check.True(response.Listening.Rendition.Format == ListenFormatEnum.Mp3 && response.Listening.DurationMs > 7000, "rendition and duration still reported");

                        AudioAnalysisRequest off = new AudioAnalysisRequest { Audio = new MediaSource { Data = data } };
                        off.Listen.Enable = false;
                        AudioAnalysisResponse offResponse = await new AudioAnalyzer(services).AnalyzeAsync(off, null, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NotRequested, offResponse.Listening.Reason, "not requested");
                        Check.True(!offResponse.Warnings.Any(w => w.Contains("listening") || w.Contains("transcript only")), "no fallback warning when not requested");
                        Check.Equal(response.Results["Summary"] as string, offResponse.Results["Summary"] as string, "same results as before this change");
                        Check.True(offResponse.Telemetry.ModelCalls.Where(c => c.Purpose != ModelCallPurposeEnum.Transcribe).All(c => c.InputAudioBytes == 0), "no audio on completion calls");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "FallbackIncapableAndUnhealthy", "An Ollama default and an unhealthy endpoint fall back without a 400 and without a listening call", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "ollama", Provider = ProviderTypeEnum.Ollama, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1", Model = "m" });
                    FakeHealthLookup health = new FakeHealthLookup();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Health = health;
                        string data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

                        settings.Inference.DefaultListening = "ollama";
                        AudioAnalysisRequest incapable = new AudioAnalysisRequest { Audio = new MediaSource { Data = data } };
                        new AudioAnalyzer(services).Preflight(incapable);
                        AudioAnalysisResponse a = await new AudioAnalyzer(services).AnalyzeAsync(incapable, null, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.EndpointLacksAudioInput, a.Listening.Reason, "incapable reason");
                        Check.True(a.Warnings.Any(w => w.Contains("cannot take audio input")), "incapable warning");

                        settings.Inference.DefaultListening = "mock-listen";
                        health.Set("mock-listen", false);
                        AudioAnalysisRequest named = new AudioAnalysisRequest { Audio = new MediaSource { Data = data } };
                        named.Listen.Model = new ModelSelection { Endpoint = "mock-listen" };
                        new AudioAnalyzer(services).Preflight(named);
                        AudioAnalysisResponse b = await new AudioAnalyzer(services).AnalyzeAsync(named, null, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.EndpointUnhealthy, b.Listening.Reason, "unhealthy reason");
                        Check.True(b.Warnings.Any(w => w.Contains("is unhealthy")), "unhealthy warning");
                        Check.True(!b.Telemetry.ModelCalls.Any(c => c.Endpoint == "mock-listen"), "no call to the unhealthy endpoint");
                        Check.True(b.Success, "transcript results still produced");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "ResolvedAtRun", "An endpoint healthy at submission and unhealthy when the job runs falls back", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    FakeHealthLookup health = new FakeHealthLookup();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Health = health;
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        health.Set("mock-listen", true);
                        new AudioAnalyzer(services).Preflight(request);
                        health.Set("mock-listen", false);
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.EndpointUnhealthy, response.Listening.Reason, "current health used");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "EndToEnd", "A short recording is heard whole: one clip, Full mode, audio bytes on the Summarize and Extract calls", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    List<AnalysisCompletion> completions = new List<AnalysisCompletion>();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Completed = c => { lock (completions) completions.Add(c); };
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Functions.Extract.Enable = true;
                        request.Functions.Extract.Fields.Add(new FieldSpec { Key = "recording", Type = FieldTypeEnum.Enum, Options = new List<string> { "Live", "Studio" }, Prompt = "How was this recorded?" });
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);

                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        ListeningInfo l = response.Listening;
                        Check.True(l.Listened && l.Mode == ListeningModeEnum.Full && l.Reason == null, "heard in full");
                        Check.Equal("mock-listen", l.Endpoint, "endpoint");
                        Check.Equal("mock-audio", l.Model, "model");
                        Check.Equal(1, l.Clips.Count, "one clip");
                        Check.True(l.Clips[0].Scope == ListenClipScopeEnum.Recording && l.Clips[0].Index == null && l.Clips[0].StartMs == 0 && l.Clips[0].EndMs == l.DurationMs && l.Clips[0].SizeBytes > 0, "whole-recording clip");
                        Check.Equal(l.DurationMs, l.HeardMs, "heard everything");
                        Check.True(!response.Warnings.Any(w => w.Contains("excerpt") || w.Contains("transcript only")), "no degradation warning");
                        ModelCallTelemetry summary = response.Telemetry.ModelCalls.Single(c => c.Purpose == ModelCallPurposeEnum.Summarize);
                        Check.True(summary.InputAudioBytes == l.Clips[0].SizeBytes && summary.Endpoint == "mock-listen", "summary heard the clip");
                        Check.True(response.Telemetry.ModelCalls.Single(c => c.Purpose == ModelCallPurposeEnum.Extract).InputAudioBytes > 0, "extract heard the clip");
                        Check.True((response.Results["Summary"] as string ?? "").Contains("1 audio clip"), "mock saw the audio: " + response.Results["Summary"]);
                        Check.True(response.Results["recording"] is string rec && (rec == "Live" || rec == "Studio"), "typed field");
                        Check.True(response.Telemetry.Stages.Any(s => s.Name == "ListenExtraction"), "listen stage timed");
                        Check.True(summary.PromptTokens > 200, "audio tokens estimated: " + summary.PromptTokens);

                        AnalysisCompletion done = completions.Single();
                        Check.Equal("mock-listen", done.VisionEndpoint, "listening endpoint recorded as the media model");
                        Check.Equal(null, done.TextEndpoint, "no text endpoint needed");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "NoTranscript", "With Transcribe off the listening model still summarizes the audio", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Functions.Transcribe.Enable = false;
                        new AudioAnalyzer(services).Preflight(request);
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.True(response.Transcript == null, "no transcript");
                        Check.True(response.Results["Summary"] is string sum && sum.Length > 0, "summary from the audio alone");
                        Check.True(!response.Telemetry.ModelCalls.Any(c => c.Purpose == ModelCallPurposeEnum.Transcribe), "no transcription");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "NoModelFunctions", "A transcribe-only request reports NoModelFunctions and no listening warning", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Functions.Summarize.Enable = false;
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoModelFunctions, response.Listening.Reason, "reason");
                        Check.True(!response.Listening.Listened && response.Listening.Mode == ListeningModeEnum.None, "not listened");
                        Check.True(!response.Warnings.Any(w => w.Contains("transcript only")), "no warning");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Excerpts", "A recording over the listen budget is heard as evenly spaced excerpts with a warning", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    settings.Media.MaxListenSeconds = 10;
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.WavAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Listen.SampleClips = 2;
                        request.Listen.ClipSeconds = 5;
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        ListeningInfo l = response.Listening;
                        Check.Equal(ListeningModeEnum.Excerpts, l.Mode, "excerpts");
                        Check.Equal(2, l.Clips.Count, "two clips");
                        Check.True(l.Clips.All(c => c.EndMs - c.StartMs == 5000 && c.Scope == ListenClipScopeEnum.Recording), "five seconds each");
                        Check.True(l.Clips[0].StartMs == 0 && l.Clips[1].EndMs == l.DurationMs, "first at the start, last at the end");
                        Check.Equal(10000L, l.HeardMs, "heard 10 s");
                        Check.True(response.Warnings.Any(w => w.StartsWith("Listened to 2 excerpts (10s) of a 25s recording")), "degradation warning: " + String.Join(" | ", response.Warnings));
                        Check.True(!response.Warnings.Any(w => w.Contains("shortened")), "no shortening when the excerpts fit");
                        Check.True((response.Results["Summary"] as string ?? "").Contains("2 audio clips"), "one call carried every excerpt");

                        request.Listen.SampleClips = 4;
                        request.Listen.ClipSeconds = 5;
                        AudioAnalysisResponse shortened = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.True(shortened.Listening.Clips.All(c => c.EndMs - c.StartMs == 2500), "excerpts shortened to fit the budget");
                        Check.True(shortened.Warnings.Any(w => w.Contains("to fit the listen budget of 10s")), "shortening warning");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "EndpointBudget", "An endpoint MaxListenSeconds overrides Media.MaxListenSeconds", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.WavAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Listen.SampleClips = 2;
                        request.Listen.ClipSeconds = 5;
                        AudioAnalysisResponse full = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(ListeningModeEnum.Full, full.Listening.Mode, "media budget of 1200 s hears 25 s whole");

                        settings.Inference.Endpoints.Single(e => e.Name == "mock-listen").MaxListenSeconds = 10;
                        AudioAnalysisResponse excerpts = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(ListeningModeEnum.Excerpts, excerpts.Listening.Mode, "endpoint budget of 10 s forces excerpts");
                        Check.True(excerpts.Listening.Clips.Sum(c => c.EndMs - c.StartMs) <= 10000, "excerpts fit the endpoint budget");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "InlineLimit", "A small MaxInlineMediaBytes shortens clips instead of failing", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    EndpointSettings listen = settings.Inference.Endpoints.Single(e => e.Name == "mock-listen");
                    listen.MaxInlineMediaBytes = 1048576;
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.WavAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Listen.Rendition = new AudioRenditionSpec { Format = ListenFormatEnum.Wav, SampleRate = 48000, Channels = 2 };
                        request.Listen.SampleClips = 4;
                        request.Listen.ClipSeconds = 5;
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.Equal(ListeningModeEnum.Excerpts, response.Listening.Mode, "whole recording does not fit, excerpts do");
                        long wire = response.Listening.Clips.Sum(c => RenditionResolver.WireBytes(c.SizeBytes));
                        Check.True(wire <= 1048576, "clips fit the inline limit: " + wire);
                        Check.True(response.Listening.Clips.All(c => c.EndMs - c.StartMs < 5000), "clips shortened");
                        Check.True(response.Warnings.Any(w => w.Contains("inline media limit")), "inline warning");
                        Check.True(response.Listening.Rendition.Format == ListenFormatEnum.Wav && response.Listening.Rendition.Channels == 2 && response.Listening.Rendition.SampleRate == 48000, "custom rendition reported");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "CallFails", "A failing listening call is a function error, with no rerun on the transcript", async ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    settings.Inference.MaxRetries = 0;
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MockInferenceClient mock = (MockInferenceClient)((GatedInferenceClient)services.Clients.GetCompletionClient(settings.Inference.Endpoints.Single(e => e.Name == "mock-listen"))).Inner;
                        mock.FailNextPermanent(1);
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.True(!response.Success, "failed");
                        Check.True(response.Errors.Any(e => e.StartsWith("Summarize:") && e.Contains("mock-listen")), "function error: " + String.Join(" | ", response.Errors));
                        Check.True(response.Listening.Listened, "audio was sent");
                        Check.Equal(1, response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.Summarize), "no second summarize call");
                        Check.True(response.Results["Summary"] == null, "no summary");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "UnreachableMarksUnhealthy", "A listening call that cannot reach its endpoint is an error and marks the endpoint unhealthy, so the next request falls back", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    settings.Inference.MaxRetries = 0;
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "dead", Provider = ProviderTypeEnum.OpenAiCompatible, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1/v1", Model = "m", TimeoutMs = 10000 });
                    settings.Inference.DefaultListening = "dead";
                    FakeHealthLookup health = new FakeHealthLookup();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Health = health;
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        AudioAnalysisResponse first = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.True(!first.Success && first.Listening.Listened && first.Errors.Any(e => e.StartsWith("Summarize:")), "first call fails as a function error");
                        Check.Equal((bool?)false, health.GetHealth("dead"), "endpoint marked unhealthy at once");
                        AudioAnalysisResponse second = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.EndpointUnhealthy, second.Listening.Reason, "next request falls back");
                        Check.True(second.Success, "fallback succeeds from the transcript");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "OpenAiCompatibleWire", "A listening request to an OpenAI-compatible server sends input_audio mp3 after the prompt and labels the transcript a hint", async ct =>
                {
                    using (StubProviderServer stub = new StubProviderServer())
                    {
                        KeyframeSettings settings = TestEnvironment.CreateSettings();
                        settings.Inference.Endpoints.Add(new EndpointSettings { Name = "compat", Provider = ProviderTypeEnum.OpenAiCompatible, Kind = EndpointKindEnum.Completion, Endpoint = stub.BaseUrl, Model = "stub-audio" });
                        settings.Inference.DefaultListening = "compat";
                        using (AnalysisServices services = new AnalysisServices(() => settings))
                        {
                            AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                            AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                            Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                            Check.True(response.Listening.Listened && response.Listening.Endpoint == "compat", "listened through the stub");

                            StubRequest sent = stub.Requests.Single(r => r.Path.EndsWith("/chat/completions"));
                            using (JsonDocument doc = JsonDocument.Parse(sent.Body))
                            {
                                JsonElement messages = doc.RootElement.GetProperty("messages");
                                Check.Equal(1, messages.GetArrayLength(), "no separate system message when audio is attached (Voxtral rejects one)");
                                Check.Equal("user", messages[0].GetProperty("role").GetString(), "single user message");
                                JsonElement parts = messages[0].GetProperty("content");
                                string prompt = parts[0].GetProperty("text").GetString() ?? "";
                                Check.True(prompt.StartsWith("You are a careful media analyst."), "system prompt leads the user message");
                                Check.True(prompt.Contains("is attached as audio") && prompt.Contains("A machine transcript follows as a hint"), "listening description: " + prompt.Substring(0, Math.Min(300, prompt.Length)));
                                Check.True(prompt.Contains("<transcript>"), "transcript included");
                                Check.True((parts[1].GetProperty("text").GetString() ?? "").StartsWith("Full recording"), "clip label before the audio");
                                JsonElement audio = parts[2];
                                Check.Equal("input_audio", audio.GetProperty("type").GetString(), "input_audio part");
                                Check.Equal("mp3", audio.GetProperty("input_audio").GetProperty("format").GetString(), "mp3 format");
                                Check.True((audio.GetProperty("input_audio").GetProperty("data").GetString() ?? "").Length > 1000, "base64 audio data");
                            }
                        }
                    }
                }, new[] { "provider", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "OllamaRejectsAudio", "Audio sent to an Ollama client is a reported capability error, never sent", async ct =>
                {
                    using (StubProviderServer stub = new StubProviderServer())
                    using (PolyPromptInferenceClient client = new PolyPromptInferenceClient(new EndpointSettings { Name = "o", Provider = ProviderTypeEnum.Ollama, Kind = EndpointKindEnum.Completion, Endpoint = stub.BaseUrl, Model = "m" }))
                    {
                        byte[] mp3 = await File.ReadAllBytesAsync(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        InferenceResponse response = await client.CompleteAsync(new InferenceRequest { UserPrompt = "Listen.", Audio = new List<InferenceAudio> { new InferenceAudio { Data = mp3 } } }, ct).ConfigureAwait(false);
                        Check.True(!response.Success && (response.Error ?? "").Contains("cannot take this input"), "capability error: " + response.Error);
                        Check.Equal(0, stub.Requests.Count, "nothing sent");
                    }
                }, new[] { "provider", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Route", "POST /v1.0/audio rejects an incapable named listening endpoint with 400, listens otherwise, and endpoints report SupportsAudioInput", async ct =>
                {
                    using (ServerFixture f = await ServerFixture.StartAsync(s =>
                    {
                        s.Inference.Endpoints.Add(new EndpointSettings { Name = "mock-listen", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "mock-audio", MaxConcurrentRequests = 4 });
                        s.Inference.Endpoints.Add(new EndpointSettings { Name = "ollama", Provider = ProviderTypeEnum.Ollama, Kind = EndpointKindEnum.Completion, Endpoint = "http://127.0.0.1:1", Model = "m", Active = true });
                        s.Inference.DefaultListening = "mock-listen";
                    }, ct).ConfigureAwait(false))
                    {
                        string data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        ApiErrorResponse rejected = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/audio", ServerFixture.AccessKey,
                            "{\"Audio\":{\"Data\":\"" + data + "\"},\"Listen\":{\"Model\":{\"Endpoint\":\"ollama\"}}}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                        Check.True(rejected.Details.Any(d => d.Contains("cannot take audio input")), "capability 400: " + String.Join(" | ", rejected.Details));

                        AudioAnalysisResponse heard = await f.JsonAsync<AudioAnalysisResponse>(HttpMethod.Post, "/v1.0/audio", ServerFixture.AccessKey,
                            "{\"Audio\":{\"Data\":\"" + data + "\"},\"Wait\":true,\"WaitTimeoutMs\":60000}", HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(heard.Listening.Listened && heard.Listening.Mode == ListeningModeEnum.Full && heard.Listening.Endpoint == "mock-listen", "listened over HTTP");

                        List<EndpointResponse> endpoints = await f.JsonAsync<List<EndpointResponse>>(HttpMethod.Get, "/v1.0/endpoints", ServerFixture.AccessKey, null, HttpStatusCode.OK, ct).ConfigureAwait(false);
                        Check.True(endpoints.Single(e => e.Name == "mock-listen").SupportsAudioInput, "mock listens");
                        Check.True(endpoints.Single(e => e.Name == "mock-listen").IsDefault, "the default listening endpoint is a default");
                        Check.True(!endpoints.Single(e => e.Name == "ollama").SupportsAudioInput, "Ollama does not");
                        Check.True(!endpoints.Single(e => e.Name == "mock-stt").SupportsAudioInput, "transcription endpoints do not");
                    }
                }, new[] { "server", "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "SettingsMetadata", "New listening, measurement, and probe settings appear in the settings metadata as Live", ct =>
                {
                    KeyframeSettings settings = ListeningSettings();
                    string file = System.IO.Path.Combine(TestEnvironment.Root, "settings-" + Guid.NewGuid().ToString("N") + ".json");
                    Keyframe.Server.SettingsManager manager = new Keyframe.Server.SettingsManager(settings, file);
                    SettingsResponse masked = manager.GetMasked();
                    foreach (string path in new[] { "Inference.DefaultListening", "Inference.HealthProbeIntervalMs", "Inference.UnhealthyProbeIntervalMs",
                        "Inference.Endpoints[].MaxInlineMediaBytes", "Inference.Endpoints[].MaxListenSeconds", "Media.ListenFormat", "Media.ListenSampleRate",
                        "Media.ListenChannels", "Media.ListenBitrateKbps", "Media.MaxListenSeconds", "Media.MeasureSilenceDb", "Media.MeasureMinSilenceMs",
                        "Media.ClippingTruePeakDbtp", "Media.ClippingMinPeaksPerMinute", "Media.MonoMaxSideDb", "Analytics.Enable", "RequestHistory.Enable" })
                    {
                        SettingsFieldMetadata? field = masked.Fields.FirstOrDefault(f => f.Path == path);
                        Check.True(field != null, "metadata for " + path);
                        Check.Equal("Live", field!.ApplyMode, "apply mode of " + path);
                    }

                    Check.True(!masked.Fields.Any(f => f.Path.EndsWith(".Enabled")), "no Enabled members remain");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "HealthScheduling", "Probes run concurrently on the short interval, and a busy endpoint is skipped with its state kept", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    settings.Inference.UnhealthyProbeIntervalMs = 1000;
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "busy", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "m", MaxConcurrentRequests = 1, TimeoutMs = 60000 });
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        LoggingModule logging = new LoggingModule();
                        logging.Settings.EnableConsole = false;
                        using (EndpointHealthService health = new EndpointHealthService(services, () => settings, logging))
                        {
                            GatedInferenceClient gate = (GatedInferenceClient)services.Clients.GetCompletionClient(settings.Inference.Endpoints.Single(e => e.Name == "busy"));
                            MockInferenceClient mock = (MockInferenceClient)gate.Inner;
                            mock.DelayMs = 4000;
                            Task<InferenceResponse> work = gate.CompleteAsync(new InferenceRequest { UserPrompt = "long work" }, ct);
                            await Task.Delay(100, ct).ConfigureAwait(false);
                            Check.True(gate.IsBusy, "the only slot is taken");
                            Check.Equal(null, health.GetHealth("mock"), "not checked before start");

                            health.Start();
                            await Task.Delay(1500, ct).ConfigureAwait(false);
                            Check.Equal((bool?)true, health.GetHealth("mock"), "other endpoints are probed while one is busy");
                            Check.Equal(null, health.GetHealth("busy"), "busy endpoint skipped, state kept (never checked)");

                            await work.ConfigureAwait(false);
                            mock.DelayMs = 0;
                            await Task.Delay(2500, ct).ConfigureAwait(false);
                            Check.Equal((bool?)true, health.GetHealth("busy"), "probed once free");
                            health.Stop();
                        }
                    }
                }, new[] { "server" })
            });
        }

        /// <summary>
        /// Test settings with a mock listening endpoint ("mock-listen", model "mock-audio") as Inference.DefaultListening.
        /// </summary>
        /// <returns>Settings.</returns>
        public static KeyframeSettings ListeningSettings()
        {
            KeyframeSettings settings = TestEnvironment.CreateSettings();
            settings.Inference.Endpoints.Add(new EndpointSettings { Name = "mock-listen", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "mock-audio", MaxConcurrentRequests = 4 });
            settings.Inference.DefaultListening = "mock-listen";
            return settings;
        }

        private static AudioAnalysisRequest Request(string url)
        {
            return new AudioAnalysisRequest { Audio = new MediaSource { Url = url } };
        }
    }
}
