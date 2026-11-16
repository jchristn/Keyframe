namespace Test.Automated
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Media fixtures generated with ffmpeg at run time into a temporary directory.
    /// </summary>
    public class Fixtures : IDisposable
    {
        /// <summary>
        /// Temporary directory holding the fixtures.
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// 640x360 JPEG test pattern.
        /// </summary>
        public string ImagePath { get; }

        /// <summary>
        /// 6 second 320x240 H.264 video with an AAC sine tone.
        /// </summary>
        public string VideoPath { get; }

        /// <summary>
        /// 30 second 320x240 H.264 video with an AAC sine tone, used to keep workers busy.
        /// </summary>
        public string LongVideoPath { get; }

        /// <summary>
        /// 8 second mono MP3 sine tone.
        /// </summary>
        public string AudioPath { get; }

        /// <summary>
        /// Whether ffmpeg produced every fixture.
        /// </summary>
        public bool Available { get; private set; } = false;

        /// <summary>
        /// Reason fixtures are unavailable, or null.
        /// </summary>
        public string? Reason { get; private set; } = null;

        /// <summary>
        /// Create the fixture set (call GenerateAsync to build the files).
        /// </summary>
        public Fixtures()
        {
            Directory = Path.Combine(Path.GetTempPath(), "keyframe-sdk-csharp-" + Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);
            ImagePath = Path.Combine(Directory, "image.jpg");
            VideoPath = Path.Combine(Directory, "video.mp4");
            LongVideoPath = Path.Combine(Directory, "long.mp4");
            AudioPath = Path.Combine(Directory, "audio.mp3");
        }

        /// <summary>
        /// Generate the fixtures with ffmpeg.
        /// </summary>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Task.</returns>
        public async Task GenerateAsync(CancellationToken token)
        {
            string ffmpeg = Environment.GetEnvironmentVariable("FFMPEG") ?? "ffmpeg";
            try
            {
                await RunAsync(ffmpeg, "-y -loglevel error -f lavfi -i testsrc2=size=640x360 -frames:v 1 \"" + ImagePath + "\"", token).ConfigureAwait(false);
                await RunAsync(ffmpeg, Video(6, VideoPath), token).ConfigureAwait(false);
                await RunAsync(ffmpeg, Video(30, LongVideoPath), token).ConfigureAwait(false);
                await RunAsync(ffmpeg, "-y -loglevel error -f lavfi -i sine=frequency=330:duration=8 -ac 1 -c:a libmp3lame -b:a 64k \"" + AudioPath + "\"", token).ConfigureAwait(false);
                Available = File.Exists(ImagePath) && File.Exists(VideoPath) && File.Exists(LongVideoPath) && File.Exists(AudioPath);
                if (!Available) Reason = "ffmpeg did not produce every fixture.";
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException)
            {
                Reason = "ffmpeg unavailable: " + e.Message;
            }
        }

        /// <summary>
        /// Delete the fixture directory.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Delete the fixture directory.
        /// </summary>
        /// <param name="disposing">True when called from Dispose.</param>
        protected virtual void Dispose(bool disposing)
        {
            try
            {
                if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
            }
            catch (IOException)
            {
            }
        }

        private static string Video(int seconds, string path)
        {
            return "-y -loglevel error -f lavfi -i testsrc=duration=" + seconds + ":size=320x240:rate=25 -f lavfi -i sine=frequency=440:duration=" + seconds
                + " -c:v libx264 -pix_fmt yuv420p -c:a aac -shortest \"" + path + "\"";
        }

        private static async Task RunAsync(string file, string args, CancellationToken token)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            };

            using (Process process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start " + file + "."))
            {
                string stderr = await process.StandardError.ReadToEndAsync(token).ConfigureAwait(false);
                await process.WaitForExitAsync(token).ConfigureAwait(false);
                if (process.ExitCode != 0) throw new InvalidOperationException(file + " exited " + process.ExitCode + ": " + stderr);
            }
        }
    }
}
