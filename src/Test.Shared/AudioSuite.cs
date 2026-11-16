namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Audio analysis with the mock providers: request defaults and validation, chunked transcription, transcript
    /// summary and fields on the text model, endpoint selection, audio formats including cover art, the audio track
    /// of a video, rejections, and the completion notification used for analytics.
    /// </summary>
    public static class AudioSuite
    {
        private const string Id = "Audio";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Audio analysis", new[]
            {
                new TestCaseDescriptor(Id, "Defaults", "Transcribe and Summarize default on, Extract off; Model is accepted as an alias of Name", ct =>
                {
                    AudioAnalysisRequest fresh = new AudioAnalysisRequest();
                    Check.True(fresh.Functions.Transcribe.Enable, "Transcribe on");
                    Check.True(fresh.Functions.Summarize.Enable, "Summarize on");
                    Check.True(!fresh.Functions.Extract.Enable, "Extract off");
                    Check.Equal(true, fresh.IncludeProperties, "properties on");

                    AudioAnalysisRequest parsed = KeyframeSerializer.Deserialize<AudioAnalysisRequest>(
                        "{\"Audio\":{\"Url\":\"https://example.com/a.mp3\"},\"TextModel\":{\"Endpoint\":\"t\",\"Model\":\"m1\"},\"TranscriptionModel\":{\"Endpoint\":\"s\",\"Name\":\"m2\"}}")!;
                    Check.True(parsed.Functions.Transcribe.Enable && parsed.Functions.Summarize.Enable, "defaults survive JSON");
                    Check.Equal("m1", parsed.TextModel!.Name, "Model alias sets Name");
                    Check.Equal("m2", parsed.TranscriptionModel!.Name, "Name still works");
                    string json = KeyframeSerializer.Serialize(parsed.TextModel, false);
                    Check.True(!json.Contains("\"Model\""), "alias is not written: " + json);
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Validation", "Every problem is reported", ct =>
                {
                    Check.True(RequestValidator.Validate((AudioAnalysisRequest?)null).Count == 1, "null body");

                    AudioAnalysisRequest missing = new AudioAnalysisRequest();
                    Check.True(RequestValidator.Validate(missing).Any(e => e.StartsWith("Audio is required")), "audio required");

                    AudioAnalysisRequest noTranscript = new AudioAnalysisRequest { Audio = new MediaSource { Url = "https://example.com/a.mp3" } };
                    noTranscript.Functions.Transcribe.Enable = false;
                    Check.True(RequestValidator.Validate(noTranscript).Any(e => e.Contains("enable Functions.Transcribe")), "summary needs a transcript");

                    AudioAnalysisRequest nothing = new AudioAnalysisRequest { Audio = new MediaSource { Url = "https://example.com/a.mp3" } };
                    nothing.Functions.Transcribe.Enable = false;
                    nothing.Functions.Summarize.Enable = false;
                    Check.True(RequestValidator.Validate(nothing).Any(e => e.StartsWith("Enable at least one function")), "a function is required");

                    AudioAnalysisRequest bad = new AudioAnalysisRequest { Audio = new MediaSource { Url = "ftp://example.com/a.mp3" } };
                    bad.Functions.Transcribe.Language = "english!";
                    bad.Functions.Extract.Enable = true;
                    List<string> errors = RequestValidator.Validate(bad);
                    Check.True(errors.Any(e => e.Contains("http or https")), "url scheme");
                    Check.True(errors.Any(e => e.Contains("ISO-639")), "language");
                    Check.True(errors.Any(e => e.Contains("at least one field")), "fields required");

                    AudioAnalysisRequest good = new AudioAnalysisRequest { Audio = new MediaSource { Url = "https://example.com/a.mp3" } };
                    good.Functions.Transcribe.Language = "en";
                    Check.Equal(0, RequestValidator.Validate(good).Count, "valid request");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "WavEndToEnd", "A 25 second WAV is transcribed in three chunks, then summarized and extracted on the text model", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    settings.Media.TranscriptionChunkSeconds = 10;
                    List<AnalysisCompletion> completions = new List<AnalysisCompletion>();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Completed = c => { lock (completions) completions.Add(c); };
                        AudioAnalysisRequest request = new AudioAnalysisRequest
                        {
                            Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.WavAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false) },
                            Context = new List<ContextItem> { new ContextItem { Label = "Event", Text = "Synthetic tone." } }
                        };
                        request.Functions.Transcribe.Language = "en";
                        request.Functions.Extract.Enable = true;
                        request.Functions.Extract.Fields.Add(new FieldSpec { Key = "speakerCount", Type = FieldTypeEnum.Integer, Prompt = "How many people speak?" });

                        List<JobProgress> progress = new List<JobProgress>();
                        AnalysisOrigin origin = new AnalysisOrigin { RequestId = "rqi_test", JobId = "kfj_test", PrincipalName = "tester", QueueWaitMs = 12.5 };
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, p => { lock (progress) progress.Add(p); }, origin, ct).ConfigureAwait(false);

                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.Equal(MediaKindEnum.Audio, response.MediaKind, "media kind");
                        Check.Equal("rqi_test", response.RequestId, "request id");
                        Check.Equal(12.5, response.Telemetry.QueueWaitMs, "queue wait");
                        Check.Equal(12.5, response.Telemetry.QueuedMs, "legacy queued ms");
                        Check.True(response.Properties != null && response.Properties.MediaKind == MediaKindEnum.Audio, "properties");
                        Check.Equal("pcm_s16le", response.Properties!.AudioCodec, "codec");
                        Check.Equal(44100, response.Properties.AudioSampleRate, "sample rate");
                        Check.Equal(2, response.Properties.AudioChannels, "channels");
                        Check.True(response.Properties.DurationMs >= 24900 && response.Properties.DurationMs <= 25100, "duration");
                        Check.True(response.Transcript != null && response.Transcript.Segments.Count > 0, "transcript");
                        Check.Equal("en", response.Transcript!.Language, "language");
                        Check.True(response.Transcript.Segments.Max(s => s.EndMs) > 20000, "segments cover the third chunk");
                        Check.True(response.Transcript.Segments.Zip(response.Transcript.Segments.Skip(1), (a, b) => a.StartMs <= b.StartMs).All(x => x), "segments in order");
                        Check.True(response.Results["Summary"] is string, "summary");
                        Check.True(response.Results["speakerCount"] is long, "typed field");
                        Check.Equal(3, response.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.Transcribe), "three 10 second chunks");
                        Check.True(response.Telemetry.ModelCalls.Where(c => c.Purpose == ModelCallPurposeEnum.Transcribe).All(c => c.InputAudioBytes > 0 && c.Endpoint == "mock-stt"), "chunk audio sent to the transcription endpoint");
                        ModelCallTelemetry summary = response.Telemetry.ModelCalls.Single(c => c.Purpose == ModelCallPurposeEnum.Summarize);
                        Check.Equal(0, summary.InputImages, "text-only summary");
                        Check.True(summary.TimeToFirstTokenMs > 0 && summary.GenerationMs > 0 && summary.TokensPerSecond > 0, "mock timing reported");
                        List<string> stages = response.Telemetry.Stages.Select(s => s.Name).ToList();
                        foreach (string stage in new[] { "Resolve", "Probe", "AudioExtraction", "Transcribe", "Summarize", "Extract" })
                            Check.True(stages.Contains(stage), "stage " + stage);
                        Check.True(progress.Count > 0 && progress.Last().Percent == 100, "progress to 100");

                        Check.Equal(1, completions.Count, "one completion");
                        AnalysisCompletion done = completions[0];
                        Check.Equal(MediaKindEnum.Audio, done.MediaKind, "completion kind");
                        Check.Equal("mock", done.TextEndpoint, "text endpoint");
                        Check.Equal("mock-stt", done.TranscriptionEndpoint, "transcription endpoint");
                        Check.Equal(null, done.VisionEndpoint, "no vision endpoint");
                        Check.True(done.MediaDurationMs >= 24900 && done.MediaBytes > 0, "media facts");
                        Check.Equal(response.Transcript.Text.Length, (int)done.TranscriptCharacters, "transcript characters");
                        Check.True(done.Exception == null && !done.Canceled, "clean finish");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "EndpointSelection", "TextModel and TranscriptionModel pick endpoints and override models", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "mock-text", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "mock-llm" });
                    settings.Inference.Endpoints.Add(new EndpointSettings { Name = "mock-stt2", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Transcription, Model = "mock-whisper-2" });
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest
                        {
                            Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) },
                            TextModel = new ModelSelection { Endpoint = "mock-text" },
                            TranscriptionModel = new ModelSelection { Endpoint = "mock-stt2", Name = "override-stt" }
                        };

                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.True(response.Telemetry.ModelCalls.Where(c => c.Purpose == ModelCallPurposeEnum.Transcribe).All(c => c.Endpoint == "mock-stt2" && c.Model == "override-stt"), "transcription endpoint and model");
                        Check.Equal("mock-text", response.Telemetry.ModelCalls.Single(c => c.Purpose == ModelCallPurposeEnum.Summarize).Endpoint, "text endpoint");
                        Check.Equal("audio/mpeg", response.Properties!.ContentType, "mp3 detected");
                        Check.Equal("mp3", response.Properties.AudioCodec, "mp3 codec");

                        AudioAnalysisRequest unknown = new AudioAnalysisRequest { Audio = request.Audio, TextModel = new ModelSelection { Endpoint = "nope" } };
                        Check.Throws<EndpointNotFoundException>(() => new AudioAnalyzer(services).Preflight(unknown), "unknown text endpoint rejected before queueing");
                        AudioAnalysisRequest wrongKind = new AudioAnalysisRequest { Audio = request.Audio, TranscriptionModel = new ModelSelection { Endpoint = "mock" } };
                        Check.Throws<EndpointNotFoundException>(() => new AudioAnalyzer(services).Preflight(wrongKind), "completion endpoint is not a transcription endpoint");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "CoverArtAndVideo", "MP3 cover art is not mistaken for video; a video file contributes only its audio track", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    settings.Media.TranscriptionChunkSeconds = 10;
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        string cover = await TestEnvironment.Mp3WithCoverAsync(ct).ConfigureAwait(false);
                        MediaProperties props = await services.Prober.ProbeAsync(cover, ct).ConfigureAwait(false);
                        Check.Equal(MediaKindEnum.Audio, props.MediaKind, "cover art mp3 is audio");
                        Check.Equal(null, props.Width, "no width from cover art");

                        AudioAnalysisRequest coverRequest = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(cover, ct).ConfigureAwait(false) } };
                        coverRequest.Functions.Summarize.Enable = false;
                        AudioAnalysisResponse coverResponse = await new AudioAnalyzer(services).AnalyzeAsync(coverRequest, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, coverResponse.Success, "cover art mp3 analyzed");
                        Check.True(!coverResponse.Telemetry.ModelCalls.Any(c => c.Purpose != ModelCallPurposeEnum.Transcribe), "transcribe only");

                        AudioAnalysisRequest videoRequest = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        AudioAnalysisResponse videoResponse = await new AudioAnalyzer(services).AnalyzeAsync(videoRequest, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, videoResponse.Success, "video analyzed as audio: " + String.Join(" ", videoResponse.Errors));
                        Check.Equal(MediaKindEnum.Audio, videoResponse.MediaKind, "response kind");
                        Check.Equal(MediaKindEnum.Video, videoResponse.Properties!.MediaKind, "properties describe the video");
                        Check.True(videoResponse.Warnings.Any(w => w.Contains("only its audio track")), "warning");
                        Check.Equal(2, videoResponse.Telemetry.ModelCalls.Count(c => c.Purpose == ModelCallPurposeEnum.Transcribe), "two chunks for 12 seconds");
                        Check.True(videoResponse.Telemetry.Totals.InputImages == 0, "no frames sent");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Rejections", "Silent video and images are rejected with 422-class errors and still reported as failed analyses", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    List<AnalysisCompletion> completions = new List<AnalysisCompletion>();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        services.Completed = c => { lock (completions) completions.Add(c); };
                        foreach (string path in new[] { await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), await TestEnvironment.PngAsync(ct).ConfigureAwait(false) })
                        {
                            AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(path, ct).ConfigureAwait(false) } };
                            bool threw = false;
                            try
                            {
                                await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                            }
                            catch (UnsupportedMediaException)
                            {
                                threw = true;
                            }

                            Check.True(threw, "rejected: " + path);
                        }

                        Check.Equal(2, completions.Count, "both failures reported");
                        Check.True(completions.All(c => c.Exception != null && !c.Response.Success && c.MediaBytes > 0), "failure details");
                    }
                }, new[] { "ffmpeg" }, skip)
            });
        }
    }
}
