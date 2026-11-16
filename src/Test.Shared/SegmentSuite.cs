namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Media;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Segment retrieval: validation, video clips (mp4 and webm, scaled), audio clips in every format, frames,
    /// clamping past the end, missing-stream errors, and the POST /v1.0/segment route in binary and JSON form.
    /// </summary>
    public static class SegmentSuite
    {
        private const string Id = "Segment";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Segments", new[]
            {
                new TestCaseDescriptor(Id, "Validation", "Range, format, MaxEdge, and length limits", ct =>
                {
                    MediaSource source = new MediaSource { Url = "https://example.com/v.mp4" };
                    Check.Equal(0, RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 0, EndMs = 1000 }, 600000).Count, "valid video");
                    Check.Equal(0, RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 500, Output = SegmentOutputEnum.Frame, Format = "JPEG" }, 600000).Count, "frame ignores EndMs; jpeg alias");
                    Check.Equal(0, RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 0, EndMs = 10, Output = SegmentOutputEnum.Audio, Format = "ogg" }, 600000).Count, "valid audio");
                    Check.True(RequestValidator.Validate(new SegmentRequest { StartMs = 0, EndMs = 10 }, 600000).Any(e => e.StartsWith("Source is required")), "source required");
                    Check.True(RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = -1, EndMs = 10 }, 600000).Any(e => e.Contains("StartMs")), "negative start");
                    Check.True(RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 0 }, 600000).Any(e => e.Contains("EndMs is required")), "end required");
                    Check.True(RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 100, EndMs = 100 }, 600000).Any(e => e.Contains("greater than StartMs")), "empty range");
                    Check.True(RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 0, EndMs = 600001 }, 600000).Any(e => e.Contains("MaxSegmentMs")), "too long");
                    Check.True(RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 0, EndMs = 10, Format = "mp3" }, 600000).Any(e => e.Contains("not valid for Video")), "audio format for video");
                    Check.True(RequestValidator.Validate(new SegmentRequest { Source = source, StartMs = 0, EndMs = 10, MaxEdge = 8 }, 600000).Any(e => e.Contains("MaxEdge")), "max edge");
                    Check.Equal("m4a", SegmentFormats.Normalize(SegmentOutputEnum.Audio, ".M4A"), "format normalization");
                    Check.Equal("mp4", SegmentFormats.Normalize(SegmentOutputEnum.Video, null), "default video format");
                    Check.Equal("audio/mpeg", SegmentFormats.MediaType("mp3"), "media type");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "VideoClips", "mp4 and webm clips are re-encoded to the requested range and size", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MediaSource source = await SourceAsync(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        foreach (string format in new[] { "mp4", "webm" })
                        {
                            ExtractedSegment segment = await services.Segments.CutAsync(new SegmentRequest { Source = source, StartMs = 4000, EndMs = 7000, Format = format, MaxEdge = 320 }, settings.Media, ct).ConfigureAwait(false);
                            Check.Equal("segment-4000-7000." + format, segment.Filename, format + " filename");
                            Check.Equal(3000L, segment.DurationMs, format + " duration");
                            Check.Equal(format == "mp4" ? "video/mp4" : "video/webm", segment.MediaType, format + " media type");
                            MediaProperties props = await ProbeBytesAsync(services, segment.Data, format, ct).ConfigureAwait(false);
                            Check.Equal(MediaKindEnum.Video, props.MediaKind, format + " is video");
                            Check.True(props.DurationMs >= 2800 && props.DurationMs <= 3200, format + " probed duration " + props.DurationMs);
                            Check.Equal(320, props.Width, format + " scaled width");
                            Check.True(props.HasAudio, format + " keeps audio");
                        }
                    }
                }, new[] { "ffmpeg", "slow" }, skip),

                new TestCaseDescriptor(Id, "AudioClips", "Audio clips in mp3, wav, flac, m4a, and ogg from video and audio sources", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MediaSource video = await SourceAsync(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        MediaSource mp3 = await SourceAsync(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        foreach (string format in SegmentFormats.For(SegmentOutputEnum.Audio))
                        {
                            MediaSource source = format == "mp3" ? video : mp3;
                            ExtractedSegment segment = await services.Segments.CutAsync(new SegmentRequest { Source = source, StartMs = 1000, EndMs = 3000, Output = SegmentOutputEnum.Audio, Format = format }, settings.Media, ct).ConfigureAwait(false);
                            MediaProperties props = await ProbeBytesAsync(services, segment.Data, format, ct).ConfigureAwait(false);
                            Check.Equal(MediaKindEnum.Audio, props.MediaKind, format + " is audio");
                            Check.True(props.DurationMs >= 1850 && props.DurationMs <= 2200, format + " duration " + props.DurationMs);
                            Check.Equal(SegmentFormats.MediaType(format), segment.MediaType, format + " media type");
                        }
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "FramesAndClamping", "Frames in jpg and png; EndMs past the end is clamped; StartMs past the end is rejected", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MediaSource video = await SourceAsync(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        ExtractedSegment jpg = await services.Segments.CutAsync(new SegmentRequest { Source = video, StartMs = 5000, EndMs = 1, Output = SegmentOutputEnum.Frame }, settings.Media, ct).ConfigureAwait(false);
                        Check.True(jpg.Data[0] == 0xFF && jpg.Data[1] == 0xD8, "jpeg bytes");
                        Check.True(jpg.StartMs == 5000 && jpg.EndMs == 5000 && jpg.DurationMs == 0, "frame range");
                        Check.Equal("segment-5000.jpg", jpg.Filename, "frame filename");

                        ExtractedSegment png = await services.Segments.CutAsync(new SegmentRequest { Source = video, StartMs = 1000, Output = SegmentOutputEnum.Frame, Format = "png", MaxEdge = 100 }, settings.Media, ct).ConfigureAwait(false);
                        Check.True(png.Data[0] == 0x89 && png.Data[1] == 0x50, "png bytes");
                        MediaProperties props = await ProbeBytesAsync(services, png.Data, "png", ct).ConfigureAwait(false);
                        Check.Equal(100, props.Width, "frame scaled");

                        ExtractedSegment clamped = await services.Segments.CutAsync(new SegmentRequest { Source = video, StartMs = 10000, EndMs = 60000, Output = SegmentOutputEnum.Audio, Format = "wav" }, settings.Media, ct).ConfigureAwait(false);
                        Check.True(clamped.Clamped && clamped.EndMs >= 11900 && clamped.EndMs <= 12100, "end clamped to the duration: " + clamped.EndMs);
                        Check.Equal(clamped.EndMs - 10000, clamped.DurationMs, "duration after clamping");

                        await ExpectAsync<UnsupportedMediaException>(() => services.Segments.CutAsync(new SegmentRequest { Source = video, StartMs = 20000, EndMs = 21000 }, settings.Media, ct), "start past the end").ConfigureAwait(false);
                        await ExpectAsync<RequestValidationException>(() => services.Segments.CutAsync(new SegmentRequest { Source = video, StartMs = 0, EndMs = 700000 }, settings.Media, ct), "longer than MaxSegmentMs").ConfigureAwait(false);
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "MissingStreams", "Audio-only sources cannot give video or frames; silent video cannot give audio; images are rejected", async ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        MediaSource mp3 = await SourceAsync(await TestEnvironment.Mp3WithCoverAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        MediaSource silent = await SourceAsync(await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        MediaSource image = await SourceAsync(await TestEnvironment.PngAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                        await ExpectAsync<UnsupportedMediaException>(() => services.Segments.CutAsync(new SegmentRequest { Source = mp3, StartMs = 0, EndMs = 1000 }, settings.Media, ct), "video from audio").ConfigureAwait(false);
                        await ExpectAsync<UnsupportedMediaException>(() => services.Segments.CutAsync(new SegmentRequest { Source = mp3, StartMs = 0, Output = SegmentOutputEnum.Frame }, settings.Media, ct), "frame from audio with cover art").ConfigureAwait(false);
                        await ExpectAsync<UnsupportedMediaException>(() => services.Segments.CutAsync(new SegmentRequest { Source = silent, StartMs = 0, EndMs = 1000, Output = SegmentOutputEnum.Audio }, settings.Media, ct), "audio from silent video").ConfigureAwait(false);
                        await ExpectAsync<UnsupportedMediaException>(() => services.Segments.CutAsync(new SegmentRequest { Source = image, StartMs = 0, Output = SegmentOutputEnum.Frame }, settings.Media, ct), "image source").ConfigureAwait(false);

                        ExtractedSegment silentClip = await services.Segments.CutAsync(new SegmentRequest { Source = silent, StartMs = 1000, EndMs = 2000 }, settings.Media, ct).ConfigureAwait(false);
                        MediaProperties props = await ProbeBytesAsync(services, silentClip.Data, "mp4", ct).ConfigureAwait(false);
                        Check.True(props.MediaKind == MediaKindEnum.Video && !props.HasAudio, "silent video clip has no audio track");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Route", "POST /v1.0/segment returns binary media with headers, or JSON with base64", async ct =>
                {
                    ServerFixture f = await ServerFixture.SharedAsync(ct).ConfigureAwait(false);
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.VideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);

                    string body = "{\"Source\":{\"Data\":\"" + data + "\"},\"StartMs\":2000,\"EndMs\":4500,\"Output\":\"Audio\"}";
                    using (HttpResponseMessage response = await f.SendAsync(HttpMethod.Post, "/v1.0/segment", ServerFixture.AccessKey, body, ct).ConfigureAwait(false))
                    {
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                        Check.Equal(HttpStatusCode.OK, response.StatusCode, "binary 200");
                        Check.Equal("audio/mpeg", response.Content.Headers.ContentType?.MediaType, "content type");
                        Check.True(bytes.Length > 1000, "media bytes");
                        Check.Equal("2000", Header(response, "x-keyframe-segment-start-ms"), "start header");
                        Check.Equal("4500", Header(response, "x-keyframe-segment-end-ms"), "end header");
                        Check.Equal("2500", Header(response, "x-keyframe-segment-duration-ms"), "duration header");
                        string disposition = response.Content.Headers.ContentDisposition?.ToString() ?? Header(response, "Content-Disposition") ?? String.Empty;
                        Check.True(disposition.Contains("segment-2000-4500.mp3"), "content disposition: " + disposition);
                    }

                    string jsonBody = "{\"Source\":{\"Data\":\"" + data + "\"},\"StartMs\":3000,\"Output\":\"Frame\",\"ResponseFormat\":\"Json\"}";
                    SegmentResponse frame = await f.JsonAsync<SegmentResponse>(HttpMethod.Post, "/v1.0/segment", ServerFixture.AccessKey, jsonBody, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.Equal("image/jpeg", frame.MediaType, "frame media type");
                    Check.Equal(0L, frame.DurationMs, "frame duration");
                    byte[] decoded = Convert.FromBase64String(frame.Data);
                    Check.True(decoded.Length == frame.SizeBytes && decoded[0] == 0xFF, "base64 jpeg");

                    string clamp = "{\"Source\":{\"Data\":\"" + data + "\"},\"StartMs\":11000,\"EndMs\":15000,\"Output\":\"Audio\",\"Format\":\"wav\",\"ResponseFormat\":\"Json\"}";
                    SegmentResponse clamped = await f.JsonAsync<SegmentResponse>(HttpMethod.Post, "/v1.0/segment", ServerFixture.AccessKey, clamp, HttpStatusCode.OK, ct).ConfigureAwait(false);
                    Check.True(clamped.EndMs < 15000 && clamped.DurationMs == clamped.EndMs - 11000, "clamped in JSON");

                    ApiErrorResponse invalid = await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/segment", ServerFixture.AccessKey, "{\"Source\":{\"Data\":\"" + data + "\"},\"StartMs\":5000,\"EndMs\":1000,\"Format\":\"avi\"}", HttpStatusCode.BadRequest, ct).ConfigureAwait(false);
                    Check.True(invalid.Details.Count >= 2, "every validation error: " + String.Join(" | ", invalid.Details));

                    string silent = await TestEnvironment.Base64Async(await TestEnvironment.SilentVideoAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/segment", ServerFixture.AccessKey, "{\"Source\":{\"Data\":\"" + silent + "\"},\"StartMs\":0,\"EndMs\":1000,\"Output\":\"Audio\"}", (HttpStatusCode)422, ct).ConfigureAwait(false);
                    await f.JsonAsync<ApiErrorResponse>(HttpMethod.Post, "/v1.0/segment", null, "{}", HttpStatusCode.Unauthorized, ct).ConfigureAwait(false);
                }, new[] { "server", "ffmpeg" }, skip)
            });
        }

        private static async Task<MediaSource> SourceAsync(string path, CancellationToken token)
        {
            return new MediaSource { Data = await TestEnvironment.Base64Async(path, token).ConfigureAwait(false) };
        }

        private static async Task<MediaProperties> ProbeBytesAsync(AnalysisServices services, byte[] data, string extension, CancellationToken token)
        {
            string path = Path.Combine(TestEnvironment.Root, "segment-" + Guid.NewGuid().ToString("N") + "." + extension);
            await File.WriteAllBytesAsync(path, data, token).ConfigureAwait(false);
            try
            {
                return await services.Prober.ProbeAsync(path, token).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static async Task ExpectAsync<TException>(Func<Task> action, string what) where TException : Exception
        {
            bool threw = false;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (TException)
            {
                threw = true;
            }

            Check.True(threw, what + " throws " + typeof(TException).Name);
        }

        private static string? Header(HttpResponseMessage response, string name)
        {
            IEnumerable<string>? values;
            if (response.Headers.TryGetValues(name, out values)) return values.FirstOrDefault();
            if (response.Content.Headers.TryGetValues(name, out values)) return values.FirstOrDefault();
            return null;
        }
    }
}
