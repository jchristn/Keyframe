namespace Keyframe.Core.Media
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// Turns cut timestamps into contiguous segments covering a recording. Shared by scene detection (video) and section
    /// detection (audio) so both follow the same merge and split rules.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class BoundaryBuilder
    {
        #region Public-Methods

        /// <summary>
        /// Build segments: drop out-of-range and near-duplicate cuts, merge segments shorter than the minimum, split
        /// segments longer than the maximum, then merge the shortest adjacent pairs until the count is within the limit.
        /// The count limit takes precedence over the maximum length.
        /// </summary>
        /// <param name="cutsMs">Cut timestamps in milliseconds.</param>
        /// <param name="durationMs">Recording length in milliseconds.</param>
        /// <param name="minMs">Shortest segment in milliseconds; shorter ones merge into a neighbor. 0 disables.</param>
        /// <param name="maxMs">Longest segment in milliseconds; longer ones split evenly. 0 disables.</param>
        /// <param name="maxCount">Most segments. Minimum 1.</param>
        /// <param name="minCutSpacingMs">Cuts closer than this to each other or to either end are dropped.</param>
        /// <returns>Segments in order, covering 0 to durationMs. Empty when durationMs is below 1.</returns>
        /// <exception cref="ArgumentNullException">Thrown when cutsMs is null.</exception>
        public static List<SceneBoundary> Build(List<long> cutsMs, long durationMs, long minMs, long maxMs, int maxCount, long minCutSpacingMs)
        {
            ArgumentNullException.ThrowIfNull(cutsMs);
            if (durationMs < 1) return new List<SceneBoundary>();

            List<long> cuts = new List<long>();
            foreach (long c in cutsMs.Where(c => c > 0 && c < durationMs).OrderBy(c => c))
            {
                if (cuts.Count > 0 && c - cuts[cuts.Count - 1] < minCutSpacingMs) continue;
                if (c < minCutSpacingMs || durationMs - c < minCutSpacingMs) continue;
                cuts.Add(c);
            }

            List<SceneBoundary> segments = new List<SceneBoundary>();
            long start = 0;
            foreach (long c in cuts)
            {
                segments.Add(new SceneBoundary { StartMs = start, EndMs = c });
                start = c;
            }

            segments.Add(new SceneBoundary { StartMs = start, EndMs = durationMs });

            MergeShort(segments, minMs);
            segments = SplitLong(segments, maxMs);
            MergeToCount(segments, maxCount);
            return segments;
        }

        #endregion

        #region Private-Methods

        private static void MergeShort(List<SceneBoundary> segments, long minMs)
        {
            if (minMs < 1) return;
            bool merged = true;
            while (merged && segments.Count > 1)
            {
                merged = false;
                for (int i = 0; i < segments.Count; i++)
                {
                    if (segments[i].DurationMs >= minMs) continue;

                    int neighbor;
                    if (i == 0) neighbor = 1;
                    else if (i == segments.Count - 1) neighbor = i - 1;
                    else neighbor = segments[i - 1].DurationMs <= segments[i + 1].DurationMs ? i - 1 : i + 1;

                    MergePair(segments, Math.Min(i, neighbor));
                    merged = true;
                    break;
                }
            }
        }

        private static List<SceneBoundary> SplitLong(List<SceneBoundary> segments, long maxMs)
        {
            if (maxMs < 1) return segments;
            List<SceneBoundary> result = new List<SceneBoundary>();
            foreach (SceneBoundary segment in segments)
            {
                if (segment.DurationMs <= maxMs)
                {
                    result.Add(segment);
                    continue;
                }

                int parts = (int)Math.Ceiling(segment.DurationMs / (double)maxMs);
                long size = segment.DurationMs / parts;
                long s = segment.StartMs;
                for (int p = 0; p < parts; p++)
                {
                    long e = p == parts - 1 ? segment.EndMs : s + size;
                    result.Add(new SceneBoundary { StartMs = s, EndMs = e });
                    s = e;
                }
            }

            return result;
        }

        private static void MergeToCount(List<SceneBoundary> segments, int maxCount)
        {
            while (segments.Count > Math.Max(1, maxCount))
            {
                int best = 0;
                long bestDuration = Int64.MaxValue;
                for (int i = 0; i < segments.Count - 1; i++)
                {
                    long combined = segments[i + 1].EndMs - segments[i].StartMs;
                    if (combined < bestDuration)
                    {
                        bestDuration = combined;
                        best = i;
                    }
                }

                MergePair(segments, best);
            }
        }

        private static void MergePair(List<SceneBoundary> segments, int index)
        {
            segments[index] = new SceneBoundary { StartMs = segments[index].StartMs, EndMs = segments[index + 1].EndMs };
            segments.RemoveAt(index + 1);
        }

        #endregion
    }
}
