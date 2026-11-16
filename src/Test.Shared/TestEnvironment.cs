namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Media;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Shared test environment: temp directories and generated media fixtures. Fixtures are produced with ffmpeg's
    /// synthetic sources, so no binary media is stored in the repository.
    /// </summary>
    public static class TestEnvironment
    {
        #region Public-Members

        /// <summary>
        /// Whether ffmpeg and ffprobe are available.
        /// </summary>
        public static bool FfmpegAvailable
        {
            get
            {
                return _FfmpegAvailable.Value;
            }
        }

        /// <summary>
        /// Skip reason for ffmpeg-dependent cases, or null when ffmpeg is available.
        /// </summary>
        public static string? FfmpegSkipReason
        {
            get
            {
                return FfmpegAvailable ? null : "ffmpeg and ffprobe are not on the PATH.";
            }
        }

        /// <summary>
        /// Root directory for test artifacts.
        /// </summary>
        public static string Root
        {
            get
            {
                return _Root.Value;
            }
        }

        #endregion

        #region Private-Members

        private static readonly Lazy<bool> _FfmpegAvailable = new Lazy<bool>(DetectFfmpeg, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<string> _Root = new Lazy<string>(CreateRoot, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly SemaphoreSlim _FixtureLock = new SemaphoreSlim(1);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Settings suitable for tests: temp under the test root, fast retries, and only the mock endpoints
        /// ("mock" for completion, "mock-stt" for transcription) as defaults. The shipped Ollama and Whisper
        /// defaults are removed so tests never reach a real model server.
        /// </summary>
        /// <returns>Settings.</returns>
        public static KeyframeSettings CreateSettings()
        {
            KeyframeSettings settings = new KeyframeSettings();
            settings.Media.TempDirectory = Path.Combine(Root, "temp");
            settings.Inference.RetryBackoffMs = 0;
            settings.Database.Filename = Path.Combine(Root, "db-" + Guid.NewGuid().ToString("N") + ".db");
            AddMockEndpoints(settings);
            return settings;
        }

        /// <summary>
        /// Replace the configured endpoints with the mock completion and transcription endpoints and make them the defaults.
        /// </summary>
        /// <param name="settings">Settings to change.</param>
        public static void AddMockEndpoints(KeyframeSettings settings)
        {
            settings.Inference.Endpoints = new List<EndpointSettings>
            {
                new EndpointSettings { Name = "mock", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "mock-vision", MaxConcurrentRequests = 4 },
                new EndpointSettings { Name = "mock-stt", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Transcription, Model = "mock-whisper", MaxConcurrentRequests = 4 }
            };
            settings.Inference.DefaultVision = "mock";
            settings.Inference.DefaultText = null;
            settings.Inference.DefaultTranscription = "mock-stt";
        }

        /// <summary>
        /// Path to a generated 1280x720 PNG with an alpha channel.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> PngAsync(CancellationToken token)
        {
            return FixtureAsync("image.png", new[] { "-f", "lavfi", "-i", "testsrc2=size=1280x720,format=rgba", "-frames:v", "1" }, token);
        }

        /// <summary>
        /// Path to a generated 2400x1600 JPEG.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> JpegAsync(CancellationToken token)
        {
            return FixtureAsync("image.jpg", new[] { "-f", "lavfi", "-i", "testsrc2=size=2400x1600", "-frames:v", "1", "-q:v", "4" }, token);
        }

        /// <summary>
        /// Path to a generated 12 second MP4 with three visually distinct 4 second scenes and a sine-wave audio track.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> VideoAsync(CancellationToken token)
        {
            return FixtureAsync("video.mp4", new[]
            {
                "-f", "lavfi", "-i", "testsrc=duration=4:size=640x360:rate=25",
                "-f", "lavfi", "-i", "color=c=red:duration=4:size=640x360:rate=25",
                "-f", "lavfi", "-i", "mandelbrot=size=640x360:rate=25",
                "-f", "lavfi", "-i", "sine=frequency=440:duration=12",
                "-filter_complex", "[2:v]trim=duration=4,setpts=PTS-STARTPTS[m];[0:v][1:v][m]concat=n=3:v=1:a=0[v]",
                "-map", "[v]", "-map", "3:a",
                "-c:v", "libx264", "-pix_fmt", "yuv420p", "-c:a", "aac", "-shortest"
            }, token);
        }

        /// <summary>
        /// Path to a generated 6 second MP4 with no audio track.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> SilentVideoAsync(CancellationToken token)
        {
            return FixtureAsync("silent.mp4", new[]
            {
                "-f", "lavfi", "-i", "testsrc=duration=6:size=320x240:rate=25",
                "-c:v", "libx264", "-pix_fmt", "yuv420p"
            }, token);
        }

        /// <summary>
        /// Path to a generated 25 second 44.1 kHz stereo WAV (a 440 Hz sine wave).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> WavAsync(CancellationToken token)
        {
            return FixtureAsync("audio.wav", new[] { "-f", "lavfi", "-i", "sine=frequency=440:duration=25:sample_rate=44100", "-ac", "2", "-c:a", "pcm_s16le" }, token);
        }

        /// <summary>
        /// Path to a generated 8 second MP3 (a 660 Hz sine wave).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> Mp3Async(CancellationToken token)
        {
            return FixtureAsync("audio.mp3", new[] { "-f", "lavfi", "-i", "sine=frequency=660:duration=8", "-c:a", "libmp3lame", "-q:a", "6" }, token);
        }

        /// <summary>
        /// Path to a generated 6 second MP3 with embedded cover art (an attached picture stream).
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path.</returns>
        public static Task<string> Mp3WithCoverAsync(CancellationToken token)
        {
            return FixtureAsync("cover.mp3", new[]
            {
                "-f", "lavfi", "-i", "sine=frequency=550:duration=6",
                "-f", "lavfi", "-i", "testsrc2=size=300x300:duration=0.04:rate=25,format=yuvj420p",
                "-map", "0:a", "-map", "1:v",
                "-c:a", "libmp3lame", "-q:a", "6", "-c:v", "mjpeg",
                "-disposition:v:0", "attached_pic", "-id3v2_version", "3"
            }, token);
        }

        /// <summary>
        /// Path to a generated speech clip (16 kHz mono WAV) made with the macOS "say" command, or null when "say"
        /// is not available. Used by live transcription tests.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Path, or null.</returns>
        public static async Task<string?> SpeechAsync(CancellationToken token)
        {
            string wav = Path.Combine(Root, "fixtures", "speech.wav");
            if (File.Exists(wav)) return wav;
            Directory.CreateDirectory(Path.GetDirectoryName(wav)!);
            string aiff = Path.Combine(Root, "fixtures", "speech-" + Guid.NewGuid().ToString("N") + ".aiff");

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("say") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                psi.ArgumentList.Add("-o");
                psi.ArgumentList.Add(aiff);
                psi.ArgumentList.Add(SpeechText);
                using (Process? process = Process.Start(psi))
                {
                    if (process == null) return null;
                    await process.WaitForExitAsync(token).ConfigureAwait(false);
                    if (process.ExitCode != 0 || !File.Exists(aiff)) return null;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null;
            }

            FfmpegRunner runner = new FfmpegRunner();
            await runner.RunFfmpegCheckedAsync("fixture", new[] { "-y", "-hide_banner", "-loglevel", "error", "-i", aiff, "-ar", "16000", "-ac", "1", wav }, token).ConfigureAwait(false);
            File.Delete(aiff);
            return wav;
        }

        /// <summary>
        /// The sentence spoken in <see cref="SpeechAsync"/>.
        /// </summary>
        public static string SpeechText
        {
            get
            {
                return "Keyframe turns audio into searchable text. The weather today is sunny and warm.";
            }
        }

        /// <summary>
        /// Read a fixture as base64.
        /// </summary>
        /// <param name="path">Path.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Base64 text.</returns>
        public static async Task<string> Base64Async(string path, CancellationToken token)
        {
            byte[] data = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            return Convert.ToBase64String(data);
        }

        #endregion

        #region Private-Methods

        private static async Task<string> FixtureAsync(string name, string[] inputArgs, CancellationToken token)
        {
            string path = Path.Combine(Root, "fixtures", name);
            await _FixtureLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                if (File.Exists(path)) return path;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                FfmpegRunner runner = new FfmpegRunner();
                string[] args = new string[inputArgs.Length + 5];
                args[0] = "-y";
                args[1] = "-hide_banner";
                args[2] = "-loglevel";
                args[3] = "error";
                Array.Copy(inputArgs, 0, args, 4, inputArgs.Length);
                args[args.Length - 1] = path;
                await runner.RunFfmpegCheckedAsync("fixture", args, token).ConfigureAwait(false);
                return path;
            }
            finally
            {
                _FixtureLock.Release();
            }
        }

        private static bool DetectFfmpeg()
        {
            return CanRun("ffmpeg") && CanRun("ffprobe");
        }

        private static bool CanRun(string executable)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(executable)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-version");

                using (Process? process = Process.Start(psi))
                {
                    if (process == null) return false;
                    process.StandardOutput.ReadToEnd();
                    process.WaitForExit(10000);
                    return process.HasExited && process.ExitCode == 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string CreateRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "keyframe-tests", DateTime.UtcNow.ToString("yyyyMMdd"));
            Directory.CreateDirectory(root);
            return root;
        }

        #endregion
    }
}
