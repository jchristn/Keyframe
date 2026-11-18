namespace Keyframe.Core.Analysis
{
    using System;
    using System.Globalization;

    /// <summary>
    /// Formats media offsets for prompts.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class TimeFormat
    {
        /// <summary>
        /// Format milliseconds as "mm:ss.fff", or "h:mm:ss.fff" past one hour.
        /// </summary>
        /// <param name="milliseconds">Offset in milliseconds.</param>
        /// <returns>Formatted offset.</returns>
        public static string Format(long milliseconds)
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            if (ts.TotalHours >= 1) return ((int)ts.TotalHours).ToString(CultureInfo.InvariantCulture) + ":" + ts.ToString("mm\\:ss\\.fff", CultureInfo.InvariantCulture);
            return ts.ToString("mm\\:ss\\.fff", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format milliseconds as a clock offset "hh:mm:ss", for example "01:32:10".
        /// </summary>
        /// <param name="milliseconds">Offset in milliseconds.</param>
        /// <returns>Formatted offset.</returns>
        public static string Clock(long milliseconds)
        {
            TimeSpan ts = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
            return ((int)ts.TotalHours).ToString("D2", CultureInfo.InvariantCulture) + ":" + ts.ToString("mm\\:ss", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Format a duration compactly: "45s", "2m00s", or "1h32m".
        /// </summary>
        /// <param name="milliseconds">Duration in milliseconds.</param>
        /// <returns>Formatted duration.</returns>
        public static string Span(long milliseconds)
        {
            long seconds = (long)Math.Round(Math.Max(0, milliseconds) / 1000.0);
            if (seconds < 60) return seconds.ToString(CultureInfo.InvariantCulture) + "s";
            if (seconds < 3600) return (seconds / 60).ToString(CultureInfo.InvariantCulture) + "m" + (seconds % 60).ToString("D2", CultureInfo.InvariantCulture) + "s";
            return (seconds / 3600).ToString(CultureInfo.InvariantCulture) + "h" + (seconds % 3600 / 60).ToString("D2", CultureInfo.InvariantCulture) + "m";
        }
    }
}
