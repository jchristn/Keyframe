namespace Test.Shared
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
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
        /// Settings suitable for tests: temp under the test root, fast retries.
        /// </summary>
        /// <returns>Settings.</returns>
        public static KeyframeSettings CreateSettings()
        {
            KeyframeSettings settings = new KeyframeSettings();
            settings.Media.TempDirectory = Path.Combine(Root, "temp");
            settings.Inference.RetryBackoffMs = 0;
            settings.Database.Filename = Path.Combine(Root, "db-" + Guid.NewGuid().ToString("N") + ".db");
            return settings;
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
