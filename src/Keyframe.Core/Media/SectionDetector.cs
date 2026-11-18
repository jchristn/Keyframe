namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Requests;

    /// <summary>
    /// Splits audio into sections at silences with ffmpeg silencedetect, cutting at each silence's midpoint. ffmpeg
    /// operation "section_detect". Models never choose section boundaries.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class SectionDetector
    {
        #region Private-Members

        private readonly FfmpegRunner _Runner;
        private readonly long _MinCutSpacingMs = 250;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public SectionDetector(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Detect sections in the first audio stream of a media file.
        /// </summary>
        /// <param name="path">Media path.</param>
        /// <param name="durationMs">Recording length in milliseconds. Minimum 1.</param>
        /// <param name="spec">Detection options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Sections in order, covering the recording.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path is empty or spec is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when durationMs is below 1.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<List<SceneBoundary>> DetectAsync(string path, long durationMs, SectionDetectionSpec spec, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            ArgumentNullException.ThrowIfNull(spec);
            if (durationMs < 1) throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be positive.");

            string filter = "silencedetect=n=" + spec.SilenceDb.ToString("0.##", CultureInfo.InvariantCulture) + "dB:d=" +
                (spec.MinSilenceMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            FfmpegResult result = await _Runner.RunFfmpegCheckedAsync(
                "section_detect",
                new[] { "-hide_banner", "-nostats", "-i", path, "-map", "0:a:0", "-vn", "-sn", "-dn", "-af", filter, "-f", "null", "-" },
                token).ConfigureAwait(false);

            return BuildSections(ParseCuts(result.StandardError, durationMs), durationMs, spec, _MinCutSpacingMs);
        }

        /// <summary>
        /// Cut points in milliseconds from silencedetect output: the midpoint of each silence that lies strictly inside
        /// the recording. Silences touching either end are not cuts.
        /// </summary>
        /// <param name="stderr">ffmpeg standard error.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <returns>Cuts in order.</returns>
        public static List<long> ParseCuts(string? stderr, long durationMs)
        {
            return AudioMeasurementParser.ParseSilences(stderr, durationMs)
                .Where(s => s.StartMs > 0 && s.EndMs < durationMs)
                .Select(s => s.StartMs + s.DurationMs / 2)
                .ToList();
        }

        /// <summary>
        /// Turn cut points into sections with the shared boundary rules (merge short, split long, limit the count).
        /// </summary>
        /// <param name="cutsMs">Cuts in milliseconds.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <param name="spec">Detection options.</param>
        /// <param name="minCutSpacingMs">Cuts closer than this are collapsed.</param>
        /// <returns>Sections in order.</returns>
        /// <exception cref="ArgumentNullException">Thrown when cutsMs or spec is null.</exception>
        public static List<SceneBoundary> BuildSections(List<long> cutsMs, long durationMs, SectionDetectionSpec spec, long minCutSpacingMs = 250)
        {
            ArgumentNullException.ThrowIfNull(cutsMs);
            ArgumentNullException.ThrowIfNull(spec);
            return BoundaryBuilder.Build(cutsMs, durationMs, spec.MinSectionMs, spec.MaxSectionMs, spec.MaxSections, minCutSpacingMs);
        }

        #endregion
    }
}
