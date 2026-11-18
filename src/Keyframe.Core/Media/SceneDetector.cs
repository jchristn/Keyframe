namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Requests;

    /// <summary>
    /// Detects scene boundaries with ffmpeg's scene-change score and applies minimum, maximum, and count rules.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class SceneDetector
    {
        #region Public-Members

        /// <summary>
        /// Width frames are downscaled to before scoring, which speeds detection on large videos. Default 320.
        /// Minimum 64, maximum 1920.
        /// </summary>
        public int AnalysisWidth
        {
            get
            {
                return _AnalysisWidth;
            }
            set
            {
                _AnalysisWidth = Math.Clamp(value, 64, 1920);
            }
        }

        /// <summary>
        /// Cuts closer together than this, in milliseconds, are collapsed. Default 250. Minimum 0, maximum 10000.
        /// </summary>
        public long MinCutSpacingMs
        {
            get
            {
                return _MinCutSpacingMs;
            }
            set
            {
                _MinCutSpacingMs = Math.Clamp(value, 0L, 10000L);
            }
        }

        #endregion

        #region Private-Members

        private static readonly Regex _PtsTime = new Regex("pts_time:\\s*([0-9]+(?:\\.[0-9]+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private readonly FfmpegRunner _Runner;
        private int _AnalysisWidth = 320;
        private long _MinCutSpacingMs = 250;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the detector.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public SceneDetector(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Detect scenes in a video.
        /// </summary>
        /// <param name="path">Video path.</param>
        /// <param name="durationMs">Video duration in milliseconds.</param>
        /// <param name="spec">Detection options.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Scenes in order, covering the whole video.</returns>
        /// <exception cref="ArgumentNullException">Thrown when path or spec is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when durationMs is not positive.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        public async Task<List<SceneBoundary>> DetectAsync(string path, long durationMs, SceneDetectionSpec spec, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            ArgumentNullException.ThrowIfNull(spec);
            if (durationMs < 1) throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be positive.");

            string threshold = spec.Threshold.ToString("0.###", CultureInfo.InvariantCulture);
            string filter = "scale=" + _AnalysisWidth.ToString(CultureInfo.InvariantCulture) + ":-2,select='gt(scene," + threshold + ")',showinfo";

            FfmpegResult result = await _Runner.RunFfmpegCheckedAsync(
                "scene_detect",
                new[] { "-hide_banner", "-nostats", "-i", path, "-an", "-sn", "-dn", "-vf", filter, "-f", "null", "-" },
                token).ConfigureAwait(false);

            List<long> cuts = ParseCuts(result.StandardError);
            return BuildScenes(cuts, durationMs, spec, _MinCutSpacingMs);
        }

        /// <summary>
        /// Parse cut timestamps in milliseconds from ffmpeg showinfo output.
        /// </summary>
        /// <param name="stderr">ffmpeg standard error.</param>
        /// <returns>Cut timestamps, unsorted.</returns>
        public static List<long> ParseCuts(string stderr)
        {
            List<long> cuts = new List<long>();
            if (String.IsNullOrEmpty(stderr)) return cuts;

            foreach (string line in stderr.Split('\n'))
            {
                if (!line.Contains("showinfo", StringComparison.Ordinal)) continue;
                Match m = _PtsTime.Match(line);
                if (m.Success && Double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                    cuts.Add((long)Math.Round(seconds * 1000.0));
            }

            return cuts;
        }

        /// <summary>
        /// Turn cut timestamps into scenes: drop out-of-range and near-duplicate cuts, merge scenes shorter than the
        /// minimum, split scenes longer than the maximum, then merge the shortest adjacent pairs until the scene count
        /// is within the limit. The count limit takes precedence over the maximum length.
        /// </summary>
        /// <param name="cutsMs">Cut timestamps in milliseconds.</param>
        /// <param name="durationMs">Video duration in milliseconds.</param>
        /// <param name="spec">Detection options.</param>
        /// <param name="minCutSpacingMs">Cuts closer than this are collapsed.</param>
        /// <returns>Scenes in order, covering 0 to durationMs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when cutsMs or spec is null.</exception>
        public static List<SceneBoundary> BuildScenes(List<long> cutsMs, long durationMs, SceneDetectionSpec spec, long minCutSpacingMs = 250)
        {
            ArgumentNullException.ThrowIfNull(cutsMs);
            ArgumentNullException.ThrowIfNull(spec);
            if (durationMs < 1) return new List<SceneBoundary>();
            return BoundaryBuilder.Build(cutsMs, durationMs, spec.MinSceneMs, spec.MaxSceneMs, spec.MaxScenes, minCutSpacingMs);
        }

        #endregion
    }
}
