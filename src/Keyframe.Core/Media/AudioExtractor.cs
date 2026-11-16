namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;

    /// <summary>
    /// Extracts the audio track as 16 kHz mono FLAC split into fixed-length chunks.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AudioExtractor
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
        public AudioExtractor(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Extract audio chunks.
        /// </summary>
        /// <param name="mediaPath">Media path. Must contain an audio stream.</param>
        /// <param name="outputDirectory">Directory for chunk files.</param>
        /// <param name="chunkSeconds">Chunk length in seconds.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Chunks in order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a path is null or empty.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails or produces no audio.</exception>
        public async Task<List<AudioChunk>> ExtractAsync(string mediaPath, string outputDirectory, int chunkSeconds, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaPath)) throw new ArgumentNullException(nameof(mediaPath));
            if (String.IsNullOrEmpty(outputDirectory)) throw new ArgumentNullException(nameof(outputDirectory));

            int seconds = Math.Clamp(chunkSeconds, 1, 86400);
            string pattern = System.IO.Path.Combine(outputDirectory, "chunk_%05d.flac");

            await _Runner.RunFfmpegCheckedAsync(
                "audio_extract",
                new[]
                {
                    "-y", "-hide_banner", "-nostats", "-loglevel", "error",
                    "-i", mediaPath,
                    "-vn", "-sn", "-dn",
                    "-ac", "1", "-ar", "16000",
                    "-c:a", "flac",
                    "-f", "segment",
                    "-segment_time", seconds.ToString(CultureInfo.InvariantCulture),
                    "-reset_timestamps", "1",
                    pattern
                },
                token).ConfigureAwait(false);

            List<string> files = Directory.GetFiles(outputDirectory, "chunk_*.flac").OrderBy(f => f, StringComparer.Ordinal).ToList();
            if (files.Count < 1) throw new MediaProcessingException("ffmpeg produced no audio chunks.");

            List<AudioChunk> chunks = new List<AudioChunk>();
            for (int i = 0; i < files.Count; i++)
            {
                FileInfo info = new FileInfo(files[i]);
                chunks.Add(new AudioChunk
                {
                    Index = i,
                    Path = files[i],
                    OffsetMs = (long)i * seconds * 1000L,
                    SizeBytes = info.Length
                });
            }

            return chunks;
        }

        #endregion
    }
}
