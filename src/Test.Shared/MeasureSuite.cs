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
    /// Audio measurements: parsing captured ffmpeg 6 and 9 output, levels on a known tone, mono and silence detection,
    /// the measure-only path with no model calls, prompt notes, and the at-least-one-function rule.
    /// </summary>
    public static class MeasureSuite
    {
        private const string Id = "Measure";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Audio measurements", new[]
            {
                new TestCaseDescriptor(Id, "ParserCaptured", "Captured ffmpeg 6.1 and 9.0 output parses to the same measurements", ct =>
                {
                    foreach (string version in new[] { "6", "9" })
                    {
                        AudioMeasurements m = AudioMeasurementParser.Parse(TestEnvironment.ReadData("Ffmpeg/measure-ffmpeg" + version + ".txt"), 17000, 2);
                        Check.Equal((double?)-25.7, m.IntegratedLufs, "integrated loudness, ffmpeg " + version);
                        Check.Equal((double?)4.0, m.LoudnessRangeLu, "loudness range, ffmpeg " + version);
                        Check.Equal((double?)-19.7, m.TruePeakDbtp, "true peak, ffmpeg " + version);
                        Check.True(Math.Abs(m.PeakDbfs!.Value - -19.730665) < 0.001, "sample peak, ffmpeg " + version);
                        Check.True(Math.Abs(m.RmsDbfs!.Value - -28.549529) < 0.001, "rms, ffmpeg " + version);
                        Check.Equal((double?)8.82, m.CrestFactorDb, "crest factor, ffmpeg " + version);
                        Check.Equal((long?)3, m.PeakCount, "peak count, ffmpeg " + version);
                        Check.Equal(2, m.SilenceCount, "two silences, ffmpeg " + version);
                        Check.Equal(1000L, m.LeadingSilenceMs, "leading silence, ffmpeg " + version);
                        Check.Equal(0L, m.TrailingSilenceMs, "no trailing silence, ffmpeg " + version);
                        Check.True(Math.Abs(m.SilenceRatio - 3.0 / 17.0) < 0.002, "silence ratio, ffmpeg " + version + ": " + m.SilenceRatio);
                        Check.Equal(2, m.Channels, "channels");

                        double? width = AudioMeasurementParser.ParseStereoWidth(TestEnvironment.ReadData("Ffmpeg/width-ffmpeg" + version + ".txt"));
                        Check.True(width.HasValue && Math.Abs(width.Value - -0.49) < 0.01, "stereo width, ffmpeg " + version + ": " + width);

                        AudioMeasurements mono = AudioMeasurementParser.Parse(TestEnvironment.ReadData("Ffmpeg/mono-trailing-ffmpeg" + version + ".txt"), 7000, 1);
                        Check.Equal((double?)-41.2, mono.IntegratedLufs, "mono loudness, ffmpeg " + version);
                        Check.Equal(3000L, mono.TrailingSilenceMs, "trailing silence, ffmpeg " + version);
                        Check.Equal(0L, mono.LeadingSilenceMs, "no leading silence, ffmpeg " + version);
                        AudioMeasurementParser.ApplyThresholds(mono, 7000, new MediaSettings());
                        Check.True(mono.EffectivelyMono && !mono.ClippingLikely, "mono and not clipping");
                    }

                    List<ClipWindow> open = AudioMeasurementParser.ParseSilences("[silencedetect @ 0x1] silence_start: 5.5\n", 8000);
                    Check.True(open.Count == 1 && open[0].StartMs == 5500 && open[0].EndMs == 8000, "an open silence ends at the end");
                    Check.True(AudioMeasurementParser.Parse("", 1000, 2).IntegratedLufs == null, "empty output gives nulls");
                    Check.Equal((double?)-120.0, AudioMeasurementParser.ParseStereoWidth("[Parsed_astats_1 @ 0x1] Channel: 1\n[Parsed_astats_1 @ 0x1] RMS level dB: -20.0\n[Parsed_astats_1 @ 0x1] Channel: 2\n[Parsed_astats_1 @ 0x1] RMS level dB: -inf\n"), "identical channels");

                    AudioMeasurements hot = new AudioMeasurements { TruePeakDbtp = 0.2, PeakCount = 120, Channels = 2, StereoWidthDb = -10 };
                    AudioMeasurementParser.ApplyThresholds(hot, 60000, new MediaSettings());
                    Check.True(hot.ClippingLikely && !hot.EffectivelyMono, "clipping derived from thresholds");
                    AudioMeasurementParser.ApplyThresholds(hot, 60000, new MediaSettings { ClippingMinPeaksPerMinute = 500, MonoMaxSideDb = -5 });
                    Check.True(!hot.ClippingLikely && hot.EffectivelyMono, "thresholds are settings");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "KnownLevel", "A -20 dBFS sine measures a -20 dBFS peak and a -23 dBFS RMS", async ct =>
                {
                    AudioMeasurer measurer = new AudioMeasurer(new FfmpegRunner());
                    AudioMeasurements m = await measurer.MeasureAsync(await TestEnvironment.KnownLevelToneAsync(ct).ConfigureAwait(false), 6000, 1, new MediaSettings(), ct).ConfigureAwait(false);
                    Check.True(Math.Abs(m.PeakDbfs!.Value - -20.0) < 0.2, "peak: " + m.PeakDbfs);
                    Check.True(Math.Abs(m.RmsDbfs!.Value - -23.01) < 0.3, "rms: " + m.RmsDbfs);
                    Check.True(Math.Abs(m.CrestFactorDb!.Value - 3.01) < 0.3, "crest factor of a sine: " + m.CrestFactorDb);
                    Check.True(m.IntegratedLufs.HasValue && m.TruePeakDbtp.HasValue && m.TruePeakDbtp.Value < -19, "loudness and true peak measured");
                    Check.True(m.EffectivelyMono && m.StereoWidthDb == null && !m.ClippingLikely, "mono, not clipping");
                    Check.Equal(0, m.SilenceCount, "no silence");
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "MonoAndSilence", "Identical channels are effectively mono; leading and trailing silence are measured", async ct =>
                {
                    AudioMeasurer measurer = new AudioMeasurer(new FfmpegRunner());
                    AudioMeasurements twin = await measurer.MeasureAsync(await TestEnvironment.MonoAsStereoAsync(ct).ConfigureAwait(false), 6000, 2, new MediaSettings(), ct).ConfigureAwait(false);
                    Check.True(twin.StereoWidthDb.HasValue && twin.StereoWidthDb.Value <= -100, "no side signal: " + twin.StereoWidthDb);
                    Check.True(twin.EffectivelyMono, "effectively mono");

                    AudioMeasurements padded = await measurer.MeasureAsync(await TestEnvironment.SilencePaddedAsync(ct).ConfigureAwait(false), 6000, 1, new MediaSettings(), ct).ConfigureAwait(false);
                    Check.Equal(2, padded.SilenceCount, "two silences");
                    Check.True(Math.Abs(padded.LeadingSilenceMs - 1000) <= 50, "leading: " + padded.LeadingSilenceMs);
                    Check.True(Math.Abs(padded.TrailingSilenceMs - 2000) <= 50, "trailing: " + padded.TrailingSilenceMs);
                    Check.True(Math.Abs(padded.SilenceRatio - 0.5) < 0.02, "half silence: " + padded.SilenceRatio);
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "MeasureOnly", "A measure-only request makes no model calls and returns timed measurements", async ct =>
                {
                    KeyframeSettings settings = ListenSuite.ListeningSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.WavAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                        request.Functions.Transcribe.Enable = false;
                        request.Functions.Summarize.Enable = false;
                        Check.Equal(0, RequestValidator.Validate(request).Count, "measure alone is a valid request");
                        new AudioAnalyzer(services).Preflight(request);
                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        Check.Equal(true, response.Success, "success: " + String.Join(" ", response.Errors));
                        Check.Equal(0, response.Telemetry.ModelCalls.Count, "no model calls");
                        Check.True(response.Measurements != null && response.Measurements.Channels == 2 && response.Measurements.IntegratedLufs.HasValue, "measurements");
                        Check.True(response.Measurements!.EffectivelyMono, "the stereo WAV fixture has identical channels");
                        Check.True(response.Telemetry.Stages.Any(s => s.Name == "Measure"), "measure stage timed");
                        Check.Equal((ListenSkipReasonEnum?)ListenSkipReasonEnum.NoModelFunctions, response.Listening.Reason, "nothing needed the audio");

                        AudioAnalysisRequest off = new AudioAnalysisRequest { Audio = request.Audio };
                        off.Functions.Measure.Enable = false;
                        AudioAnalysisResponse noMeasure = await new AudioAnalyzer(services).AnalyzeAsync(off, null, null, ct).ConfigureAwait(false);
                        Check.True(noMeasure.Measurements == null && !noMeasure.Telemetry.Stages.Any(s => s.Name == "Measure"), "measure off");

                        AudioAnalysisRequest none = new AudioAnalysisRequest { Audio = request.Audio };
                        none.Functions.Transcribe.Enable = false;
                        none.Functions.Summarize.Enable = false;
                        none.Functions.Measure.Enable = false;
                        Check.True(RequestValidator.Validate(none).Any(e => e.StartsWith("Enable at least one function (Transcribe, Summarize, Extract, Measure, or Sections)")), "nothing enabled is still rejected");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "PromptNotes", "Measurements reach prompts as data in the notes tag", async ct =>
                {
                    AudioMeasurements m = new AudioMeasurements { IntegratedLufs = -9.4, LoudnessRangeLu = 5.1, TruePeakDbtp = -0.2, CrestFactorDb = 11.5, SilenceRatio = 0.03, Channels = 2 };
                    string? note = AudioAnalyzer.MeasurementNotes(m);
                    Check.True(note != null && note.Contains("integrated loudness -9.4 LUFS") && note.Contains("loudness range 5.1 LU") && note.Contains("true peak -0.2 dBTP") && note.Contains("3% silence") && note.Contains("stereo"), "note: " + note);
                    Check.Equal(null, AudioAnalyzer.MeasurementNotes(null), "no measurements, no note");
                    string prompt = PromptBuilder.Summarize(new MediaSubject { Noun = "the recording", Description = "Audio.", Notes = note }, new SummarizeSpec { Enable = true });
                    Check.True(prompt.Contains("<notes>\n" + note + "\n</notes>"), "rendered inside the notes tag");

                    using (StubProviderServer stub = new StubProviderServer())
                    {
                        KeyframeSettings settings = TestEnvironment.CreateSettings();
                        settings.Inference.Endpoints.Add(new EndpointSettings { Name = "compat", Provider = ProviderTypeEnum.OpenAiCompatible, Kind = EndpointKindEnum.Completion, Endpoint = stub.BaseUrl, Model = "stub-audio" });
                        settings.Inference.DefaultListening = "compat";
                        using (AnalysisServices services = new AnalysisServices(() => settings))
                        {
                            AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                            await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                            string body = stub.Requests.Single(r => r.Path.EndsWith("/chat/completions")).Body;
                            using (System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(body))
                            {
                                string text = doc.RootElement.GetProperty("messages")[0].GetProperty("content")[0].GetProperty("text").GetString() ?? "";
                                Check.True(text.Contains("<notes>") && text.Contains("Measured by signal analysis"), "measurement note sent to the listening model");
                            }
                        }
                    }
                }, new[] { "ffmpeg" }, skip)
            });
        }
    }
}
