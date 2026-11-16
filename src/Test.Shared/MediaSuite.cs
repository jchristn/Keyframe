namespace Test.Shared
{
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Helpers;
    using Keyframe.Core.Media;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Media detection, scene rules, and the ffmpeg pipeline on generated fixtures.
    /// </summary>
    public static class MediaSuite
    {
        private const string Id = "Media";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "Media pipeline", new[]
            {
                new TestCaseDescriptor(Id, "MagicBytes", "Magic-byte detection recognizes common formats", ct =>
                {
                    Check.Equal(MediaKindEnum.Image, MediaTypeDetector.Detect(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }).MediaKind, "jpeg");
                    Check.Equal("image/png", MediaTypeDetector.Detect(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }).ContentType, "png");
                    byte[] mp4 = Encoding.ASCII.GetBytes("\0\0\0 ftypisom\0\0\0\0");
                    Check.Equal(MediaKindEnum.Video, MediaTypeDetector.Detect(mp4).MediaKind, "mp4");
                    byte[] heic = Encoding.ASCII.GetBytes("\0\0\0 ftypheic\0\0\0\0");
                    Check.Equal(MediaKindEnum.Image, MediaTypeDetector.Detect(heic).MediaKind, "heic is an image");
                    Check.Equal(MediaKindEnum.Unknown, MediaTypeDetector.Detect(Encoding.ASCII.GetBytes("Gibberish text")).MediaKind, "text is unknown");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "UrlGuard", "Restricted addresses are recognized", ct =>
                {
                    Check.True(UrlGuard.IsRestricted(IPAddress.Parse("127.0.0.1")), "loopback");
                    Check.True(UrlGuard.IsRestricted(IPAddress.Parse("10.1.2.3")), "10/8");
                    Check.True(UrlGuard.IsRestricted(IPAddress.Parse("172.20.0.1")), "172.16/12");
                    Check.True(UrlGuard.IsRestricted(IPAddress.Parse("169.254.169.254")), "metadata service");
                    Check.True(UrlGuard.IsRestricted(IPAddress.Parse("::1")), "ipv6 loopback");
                    Check.True(UrlGuard.IsRestricted(IPAddress.Parse("fd00::1")), "ipv6 unique local");
                    Check.True(!UrlGuard.IsRestricted(IPAddress.Parse("8.8.8.8")), "public");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "SceneRules", "Scene building merges short, splits long, and caps count", ct =>
                {
                    SceneDetectionSpec spec = new SceneDetectionSpec { MinSceneMs = 2000, MaxSceneMs = 10000, MaxScenes = 200 };
                    List<SceneBoundary> scenes = SceneDetector.BuildScenes(new List<long> { 4000, 4500, 8000 }, 30000, spec);
                    Check.Equal(0L, scenes[0].StartMs, "starts at zero");
                    Check.Equal(30000L, scenes[scenes.Count - 1].EndMs, "ends at duration");
                    Check.True(scenes.All(s => s.DurationMs >= 2000), "no short scenes");
                    Check.True(scenes.All(s => s.DurationMs <= 10000), "no long scenes");
                    for (int i = 1; i < scenes.Count; i++) Check.Equal(scenes[i - 1].EndMs, scenes[i].StartMs, "contiguous");

                    spec.MaxScenes = 2;
                    Check.Equal(2, SceneDetector.BuildScenes(new List<long> { 4000, 8000, 12000 }, 16000, spec).Count, "count cap");
                    Check.Equal(1, SceneDetector.BuildScenes(new List<long>(), 5000, new SceneDetectionSpec()).Count, "no cuts gives one scene");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ParseShowinfo", "Cut times are parsed from showinfo output", ct =>
                {
                    string stderr = "[Parsed_showinfo_2 @ 0x1] n:   0 pts: 102400 pts_time:4       duration:512\n" +
                                    "[Parsed_showinfo_2 @ 0x1] n:   1 pts: 204800 pts_time:8.04    duration:512\n" +
                                    "frame=  2 fps=0.0 q=-0.0 Lsize=N/A time=00:00:11.96\n";
                    List<long> cuts = SceneDetector.ParseCuts(stderr);
                    Check.Equal(2, cuts.Count, "cut count");
                    Check.Equal(8040L, cuts[1], "second cut");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "ProbeImage", "ffprobe reports PNG properties", async ct =>
                {
                    string png = await TestEnvironment.PngAsync(ct).ConfigureAwait(false);
                    MediaProber prober = new MediaProber(new FfmpegRunner());
                    MediaProperties props = await prober.ProbeAsync(png, ct).ConfigureAwait(false);
                    Check.Equal(MediaKindEnum.Image, props.MediaKind, "kind");
                    Check.Equal(1280, props.Width, "width");
                    Check.Equal(null, props.DurationMs, "images have no duration");
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "ProbeVideo", "ffprobe reports video and audio properties", async ct =>
                {
                    string mp4 = await TestEnvironment.VideoAsync(ct).ConfigureAwait(false);
                    MediaProperties props = await new MediaProber(new FfmpegRunner()).ProbeAsync(mp4, ct).ConfigureAwait(false);
                    Check.Equal(MediaKindEnum.Video, props.MediaKind, "kind");
                    Check.True(props.DurationMs >= 11500 && props.DurationMs <= 12500, "duration about 12s: " + props.DurationMs);
                    Check.Equal(true, props.HasAudio, "audio");
                    Check.Equal(25.0, props.FrameRate, "frame rate");
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Normalize", "Images are re-encoded as JPEG within the edge limit", async ct =>
                {
                    string jpg = await TestEnvironment.JpegAsync(ct).ConfigureAwait(false);
                    using (MediaWorkspace ws = new MediaWorkspace(Path.Combine(TestEnvironment.Root, "temp")))
                    {
                        NormalizedImage img = await new ImageNormalizer(new FfmpegRunner()).NormalizeAsync(jpg, ws.PathFor("n.jpg"), 800, 3, ct).ConfigureAwait(false);
                        Check.Equal(800, img.Width, "width capped");
                        Check.Equal(533, img.Height, "aspect kept");
                        Check.Equal(64, img.Sha256.Length, "hash");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "DetectScenes", "ffmpeg finds the cuts in the three-scene fixture", async ct =>
                {
                    string mp4 = await TestEnvironment.VideoAsync(ct).ConfigureAwait(false);
                    List<SceneBoundary> scenes = await new SceneDetector(new FfmpegRunner()).DetectAsync(mp4, 12000, new SceneDetectionSpec(), ct).ConfigureAwait(false);
                    Check.Equal(3, scenes.Count, "scene count");
                    Check.True(System.Math.Abs(scenes[1].StartMs - 4000) <= 100, "first cut near 4s: " + scenes[1].StartMs);
                    Check.True(System.Math.Abs(scenes[2].StartMs - 8000) <= 100, "second cut near 8s: " + scenes[2].StartMs);
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Frames", "Frames are extracted at requested timestamps", async ct =>
                {
                    string mp4 = await TestEnvironment.VideoAsync(ct).ConfigureAwait(false);
                    using (MediaWorkspace ws = new MediaWorkspace(Path.Combine(TestEnvironment.Root, "temp")))
                    {
                        List<NormalizedImage> frames = await new FrameExtractor(new FfmpegRunner())
                            .ExtractAsync(mp4, new long[] { 1000, 5000, 11500 }, ws.CreateSubdirectory("f"), 320, 3, 2, ct).ConfigureAwait(false);
                        Check.Equal(3, frames.Count, "frame count");
                        Check.Equal(5000L, frames[1].TimestampMs, "order preserved");
                        Check.Equal(320, frames[0].Width, "scaled");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "Audio", "Audio is split into fixed-length chunks", async ct =>
                {
                    string mp4 = await TestEnvironment.VideoAsync(ct).ConfigureAwait(false);
                    using (MediaWorkspace ws = new MediaWorkspace(Path.Combine(TestEnvironment.Root, "temp")))
                    {
                        List<AudioChunk> chunks = await new AudioExtractor(new FfmpegRunner()).ExtractAsync(mp4, ws.CreateSubdirectory("a"), 5, ct).ConfigureAwait(false);
                        Check.Equal(3, chunks.Count, "chunk count");
                        Check.Equal(10000L, chunks[2].OffsetMs, "third chunk offset");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "ResolveBase64", "Base64 media resolves, is hashed, and kind-checked", async ct =>
                {
                    string png = await TestEnvironment.PngAsync(ct).ConfigureAwait(false);
                    string data = "data:image/png;base64," + await TestEnvironment.Base64Async(png, ct).ConfigureAwait(false);
                    using (MediaResolver resolver = new MediaResolver(new MediaSettings()))
                    using (MediaWorkspace ws = new MediaWorkspace(Path.Combine(TestEnvironment.Root, "temp")))
                    {
                        ResolvedMedia media = await resolver.ResolveAsync(new MediaSource { Data = data }, MediaKindEnum.Image, ws, ct).ConfigureAwait(false);
                        Check.Equal("image/png", media.ContentType, "type");
                        Check.True(media.Path.EndsWith(".png"), "extension");

                        bool threw = false;
                        try
                        {
                            await resolver.ResolveAsync(new MediaSource { Data = data }, MediaKindEnum.Video, ws, ct).ConfigureAwait(false);
                        }
                        catch (UnsupportedMediaException)
                        {
                            threw = true;
                        }

                        Check.True(threw, "image rejected as video");
                    }
                }, new[] { "ffmpeg" }, skip),

                new TestCaseDescriptor(Id, "ResolveBlocksPrivate", "URLs that resolve to private addresses are refused", async ct =>
                {
                    using (MediaResolver resolver = new MediaResolver(new MediaSettings()))
                    using (MediaWorkspace ws = new MediaWorkspace(Path.Combine(TestEnvironment.Root, "temp")))
                    {
                        bool threw = false;
                        try
                        {
                            await resolver.ResolveAsync(new MediaSource { Url = "http://127.0.0.1:9/x.jpg" }, MediaKindEnum.Image, ws, ct).ConfigureAwait(false);
                        }
                        catch (MediaResolutionException ex)
                        {
                            threw = ex.Message.Contains("restricted network address");
                        }

                        Check.True(threw, "loopback URL refused by the connect guard");
                    }
                }, new[] { "fast" })
            });
        }
    }
}
