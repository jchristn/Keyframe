namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Requests;

    /// <summary>
    /// Renders the audio clips sent to listening models, separate from transcription chunks. ffmpeg operation
    /// "listen_extract".
    /// </summary>
    public class ListenRenditionExtractor
    {
        #region Private-Members

        private readonly FfmpegRunner _Runner;
        private int _Counter = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public ListenRenditionExtractor(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Render [startMs, endMs) of the first audio stream in the given rendition.
        /// </summary>
        /// <param name="mediaPath">Source media path.</param>
        /// <param name="window">Window to render.</param>
        /// <param name="rendition">Effective rendition (every member filled; see <see cref="RenditionResolver.Resolve"/>).</param>
        /// <param name="workspace">Workspace that receives the rendered file.</param>
        /// <param name="label">Prompt label placed before the clip, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rendered clip.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails or produces no audio.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<InferenceAudio> RenderAsync(
            string mediaPath,
            ClipWindow window,
            AudioRenditionSpec rendition,
            MediaWorkspace workspace,
            string? label,
            CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaPath)) throw new ArgumentNullException(nameof(mediaPath));
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(rendition);
            ArgumentNullException.ThrowIfNull(workspace);

            ListenFormatEnum format = rendition.Format ?? ListenFormatEnum.Mp3;
            int index = Interlocked.Increment(ref _Counter);
            string output = workspace.PathFor("listen_" + index.ToString("D4", CultureInfo.InvariantCulture) + "." + RenditionResolver.Extension(format));

            await _Runner.RunFfmpegCheckedAsync("listen_extract", Arguments(mediaPath, window, rendition, format, output), token).ConfigureAwait(false);

            if (!File.Exists(output)) throw new MediaProcessingException("ffmpeg produced no listening clip.");
            byte[] data = await File.ReadAllBytesAsync(output, token).ConfigureAwait(false);
            if (data.Length < 1) throw new MediaProcessingException("ffmpeg produced an empty listening clip.");

            return new InferenceAudio
            {
                Data = data,
                MediaType = RenditionResolver.MediaType(format),
                Label = label,
                Sha256 = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
                StartMs = window.StartMs,
                EndMs = window.EndMs
            };
        }

        /// <summary>
        /// Render a clip of a span that fits both a listen budget and an inline size limit: the whole span when it fits,
        /// otherwise a centered excerpt, re-rendered shorter once if the rendered bytes still exceed the limit.
        /// </summary>
        /// <param name="mediaPath">Source media path.</param>
        /// <param name="startMs">Span start in milliseconds.</param>
        /// <param name="endMs">Span end in milliseconds.</param>
        /// <param name="budgetMs">Longest clip in milliseconds. Minimum 1000.</param>
        /// <param name="maxWireBytes">Inline bytes available for the clip, counted as encoded on the wire. Minimum 1.</param>
        /// <param name="rendition">Effective rendition.</param>
        /// <param name="workspace">Workspace that receives the rendered file.</param>
        /// <param name="label">Prompt label placed before the clip, or null.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The rendered clip.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null or empty.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails or produces no audio.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<InferenceAudio> RenderFittedAsync(
            string mediaPath,
            long startMs,
            long endMs,
            long budgetMs,
            long maxWireBytes,
            AudioRenditionSpec rendition,
            MediaWorkspace workspace,
            string? label,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(rendition);
            long available = Math.Max(1, maxWireBytes);
            long length = Math.Min(Math.Max(0, endMs - startMs), Math.Max(1000, budgetMs));
            long estimate = RenditionResolver.WireBytes(RenditionResolver.EstimateBytes(rendition, length));
            if (estimate > available) length = Math.Max(1000L, (long)(length * (available * 0.95 / estimate)));

            ClipWindow window = ClipSampler.Centered(startMs, endMs, length);
            InferenceAudio clip = await RenderAsync(mediaPath, window, rendition, workspace, label, token).ConfigureAwait(false);
            long wire = RenditionResolver.WireBytes(clip.Data.Length);
            if (wire <= available) return clip;

            long shorter = Math.Max(1000L, (long)(window.DurationMs * (available * 0.9 / wire)));
            return await RenderAsync(mediaPath, ClipSampler.Centered(startMs, endMs, shorter), rendition, workspace, label, token).ConfigureAwait(false);
        }

        /// <summary>
        /// The ffmpeg arguments for a rendition. Exposed for tests.
        /// </summary>
        /// <param name="mediaPath">Source media path.</param>
        /// <param name="window">Window to render.</param>
        /// <param name="rendition">Effective rendition.</param>
        /// <param name="format">Output format.</param>
        /// <param name="outputPath">Output path.</param>
        /// <returns>Arguments, one per element.</returns>
        /// <exception cref="ArgumentNullException">Thrown when window or rendition is null.</exception>
        public static List<string> Arguments(string mediaPath, ClipWindow window, AudioRenditionSpec rendition, ListenFormatEnum format, string outputPath)
        {
            ArgumentNullException.ThrowIfNull(window);
            ArgumentNullException.ThrowIfNull(rendition);

            List<string> args = new List<string>
            {
                "-y", "-hide_banner", "-nostats", "-loglevel", "error",
                "-ss", Seconds(window.StartMs),
                "-i", mediaPath,
                "-t", Seconds(Math.Max(1, window.DurationMs)),
                "-map", "0:a:0", "-vn", "-sn", "-dn",
                "-ac", (rendition.Channels ?? 1).ToString(CultureInfo.InvariantCulture),
                "-ar", (rendition.SampleRate ?? 16000).ToString(CultureInfo.InvariantCulture)
            };

            switch (format)
            {
                case ListenFormatEnum.Wav:
                    args.AddRange(new[] { "-c:a", "pcm_s16le", "-f", "wav" });
                    break;
                case ListenFormatEnum.Flac:
                    args.AddRange(new[] { "-c:a", "flac", "-f", "flac" });
                    break;
                default:
                    args.AddRange(new[] { "-c:a", "libmp3lame", "-b:a", (rendition.BitrateKbps ?? 64).ToString(CultureInfo.InvariantCulture) + "k", "-f", "mp3" });
                    break;
            }

            args.Add(outputPath);
            return args;
        }

        #endregion

        #region Private-Methods

        private static string Seconds(long ms)
        {
            return (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
        }

        #endregion
    }
}
