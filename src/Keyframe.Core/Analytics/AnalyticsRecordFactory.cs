namespace Keyframe.Core.Analytics
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Helpers;
    using Keyframe.Core.Models;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Turns a finished analysis into the rows stored for analytics: one <see cref="AnalysisRecord"/> and one
    /// <see cref="ModelCallRecord"/> per model call attempt.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class AnalyticsRecordFactory
    {
        #region Public-Members

        /// <summary>
        /// Maximum characters kept in an error column.
        /// </summary>
        public static int MaxErrorLength { get; } = 2000;

        #endregion

        #region Public-Methods

        /// <summary>
        /// Build the analysis record, with <see cref="AnalysisRecord.Calls"/> filled.
        /// </summary>
        /// <param name="completion">Completion.</param>
        /// <returns>Record.</returns>
        /// <exception cref="ArgumentNullException">Thrown when completion is null.</exception>
        public static AnalysisRecord Build(AnalysisCompletion completion)
        {
            ArgumentNullException.ThrowIfNull(completion);
            AnalysisResponse response = completion.Response;
            AnalysisTelemetry telemetry = response.Telemetry;
            TokenTotals totals = telemetry.Totals;

            AnalysisRecord record = new AnalysisRecord
            {
                Id = response.Id,
                RequestId = response.RequestId ?? completion.Origin?.RequestId,
                JobId = completion.Origin?.JobId,
                MediaKind = completion.MediaKind,
                PrincipalName = completion.Origin?.PrincipalName,
                Success = response.Success && completion.Exception == null && !completion.Canceled,
                StartedUtc = telemetry.StartUtc,
                CompletedUtc = telemetry.EndUtc ?? DateTime.UtcNow,
                AnalysisMs = telemetry.TotalMs,
                QueueWaitMs = telemetry.QueueWaitMs,
                MediaDurationMs = completion.MediaDurationMs,
                MediaBytes = completion.MediaBytes,
                VisionEndpoint = completion.VisionEndpoint,
                VisionModel = completion.VisionModel,
                TextEndpoint = completion.TextEndpoint,
                TextModel = completion.TextModel,
                TranscriptionEndpoint = completion.TranscriptionEndpoint,
                TranscriptionModel = completion.TranscriptionModel,
                ModelCalls = totals.ModelCalls,
                FailedModelCalls = totals.FailedModelCalls,
                PromptTokens = totals.PromptTokens,
                CompletionTokens = totals.CompletionTokens,
                TotalTokens = totals.TotalTokens,
                CachedTokens = totals.CachedTokens,
                ReasoningTokens = totals.ReasoningTokens,
                InputImages = totals.InputImages,
                InputImageBytes = totals.InputImageBytes,
                InputAudioBytes = telemetry.ModelCalls.Sum(c => c.InputAudioBytes),
                Scenes = completion.Scenes,
                TranscriptCharacters = completion.TranscriptCharacters,
                Stages = telemetry.Stages.ToList(),
                Calls = new List<ModelCallRecord>()
            };

            if (completion.MediaDurationMs.HasValue && completion.MediaDurationMs.Value > 0 && completion.MediaKind != MediaKindEnum.Image)
                record.RealtimeFactor = Math.Round(telemetry.TotalMs / completion.MediaDurationMs.Value, 4);

            List<FieldResult> fields = response.Fields.ToList();
            if (response is VideoAnalysisResponse video) fields.AddRange(video.Scenes.SelectMany(s => s.Fields));
            record.FieldCount = fields.Count;
            record.InvalidFields = fields.Count(f => !f.Valid);

            foreach (ModelCallTelemetry call in telemetry.ModelCalls)
            {
                record.RepairAttempts += call.RepairAttempts;
                record.Retries += Math.Max(0, call.Attempts - 1 - call.RepairAttempts);
                record.Calls.AddRange(ToRecords(record.Id, call));
            }

            record.Error = Truncate(ErrorText(completion));
            return record;
        }

        #endregion

        #region Private-Methods

        private static List<ModelCallRecord> ToRecords(string analysisId, ModelCallTelemetry call)
        {
            List<ModelCallRecord> records = new List<ModelCallRecord>();
            List<ModelCallAttempt> attempts = call.AttemptDetails;
            if (attempts.Count < 1)
            {
                attempts = new List<ModelCallAttempt>
                {
                    new ModelCallAttempt
                    {
                        Attempt = 1,
                        StartUtc = call.StartUtc,
                        Success = call.Success,
                        Error = call.Error,
                        StatusCode = call.StatusCode,
                        DurationMs = call.DurationMs,
                        QueueMs = call.QueueMs,
                        TimeToFirstTokenMs = call.TimeToFirstTokenMs,
                        GenerationMs = call.GenerationMs,
                        TokensPerSecond = call.TokensPerSecond,
                        PromptTokens = call.PromptTokens,
                        CompletionTokens = call.CompletionTokens,
                        TotalTokens = call.TotalTokens,
                        CachedTokens = call.CachedTokens,
                        ReasoningTokens = call.ReasoningTokens,
                        FinishReason = call.FinishReason
                    }
                };
            }

            foreach (ModelCallAttempt attempt in attempts)
            {
                records.Add(new ModelCallRecord
                {
                    Id = IdGenerator.GenerateModelCallId(),
                    AnalysisId = analysisId,
                    Purpose = call.Purpose,
                    Target = call.Target,
                    Endpoint = call.Endpoint,
                    Provider = call.Provider,
                    Model = call.Model,
                    Attempt = attempt.Attempt,
                    StartUtc = attempt.StartUtc,
                    Success = attempt.Success,
                    Error = Truncate(attempt.Error),
                    StatusCode = attempt.StatusCode,
                    DurationMs = attempt.DurationMs,
                    QueueMs = attempt.QueueMs,
                    TimeToFirstTokenMs = attempt.TimeToFirstTokenMs,
                    GenerationMs = attempt.GenerationMs,
                    TokensPerSecond = attempt.TokensPerSecond,
                    PromptTokens = attempt.PromptTokens,
                    CompletionTokens = attempt.CompletionTokens,
                    TotalTokens = attempt.TotalTokens ?? Sum(attempt.PromptTokens, attempt.CompletionTokens),
                    CachedTokens = attempt.CachedTokens,
                    ReasoningTokens = attempt.ReasoningTokens,
                    InputImages = call.InputImages,
                    InputImageBytes = call.InputImageBytes,
                    InputAudioBytes = call.InputAudioBytes,
                    FinishReason = attempt.FinishReason
                });
            }

            return records;
        }

        private static string? ErrorText(AnalysisCompletion completion)
        {
            if (completion.Canceled) return "Canceled.";
            if (completion.Exception != null) return completion.Exception;
            if (!completion.Response.Success && completion.Response.Errors.Count > 0) return String.Join(" ", completion.Response.Errors);
            if (!completion.Response.Success) return "The analysis did not succeed.";
            return null;
        }

        private static string? Truncate(string? value)
        {
            if (value == null) return null;
            return value.Length <= MaxErrorLength ? value : value.Substring(0, MaxErrorLength);
        }

        private static long? Sum(long? a, long? b)
        {
            if (!a.HasValue && !b.HasValue) return null;
            return (a ?? 0) + (b ?? 0);
        }

        #endregion
    }
}
