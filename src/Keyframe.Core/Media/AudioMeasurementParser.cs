namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Parses ffmpeg ebur128, astats, and silencedetect output from standard error into measurements. Pure and static so
    /// tests can feed captured output without ffmpeg. Accepts the output of ffmpeg 6 and later, including the filter
    /// instance naming differences between versions.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class AudioMeasurementParser
    {
        #region Private-Members

        private static readonly Regex _Integrated = new Regex(@"^\s*I:\s+(\S+)\s+LUFS", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex _Range = new Regex(@"^\s*LRA:\s+(\S+)\s+LU\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex _TruePeak = new Regex(@"^\s*Peak:\s+(\S+)\s+dBFS", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex _SilenceStart = new Regex(@"silence_start:\s*(\S+)", RegexOptions.CultureInvariant);
        private static readonly Regex _SilenceEnd = new Regex(@"silence_end:\s*(\S+)", RegexOptions.CultureInvariant);
        private static readonly Regex _AstatsLine = new Regex(@"^\[[^\]]*astats[^\]]*\]\s*(.+?)\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        private const long EdgeToleranceMs = 50;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Parse the output of the measurement pass (ebur128 with peak=true, astats, silencedetect).
        /// </summary>
        /// <param name="stderr">ffmpeg standard error.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <param name="channels">Channel count of the source.</param>
        /// <returns>Measurements without the derived flags; see <see cref="ApplyThresholds"/>.</returns>
        public static AudioMeasurements Parse(string? stderr, long durationMs, int channels)
        {
            string text = stderr ?? String.Empty;
            AudioMeasurements m = new AudioMeasurements { Channels = Math.Max(0, channels) };

            int summary = text.LastIndexOf("Summary:", StringComparison.Ordinal);
            string loudness = summary >= 0 ? text.Substring(summary) : String.Empty;
            m.IntegratedLufs = Number(_Integrated.Match(loudness));
            m.LoudnessRangeLu = Number(_Range.Match(loudness));
            m.TruePeakDbtp = Number(_TruePeak.Match(loudness));

            Dictionary<string, string> overall = AstatsBlock(text, "Overall");
            m.PeakDbfs = Value(overall, "Peak level dB");
            m.RmsDbfs = Value(overall, "RMS level dB");
            double? peakCount = Value(overall, "Peak count");
            m.PeakCount = peakCount.HasValue ? (long)Math.Round(peakCount.Value) : null;
            if (m.PeakDbfs.HasValue && m.RmsDbfs.HasValue) m.CrestFactorDb = Math.Round(m.PeakDbfs.Value - m.RmsDbfs.Value, 2);

            List<ClipWindow> silences = ParseSilences(text, durationMs);
            long silent = silences.Sum(s => s.DurationMs);
            m.SilenceCount = silences.Count;
            m.SilenceRatio = durationMs > 0 ? Math.Round(Math.Min(1.0, silent / (double)durationMs), 4) : 0;
            ClipWindow? first = silences.FirstOrDefault();
            ClipWindow? last = silences.LastOrDefault();
            m.LeadingSilenceMs = first != null && first.StartMs <= EdgeToleranceMs ? first.EndMs : 0;
            m.TrailingSilenceMs = last != null && last.EndMs >= durationMs - EdgeToleranceMs ? Math.Max(0, durationMs - last.StartMs) : 0;
            return m;
        }

        /// <summary>
        /// Parse silencedetect output into silent windows, in order. A silence still open at the end of the output ends
        /// at the recording's end.
        /// </summary>
        /// <param name="stderr">ffmpeg standard error.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <returns>Silent windows.</returns>
        public static List<ClipWindow> ParseSilences(string? stderr, long durationMs)
        {
            List<ClipWindow> windows = new List<ClipWindow>();
            long? open = null;
            foreach (string line in (stderr ?? String.Empty).Split('\n'))
            {
                Match start = _SilenceStart.Match(line);
                if (start.Success)
                {
                    double? s = Parse(start.Groups[1].Value);
                    if (s.HasValue) open = Math.Max(0, (long)Math.Round(s.Value * 1000));
                    continue;
                }

                Match end = _SilenceEnd.Match(line);
                if (end.Success && open.HasValue)
                {
                    double? e = Parse(end.Groups[1].Value);
                    long endMs = e.HasValue ? (long)Math.Round(e.Value * 1000) : durationMs;
                    windows.Add(new ClipWindow(open.Value, Math.Min(Math.Max(endMs, open.Value), Math.Max(durationMs, open.Value))));
                    open = null;
                }
            }

            if (open.HasValue && durationMs > open.Value) windows.Add(new ClipWindow(open.Value, durationMs));
            return windows;
        }

        /// <summary>
        /// Parse the mid/side pass (pan to mid and side, then astats per channel) into a stereo width: side RMS minus mid
        /// RMS in dB. Identical channels (no side signal) return -120.
        /// </summary>
        /// <param name="stderr">ffmpeg standard error.</param>
        /// <returns>Stereo width in dB, or null when the output has no levels.</returns>
        public static double? ParseStereoWidth(string? stderr)
        {
            string text = stderr ?? String.Empty;
            Dictionary<string, string> mid = AstatsBlock(text, "Channel: 1");
            Dictionary<string, string> side = AstatsBlock(text, "Channel: 2");
            if (!mid.ContainsKey("RMS level dB") || !side.ContainsKey("RMS level dB")) return null;
            double? midDb = Value(mid, "RMS level dB");
            double? sideDb = Value(side, "RMS level dB");
            if (!midDb.HasValue) return null;
            if (!sideDb.HasValue) return -120.0;
            return Math.Round(Math.Max(-120.0, sideDb.Value - midDb.Value), 2);
        }

        /// <summary>
        /// Fill the derived flags (ClippingLikely, EffectivelyMono) from the thresholds in media settings.
        /// </summary>
        /// <param name="measurements">Measurements to update.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <param name="media">Media settings holding the thresholds.</param>
        /// <exception cref="ArgumentNullException">Thrown when measurements or media is null.</exception>
        public static void ApplyThresholds(AudioMeasurements measurements, long durationMs, MediaSettings media)
        {
            ArgumentNullException.ThrowIfNull(measurements);
            ArgumentNullException.ThrowIfNull(media);
            double minutes = Math.Max(1, durationMs) / 60000.0;
            double peaksPerMinute = (measurements.PeakCount ?? 0) / minutes;
            measurements.ClippingLikely = measurements.TruePeakDbtp.HasValue
                && measurements.TruePeakDbtp.Value >= media.ClippingTruePeakDbtp
                && peaksPerMinute >= media.ClippingMinPeaksPerMinute;
            measurements.EffectivelyMono = measurements.Channels == 1
                || (measurements.StereoWidthDb.HasValue && measurements.StereoWidthDb.Value <= media.MonoMaxSideDb);
        }

        #endregion

        #region Private-Methods

        private static Dictionary<string, string> AstatsBlock(string text, string header)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool inside = false;
            foreach (Match match in _AstatsLine.Matches(text))
            {
                string body = match.Groups[1].Value;
                bool isHeader = body.StartsWith("Channel:", StringComparison.Ordinal) || String.Equals(body, "Overall", StringComparison.Ordinal);
                if (isHeader)
                {
                    if (inside) break;
                    inside = String.Equals(body, header, StringComparison.Ordinal);
                    continue;
                }

                if (!inside) continue;
                int colon = body.IndexOf(':');
                if (colon <= 0) continue;
                values[body.Substring(0, colon).Trim()] = body.Substring(colon + 1).Trim();
            }

            return values;
        }

        private static double? Value(Dictionary<string, string> block, string key)
        {
            string? raw;
            return block.TryGetValue(key, out raw) ? Parse(raw) : null;
        }

        private static double? Number(Match match)
        {
            return match.Success ? Parse(match.Groups[1].Value) : null;
        }

        private static double? Parse(string raw)
        {
            double value;
            if (!Double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return null;
            if (Double.IsNaN(value) || Double.IsInfinity(value)) return null;
            return value;
        }

        #endregion
    }
}
