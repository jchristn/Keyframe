namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Chooses frame timestamps inside a time range.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class FrameSampler
    {
        /// <summary>
        /// Evenly spaced timestamps at the centers of equal bins, which keeps samples away from scene transitions at the edges.
        /// </summary>
        /// <param name="startMs">Range start.</param>
        /// <param name="endMs">Range end.</param>
        /// <param name="count">Number of timestamps. Minimum 1.</param>
        /// <param name="maxTimestampMs">Largest timestamp allowed, typically the video duration minus one frame.</param>
        /// <returns>Distinct timestamps in ascending order.</returns>
        public static List<long> Evenly(long startMs, long endMs, int count, long maxTimestampMs)
        {
            List<long> result = new List<long>();
            if (endMs <= startMs || count < 1) return result;

            long length = endMs - startMs;
            for (int i = 0; i < count; i++)
            {
                long t = startMs + (long)Math.Round((i + 0.5) * length / count);
                t = Math.Clamp(t, 0L, Math.Max(0L, maxTimestampMs));
                if (result.Count == 0 || result[result.Count - 1] != t) result.Add(t);
            }

            return result;
        }
    }
}
