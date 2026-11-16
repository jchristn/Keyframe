namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Runs the summarize and extract functions against a subject. Shared by image, scene, and whole-video analysis.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class MediaFunctions
    {
        #region Private-Members

        private readonly ModelInvoker _Invoker;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="invoker">Model invoker.</param>
        /// <exception cref="ArgumentNullException">Thrown when invoker is null.</exception>
        public MediaFunctions(ModelInvoker invoker)
        {
            ArgumentNullException.ThrowIfNull(invoker);
            _Invoker = invoker;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Summarize a subject.
        /// </summary>
        /// <param name="client">Completion client.</param>
        /// <param name="model">Model override, or null.</param>
        /// <param name="subject">Subject.</param>
        /// <param name="spec">Summary options.</param>
        /// <param name="purpose">Call purpose for telemetry.</param>
        /// <param name="target">Call target for telemetry, or null.</param>
        /// <param name="context">Analysis context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task<SummaryOutcome> SummarizeAsync(
            IInferenceClient client,
            string? model,
            MediaSubject subject,
            SummarizeSpec spec,
            ModelCallPurposeEnum purpose,
            string? target,
            AnalysisContext context,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(context);

            InferenceRequest request = new InferenceRequest
            {
                Purpose = purpose,
                Target = target,
                SystemPrompt = PromptBuilder.SystemPrompt,
                UserPrompt = PromptBuilder.Summarize(subject, spec),
                Images = subject.Images,
                ResponseFormat = ResponseFormatEnum.Text,
                Model = model,
                MaxOutputTokens = Math.Min(8192, spec.MaxWords * 3 + 64)
            };

            InvocationResult result = await _Invoker.InvokeAsync(client, request, ValidateText, context, token).ConfigureAwait(false);
            if (!result.Success) return new SummaryOutcome { Success = false, Error = result.Error };
            return new SummaryOutcome { Success = true, Text = CleanSummary(result.Text) };
        }

        /// <summary>
        /// Extract fields from a subject.
        /// </summary>
        /// <param name="client">Completion client.</param>
        /// <param name="model">Model override, or null.</param>
        /// <param name="subject">Subject.</param>
        /// <param name="spec">Extract options.</param>
        /// <param name="purpose">Call purpose for telemetry.</param>
        /// <param name="target">Call target prefix for telemetry, or null.</param>
        /// <param name="context">Analysis context.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Outcome with one result per field.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a required argument is null.</exception>
        public async Task<ExtractionOutcome> ExtractAsync(
            IInferenceClient client,
            string? model,
            MediaSubject subject,
            ExtractSpec spec,
            ModelCallPurposeEnum purpose,
            string? target,
            AnalysisContext context,
            CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(client);
            ArgumentNullException.ThrowIfNull(subject);
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(context);

            ExtractionOutcome outcome = new ExtractionOutcome();
            if (spec.Fields.Count < 1) return outcome;

            if (spec.Mode == ExtractModeEnum.PerField)
            {
                List<Task<FieldResult>> tasks = spec.Fields
                    .Select(f => ExtractOneAsync(client, model, subject, f, purpose, target, outcome, context, token))
                    .ToList();
                FieldResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
                outcome.Fields.AddRange(results);
            }
            else
            {
                InferenceRequest request = new InferenceRequest
                {
                    Purpose = purpose,
                    Target = target,
                    SystemPrompt = PromptBuilder.SystemPrompt,
                    UserPrompt = PromptBuilder.ExtractCombined(subject, spec.Fields),
                    Images = subject.Images,
                    ResponseFormat = ResponseFormatEnum.JsonObject,
                    JsonSchema = FieldSchemaBuilder.Combined(spec.Fields),
                    Model = model
                };

                InvocationResult result = await _Invoker.InvokeAsync(client, request, ValidateJson, context, token).ConfigureAwait(false);
                if (!result.Success || !ModelOutputParser.TryParseObject(result.Text, out Dictionary<string, JsonElement> values, out string? _))
                {
                    string error = result.Error ?? "Model output could not be parsed.";
                    lock (outcome) { outcome.Errors.Add(error); }
                    outcome.Success = false;
                    foreach (FieldSpec field in spec.Fields) outcome.Fields.Add(FieldCoercer.Failed(field, error));
                    return outcome;
                }

                foreach (FieldSpec field in spec.Fields)
                {
                    bool present = values.TryGetValue(field.Key, out JsonElement value);
                    outcome.Fields.Add(FieldCoercer.Coerce(field, present, value));
                }
            }

            for (int i = 0; i < spec.Fields.Count; i++)
            {
                FieldSpec field = spec.Fields[i];
                FieldResult result = outcome.Fields[i];
                if (field.Required && !result.Valid)
                {
                    outcome.Success = false;
                    outcome.Errors.Add("Required field '" + field.Key + "' has no valid value: " + (result.Error ?? "unknown error") + ".");
                }
            }

            return outcome;
        }

        #endregion

        #region Private-Methods

        private async Task<FieldResult> ExtractOneAsync(
            IInferenceClient client,
            string? model,
            MediaSubject subject,
            FieldSpec field,
            ModelCallPurposeEnum purpose,
            string? target,
            ExtractionOutcome outcome,
            AnalysisContext context,
            CancellationToken token)
        {
            InferenceRequest request = new InferenceRequest
            {
                Purpose = purpose,
                Target = target == null ? field.Key : target + ":" + field.Key,
                SystemPrompt = PromptBuilder.SystemPrompt,
                UserPrompt = PromptBuilder.ExtractSingle(subject, field),
                Images = subject.Images,
                ResponseFormat = ResponseFormatEnum.JsonObject,
                JsonSchema = FieldSchemaBuilder.Single(field),
                Model = model
            };

            InvocationResult result = await _Invoker.InvokeAsync(client, request, ValidateJson, context, token).ConfigureAwait(false);
            if (!result.Success || !ModelOutputParser.TryParseObject(result.Text, out Dictionary<string, JsonElement> values, out string? _))
            {
                string error = result.Error ?? "Model output could not be parsed.";
                lock (outcome)
                {
                    outcome.Errors.Add("Field '" + field.Key + "': " + error);
                    outcome.Success = false;
                }

                return FieldCoercer.Failed(field, error);
            }

            bool present = values.TryGetValue("value", out JsonElement value);
            if (!present) present = values.TryGetValue(field.Key, out value);
            return FieldCoercer.Coerce(field, present, value);
        }

        private static string? ValidateText(string text)
        {
            return String.IsNullOrWhiteSpace(text) ? "The response was empty." : null;
        }

        private static string? ValidateJson(string text)
        {
            return ModelOutputParser.TryParseObject(text, out Dictionary<string, JsonElement> _, out string? error) ? null : error;
        }

        private static string CleanSummary(string? text)
        {
            if (String.IsNullOrWhiteSpace(text)) return string.Empty;
            string trimmed = text.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                int firstNewline = trimmed.IndexOf('\n');
                int lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewline > 0 && lastFence > firstNewline) trimmed = trimmed.Substring(firstNewline + 1, lastFence - firstNewline - 1).Trim();
            }

            return trimmed;
        }

        #endregion
    }
}
