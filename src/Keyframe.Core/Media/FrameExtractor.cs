namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;

    /// <summary>
    /// Extracts single frames from a video as size-capped JPEG.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class FrameExtractor
    {
        #region Private-Members

        private readonly FfmpegRunner _Runner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the extractor.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public FrameExtractor(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract frames at the given timestamps.
        /// </summary>
        /// <param name="videoPath">Video path.</param>
        /// <param name="timestampsMs">Timestamps in milliseconds.</param>
        /// <param name="outputDirectory">Directory for JPEG output.</param>
        /// <param name="maxEdge">Longest edge in pixels.</param>
        /// <param name="quality">ffmpeg JPEG quality scale.</param>
        /// <param name="maxParallel">Maximum concurrent ffmpeg processes.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Frames in the order of the timestamps.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        public async Task<List<NormalizedImage>> ExtractAsync(
            string videoPath,
            IEnumerable<long> timestampsMs,
            string outputDirectory,
            int maxEdge,
            int quality,
            int maxParallel,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(videoPath)) throw new ArgumentNullException(nameof(videoPath));
            ArgumentNullException.ThrowIfNull(timestampsMs);
            if (String.IsNullOrEmpty(outputDirectory)) throw new ArgumentNullException(nameof(outputDirectory));

            List<long> stamps = timestampsMs.ToList();
            NormalizedImage[] frames = new NormalizedImage[stamps.Count];

            using (SemaphoreSlim gate = new SemaphoreSlim(Math.Max(1, maxParallel)))
            {
                List<Task> tasks = new List<Task>();
                for (int i = 0; i < stamps.Count; i++)
                {
                    int index = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        await gate.WaitAsync(token).ConfigureAwait(false);
                        try
                        {
                            frames[index] = await ExtractOneAsync(videoPath, stamps[index], outputDirectory, maxEdge, quality, token).ConfigureAwait(false);
                        }
                        finally
                        {
                            gate.Release();
                        }
                    }, token));
                }

                await Task.WhenAll(tasks).ConfigureAwait(false);
            }

            return frames.ToList();
        }

        #endregion

        #region Private-Methods

        private async Task<NormalizedImage> ExtractOneAsync(string videoPath, long timestampMs, string outputDirectory, int maxEdge, int quality, CancellationToken token)
        {
            string output = System.IO.Path.Combine(outputDirectory, "frame_" + timestampMs.ToString("D10", CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".jpg");
            string seek = (timestampMs / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

            List<string> args = new List<string> { "-y", "-hide_banner", "-nostats", "-loglevel", "error", "-ss", seek, "-i", videoPath, "-an", "-sn" };
            args.AddRange(ImageNormalizer.EncodeArguments(maxEdge, quality));
            args.Add(output);

            await _Runner.RunFfmpegCheckedAsync("frame_extract", args, token).ConfigureAwait(false);
            return await ImageNormalizer.ReadAsync(output, timestampMs, token).ConfigureAwait(false);
        }

        #endregion
    }
}
