namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Measures loudness, levels, silence, and stereo width with ffmpeg: one decode pass with ebur128, astats, and
    /// silencedetect, plus a mid/side astats pass for stereo sources. ffmpeg operation "audio_measure". No model calls.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class AudioMeasurer
    {
        #region Private-Members

        private readonly FfmpegRunner _Runner;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="runner">ffmpeg runner.</param>
        /// <exception cref="ArgumentNullException">Thrown when runner is null.</exception>
        public AudioMeasurer(FfmpegRunner runner)
        {
            ArgumentNullException.ThrowIfNull(runner);
            _Runner = runner;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Measure the first audio stream of a media file.
        /// </summary>
        /// <param name="mediaPath">Media path.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <param name="channels">Channel count of the source.</param>
        /// <param name="media">Media settings holding the silence and threshold settings.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Measurements.</returns>
        /// <exception cref="ArgumentNullException">Thrown when mediaPath is empty or media is null.</exception>
        /// <exception cref="MediaProcessingException">Thrown when ffmpeg fails.</exception>
        /// <exception cref="OperationCanceledException">Thrown when canceled.</exception>
        public async Task<AudioMeasurements> MeasureAsync(string mediaPath, long durationMs, int channels, MediaSettings media, CancellationToken token = default)
        {
            if (String.IsNullOrEmpty(mediaPath)) throw new ArgumentNullException(nameof(mediaPath));
            ArgumentNullException.ThrowIfNull(media);

            FfmpegResult main = await _Runner.RunFfmpegCheckedAsync("audio_measure", MeasureArguments(mediaPath, media), token).ConfigureAwait(false);
            AudioMeasurements measurements = AudioMeasurementParser.Parse(main.StandardError, durationMs, channels);

            if (channels == 2)
            {
                FfmpegResult width = await _Runner.RunFfmpegCheckedAsync("audio_measure", WidthArguments(mediaPath), token).ConfigureAwait(false);
                measurements.StereoWidthDb = AudioMeasurementParser.ParseStereoWidth(width.StandardError);
            }

            AudioMeasurementParser.ApplyThresholds(measurements, durationMs, media);
            return measurements;
        }

        /// <summary>
        /// Arguments of the measurement pass. Exposed for tests.
        /// </summary>
        /// <param name="mediaPath">Media path.</param>
        /// <param name="media">Media settings.</param>
        /// <returns>Arguments, one per element.</returns>
        /// <exception cref="ArgumentNullException">Thrown when media is null.</exception>
        public static List<string> MeasureArguments(string mediaPath, MediaSettings media)
        {
            ArgumentNullException.ThrowIfNull(media);
            string noise = media.MeasureSilenceDb.ToString("0.##", CultureInfo.InvariantCulture) + "dB";
            string duration = (media.MeasureMinSilenceMs / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
            return new List<string>
            {
                "-hide_banner", "-nostats", "-i", mediaPath,
                "-map", "0:a:0", "-vn", "-sn", "-dn",
                "-af", "ebur128=peak=true:framelog=verbose,astats,silencedetect=n=" + noise + ":d=" + duration,
                "-f", "null", "-"
            };
        }

        /// <summary>
        /// Arguments of the mid/side pass. Exposed for tests.
        /// </summary>
        /// <param name="mediaPath">Media path.</param>
        /// <returns>Arguments, one per element.</returns>
        public static List<string> WidthArguments(string mediaPath)
        {
            return new List<string>
            {
                "-hide_banner", "-nostats", "-i", mediaPath,
                "-map", "0:a:0", "-vn", "-sn", "-dn",
                "-af", "pan=stereo|c0=0.5*c0+0.5*c1|c1=0.5*c0-0.5*c1,astats=measure_perchannel=RMS_level+Peak_level:measure_overall=none",
                "-f", "null", "-"
            };
        }

        #endregion
    }
}
