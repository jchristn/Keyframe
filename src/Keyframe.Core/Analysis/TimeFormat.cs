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
    }
}
