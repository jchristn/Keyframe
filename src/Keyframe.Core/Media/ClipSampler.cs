namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Chooses audio excerpt windows: the audio counterpart of <see cref="FrameSampler"/>.
    /// </summary>
    public static class ClipSampler
    {
        /// <summary>
        /// Evenly spaced windows of equal length covering a recording. The first window starts at 0 and the last ends at
        /// the end of the recording; the windows between are spaced evenly. When the windows together would cover the
        /// whole recording, one window of the whole recording is returned.
        /// </summary>
        /// <param name="durationMs">Recording length in milliseconds. Minimum 1.</param>
        /// <param name="count">Number of windows. Minimum 1.</param>
        /// <param name="clipMs">Length of each window in milliseconds. Minimum 1.</param>
        /// <returns>Windows in order, never overlapping.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when durationMs, count, or clipMs is below 1.</exception>
        public static List<ClipWindow> Evenly(long durationMs, int count, long clipMs)
        {
            if (durationMs < 1) throw new ArgumentOutOfRangeException(nameof(durationMs), "Duration must be at least 1 ms.");
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count), "Count must be at least 1.");
            if (clipMs < 1) throw new ArgumentOutOfRangeException(nameof(clipMs), "Clip length must be at least 1 ms.");

            List<ClipWindow> windows = new List<ClipWindow>();
            if ((long)count * clipMs >= durationMs)
            {
                windows.Add(new ClipWindow(0, durationMs));
                return windows;
            }

            if (count == 1)
            {
                long start = (durationMs - clipMs) / 2;
                windows.Add(new ClipWindow(start, start + clipMs));
                return windows;
            }

            long span = durationMs - clipMs;
            for (int i = 0; i < count; i++)
            {
                long start = span * i / (count - 1);
                windows.Add(new ClipWindow(start, start + clipMs));
            }

            return windows;
        }

        /// <summary>
        /// A window of at most maxMs centered on a span; the span itself when it already fits.
        /// </summary>
        /// <param name="startMs">Span start in milliseconds.</param>
        /// <param name="endMs">Span end in milliseconds.</param>
        /// <param name="maxMs">Longest window in milliseconds. Minimum 1.</param>
        /// <returns>The window.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when maxMs is below 1.</exception>
        public static ClipWindow Centered(long startMs, long endMs, long maxMs)
        {
            if (maxMs < 1) throw new ArgumentOutOfRangeException(nameof(maxMs), "Maximum length must be at least 1 ms.");
            long length = Math.Max(0, endMs - startMs);
            if (length <= maxMs) return new ClipWindow(startMs, endMs);
            long start = startMs + (length - maxMs) / 2;
            return new ClipWindow(start, start + maxMs);
        }
    }
}
