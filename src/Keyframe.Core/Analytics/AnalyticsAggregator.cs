namespace Keyframe.Core.Analytics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Models;
    using Keyframe.Core.Responses;

    /// <summary>
    /// In-memory analytics math: distributions with percentiles, stat blocks, gap-filled buckets, and breakdowns by
    /// model, stage, and purpose. Providers load the filtered rows and hand them here, so every database gives the
    /// same numbers.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class AnalyticsAggregator
    {
        #region Public-Members

        /// <summary>
        /// Maximum buckets in one summary.
        /// </summary>
        public static int MaxBuckets { get; } = 10000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Choose a bucket width for a range: minute up to 2 hours, hour up to 7 days, otherwise day.
        /// </summary>
        /// <param name="range">Range length.</param>
        /// <returns>Bucket width.</returns>
        public static AnalyticsBucketEnum ChooseBucket(TimeSpan range)
        {
            if (range <= TimeSpan.FromHours(2)) return AnalyticsBucketEnum.Minute;
            if (range <= TimeSpan.FromDays(7)) return AnalyticsBucketEnum.Hour;
            return AnalyticsBucketEnum.Day;
        }

        /// <summary>
        /// Width of a bucket.
        /// </summary>
        /// <param name="bucket">Bucket.</param>
        /// <returns>Width.</returns>
        public static TimeSpan Width(AnalyticsBucketEnum bucket)
        {
            switch (bucket)
            {
                case AnalyticsBucketEnum.Minute:
                    return TimeSpan.FromMinutes(1);
                case AnalyticsBucketEnum.Day:
                    return TimeSpan.FromDays(1);
                default:
                    return TimeSpan.FromHours(1);
            }
        }

        /// <summary>
        /// Align a UTC time down to a bucket boundary.
        /// </summary>
        /// <param name="value">Time.</param>
        /// <param name="bucket">Bucket.</param>
        /// <returns>Aligned UTC time.</returns>
        public static DateTime Align(DateTime value, AnalyticsBucketEnum bucket)
        {
            DateTime utc = value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
            long width = Width(bucket).Ticks;
            return new DateTime(utc.Ticks - (utc.Ticks % width), DateTimeKind.Utc);
        }

        /// <summary>
        /// Compute a distribution.
        /// </summary>
        /// <param name="values">Samples.</param>
        /// <returns>Distribution, or null when there are no samples.</returns>
        public static Distribution? Distribute(IEnumerable<double> values)
        {
            if (values == null) return null;
            List<double> sorted = values.Where(v => !Double.IsNaN(v) && !Double.IsInfinity(v)).OrderBy(v => v).ToList();
            if (sorted.Count < 1) return null;

            double sum = sorted.Sum();
            double avg = sum / sorted.Count;
            double variance = sorted.Sum(v => (v - avg) * (v - avg)) / sorted.Count;
            return new Distribution
            {
                Count = sorted.Count,
                Min = Round(sorted[0]),
                Max = Round(sorted[sorted.Count - 1]),
                Avg = Round(avg),
                P50 = Round(Percentile(sorted, 50)),
                P90 = Round(Percentile(sorted, 90)),
                P95 = Round(Percentile(sorted, 95)),
                P99 = Round(Percentile(sorted, 99)),
                StdDev = Round(Math.Sqrt(variance)),
                Sum = Round(sum)
            };
        }

        /// <summary>
        /// Percentile of sorted samples using linear interpolation between closest ranks (the method spreadsheets
        /// call PERCENTILE.INC).
        /// </summary>
        /// <param name="sorted">Samples in ascending order.</param>
        /// <param name="percentile">Percentile, 0 to 100.</param>
        /// <returns>Value, or 0 when there are no samples.</returns>
        /// <exception cref="ArgumentNullException">Thrown when sorted is null.</exception>
        public static double Percentile(IReadOnlyList<double> sorted, double percentile)
        {
            ArgumentNullException.ThrowIfNull(sorted);
            if (sorted.Count < 1) return 0;
            if (sorted.Count == 1) return sorted[0];
            double rank = Math.Clamp(percentile, 0, 100) / 100.0 * (sorted.Count - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            if (lower == upper) return sorted[lower];
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
        }

        /// <summary>
        /// Compute a stat block over analyses and the model call attempts that belong to them.
        /// </summary>
        /// <param name="analyses">Analyses.</param>
        /// <param name="calls">Model call attempts.</param>
        /// <returns>Stat block.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static StatBlock Block(IReadOnlyCollection<AnalysisRecord> analyses, IReadOnlyCollection<ModelCallRecord> calls)
        {
            ArgumentNullException.ThrowIfNull(analyses);
            ArgumentNullException.ThrowIfNull(calls);

            StatBlock block = new StatBlock
            {
                Count = analyses.Count,
                Succeeded = analyses.LongCount(a => a.Success),
                Failed = analyses.LongCount(a => !a.Success),
                ModelCalls = analyses.Sum(a => (long)a.ModelCalls),
                FailedModelCalls = analyses.Sum(a => (long)a.FailedModelCalls),
                Retries = analyses.Sum(a => (long)a.Retries),
                RepairAttempts = analyses.Sum(a => (long)a.RepairAttempts),
                PromptTokens = analyses.Sum(a => a.PromptTokens),
                CompletionTokens = analyses.Sum(a => a.CompletionTokens),
                TotalTokens = analyses.Sum(a => a.TotalTokens),
                CachedTokens = analyses.Sum(a => a.CachedTokens),
                ReasoningTokens = analyses.Sum(a => a.ReasoningTokens),
                InputImages = analyses.Sum(a => (long)a.InputImages),
                InputImageBytes = analyses.Sum(a => a.InputImageBytes),
                InputAudioBytes = analyses.Sum(a => a.InputAudioBytes),
                Scenes = analyses.Sum(a => (long)a.Scenes),
                TranscriptCharacters = analyses.Sum(a => a.TranscriptCharacters)
            };

            block.SuccessRate = block.Count > 0 ? Math.Round(block.Succeeded / (double)block.Count, 4) : 0;
            block.AnalysisMs = Distribute(analyses.Select(a => a.AnalysisMs));
            block.QueueWaitMs = Distribute(analyses.Select(a => a.QueueWaitMs));
            block.MediaDurationMs = Distribute(analyses.Where(a => a.MediaDurationMs.HasValue).Select(a => (double)a.MediaDurationMs!.Value));
            block.RealtimeFactor = Distribute(analyses.Where(a => a.RealtimeFactor.HasValue).Select(a => a.RealtimeFactor!.Value));
            block.MediaBytes = Distribute(analyses.Select(a => (double)a.MediaBytes));
            block.TokensPerAnalysis = Distribute(analyses.Select(a => (double)a.TotalTokens));
            block.ModelCallMs = Distribute(calls.Select(c => c.DurationMs));
            block.TimeToFirstTokenMs = Distribute(calls.Where(c => c.TimeToFirstTokenMs.HasValue).Select(c => c.TimeToFirstTokenMs!.Value));
            block.GenerationMs = Distribute(calls.Where(c => c.GenerationMs.HasValue).Select(c => c.GenerationMs!.Value));
            block.TokensPerSecond = Distribute(calls.Where(c => c.TokensPerSecond.HasValue).Select(c => c.TokensPerSecond!.Value));
            return block;
        }

        /// <summary>
        /// Build a full summary. Analyses outside [from, to) are ignored; calls whose analysis is not in the set are ignored.
        /// </summary>
        /// <param name="fromUtc">Inclusive start; aligned down to the bucket width.</param>
        /// <param name="toUtc">Exclusive end.</param>
        /// <param name="bucket">Bucket width.</param>
        /// <param name="analyses">Analyses already matching the filter.</param>
        /// <param name="calls">Model call attempts of those analyses, already matching any endpoint or model filter.</param>
        /// <returns>Summary with every bucket present. MediaKind, Endpoint, and Model are left for the caller to set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when analyses or calls is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the range needs more than <see cref="MaxBuckets"/> buckets.</exception>
        public static AnalyticsSummary Summarize(DateTime fromUtc, DateTime toUtc, AnalyticsBucketEnum bucket, IReadOnlyCollection<AnalysisRecord> analyses, IReadOnlyCollection<ModelCallRecord> calls)
        {
            ArgumentNullException.ThrowIfNull(analyses);
            ArgumentNullException.ThrowIfNull(calls);

            TimeSpan width = Width(bucket);
            DateTime from = Align(fromUtc, bucket);
            DateTime to = toUtc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(toUtc, DateTimeKind.Utc) : toUtc.ToUniversalTime();
            if (to <= from) to = from.Add(width);

            long count = (long)Math.Ceiling((to - from).Ticks / (double)width.Ticks);
            if (count > MaxBuckets)
                throw new ArgumentOutOfRangeException(nameof(bucket), "The range needs " + count + " " + bucket.ToString().ToLowerInvariant() + " buckets, over the limit of " + MaxBuckets + "; choose a wider bucket or a shorter range.");

            List<AnalysisRecord> inRange = analyses.Where(a => a.CompletedUtc >= from && a.CompletedUtc < to).ToList();
            HashSet<string> ids = new HashSet<string>(inRange.Select(a => a.Id), StringComparer.Ordinal);
            List<ModelCallRecord> relevant = calls.Where(c => ids.Contains(c.AnalysisId)).ToList();
            Dictionary<string, List<ModelCallRecord>> callsByAnalysis = relevant.GroupBy(c => c.AnalysisId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            AnalyticsSummary summary = new AnalyticsSummary
            {
                FromUtc = from,
                ToUtc = to,
                Bucket = bucket.ToString().ToLowerInvariant(),
                Totals = Block(inRange, relevant)
            };

            foreach (MediaKindEnum kind in new[] { MediaKindEnum.Image, MediaKindEnum.Video, MediaKindEnum.Audio })
            {
                List<AnalysisRecord> subset = inRange.Where(a => a.MediaKind == kind).ToList();
                summary.ByMediaKind[kind.ToString()] = Block(subset, CallsFor(subset, callsByAnalysis));
            }

            List<AnalysisRecord>[] buckets = new List<AnalysisRecord>[count];
            for (int i = 0; i < count; i++) buckets[i] = new List<AnalysisRecord>();
            foreach (AnalysisRecord analysis in inRange)
            {
                long index = (analysis.CompletedUtc - from).Ticks / width.Ticks;
                if (index >= 0 && index < count) buckets[index].Add(analysis);
            }

            for (int i = 0; i < count; i++)
            {
                summary.Buckets.Add(new AnalyticsBucket
                {
                    StartUtc = from.Add(TimeSpan.FromTicks(width.Ticks * i)),
                    Stats = Block(buckets[i], CallsFor(buckets[i], callsByAnalysis))
                });
            }

            summary.ByModel = relevant
                .GroupBy(c => c.Endpoint + "\u0001" + c.Provider + "\u0001" + (c.Model ?? String.Empty), StringComparer.Ordinal)
                .Select(g => ModelBlock(g.ToList()))
                .OrderByDescending(m => m.Calls)
                .ThenBy(m => m.Endpoint, StringComparer.Ordinal)
                .ThenBy(m => m.Model ?? String.Empty, StringComparer.Ordinal)
                .ToList();

            summary.ByStage = inRange
                .SelectMany(a => a.Stages)
                .GroupBy(s => s.Name, StringComparer.Ordinal)
                .Select(g => new StageStats { Stage = g.Key, DurationMs = Distribute(g.Select(s => s.DurationMs)) })
                .OrderBy(s => StageOrder(s.Stage))
                .ThenBy(s => s.Stage, StringComparer.Ordinal)
                .ToList();

            summary.ByPurpose = relevant
                .GroupBy(c => c.Purpose)
                .OrderBy(g => (int)g.Key)
                .Select(g => new PurposeStats
                {
                    Purpose = g.Key,
                    Calls = g.LongCount(),
                    Failed = g.LongCount(c => !c.Success),
                    DurationMs = Distribute(g.Select(c => c.DurationMs)),
                    TimeToFirstTokenMs = Distribute(g.Where(c => c.TimeToFirstTokenMs.HasValue).Select(c => c.TimeToFirstTokenMs!.Value))
                })
                .ToList();

            return summary;
        }

        #endregion

        #region Private-Methods

        private static List<ModelCallRecord> CallsFor(List<AnalysisRecord> analyses, Dictionary<string, List<ModelCallRecord>> byAnalysis)
        {
            List<ModelCallRecord> list = new List<ModelCallRecord>();
            foreach (AnalysisRecord analysis in analyses)
            {
                if (byAnalysis.TryGetValue(analysis.Id, out List<ModelCallRecord>? calls)) list.AddRange(calls);
            }

            return list;
        }

        private static ModelStats ModelBlock(List<ModelCallRecord> calls)
        {
            ModelCallRecord first = calls[0];
            return new ModelStats
            {
                Endpoint = first.Endpoint,
                Provider = first.Provider,
                Model = first.Model,
                Calls = calls.Count,
                Failed = calls.LongCount(c => !c.Success),
                DurationMs = Distribute(calls.Select(c => c.DurationMs)),
                TimeToFirstTokenMs = Distribute(calls.Where(c => c.TimeToFirstTokenMs.HasValue).Select(c => c.TimeToFirstTokenMs!.Value)),
                GenerationMs = Distribute(calls.Where(c => c.GenerationMs.HasValue).Select(c => c.GenerationMs!.Value)),
                TokensPerSecond = Distribute(calls.Where(c => c.TokensPerSecond.HasValue).Select(c => c.TokensPerSecond!.Value)),
                PromptTokens = calls.Sum(c => c.PromptTokens ?? 0),
                CompletionTokens = calls.Sum(c => c.CompletionTokens ?? 0)
            };
        }

        private static int StageOrder(string name)
        {
            AnalysisStageEnum stage;
            if (Enum.TryParse<AnalysisStageEnum>(name, false, out stage)) return (int)stage;
            return Int32.MaxValue;
        }

        private static double Round(double value)
        {
            return Math.Round(value, 3);
        }

        #endregion
    }
}
