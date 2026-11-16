namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Helpers;

    /// <summary>
    /// Re-encodes images (or single video frames) as size-capped JPEG for models and thumbnails.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class ImageNormalizer
    {
        #region Private-Members

        private readonly FfmpegRunner _Runner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the normalizer.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public ImageNormalizer(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Encode the first frame of an image file as JPEG with the longest edge capped.
        /// </summary>
        /// <param name="inputPath">Input image path.</param>
        /// <param name="outputPath">Output JPEG path.</param>
        /// <param name="maxEdge">Longest edge in pixels. Smaller images are not upscaled.</param>
        /// <param name="quality">ffmpeg JPEG quality scale (1 best, 31 worst).</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Normalized image.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a path is null or empty.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        public async Task<NormalizedImage> NormalizeAsync(string inputPath, string outputPath, int maxEdge, int quality, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(inputPath)) throw new ArgumentNullException(nameof(inputPath));
            if (String.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));

            List<string> args = new List<string> { "-y", "-hide_banner", "-nostats", "-loglevel", "error", "-i", inputPath };
            args.AddRange(EncodeArguments(maxEdge, quality));
            args.Add(outputPath);

            await _Runner.RunFfmpegCheckedAsync("normalize", args, token).ConfigureAwait(false);
            return await ReadAsync(outputPath, null, token).ConfigureAwait(false);
        }

        /// <summary>
        /// Arguments that encode one frame as size-capped JPEG. Shared with frame extraction.
        /// </summary>
        /// <param name="maxEdge">Longest edge in pixels.</param>
        /// <param name="quality">ffmpeg JPEG quality scale.</param>
        /// <returns>Arguments.</returns>
        public static List<string> EncodeArguments(int maxEdge, int quality)
        {
            int edge = Math.Clamp(maxEdge, 16, 16384);
            int q = Math.Clamp(quality, 1, 31);
            string e = edge.ToString(CultureInfo.InvariantCulture);
            return new List<string>
            {
                "-frames:v", "1",
                "-vf", "scale='min(iw," + e + ")':'min(ih," + e + ")':force_original_aspect_ratio=decrease,format=yuvj420p",
                "-q:v", q.ToString(CultureInfo.InvariantCulture),
                "-f", "image2",
                "-c:v", "mjpeg"
            };
        }

        /// <summary>
        /// Read an encoded JPEG into a normalized image.
        /// </summary>
        /// <param name="path">JPEG path.</param>
        /// <param name="timestampMs">Frame timestamp, when the image came from a video.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Normalized image.</returns>
        /// <exception cref="MediaProcessingException">Thrown when the file is missing or empty.</exception>
        public static async Task<NormalizedImage> ReadAsync(string path, long? timestampMs, CancellationToken token = default)
        {
            if (!File.Exists(path)) throw new MediaProcessingException("ffmpeg did not produce '" + System.IO.Path.GetFileName(path) + "'.");
            byte[] data = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            if (data.Length < 1) throw new MediaProcessingException("ffmpeg produced an empty image '" + System.IO.Path.GetFileName(path) + "'.");

            JpegDimensions.TryRead(data, out int width, out int height);
            return new NormalizedImage
            {
                Path = path,
                Data = data,
                MediaType = "image/jpeg",
                Width = width,
                Height = height,
                Sha256 = HashHelper.Sha256Hex(data),
                TimestampMs = timestampMs
            };
        }

        #endregion
    }
}
