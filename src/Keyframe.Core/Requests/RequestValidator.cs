namespace Keyframe.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Validates analysis requests. Returns every problem found rather than stopping at the first.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class RequestValidator
    {
        #region Public-Members

        /// <summary>
        /// Maximum total context length in characters, across all items.
        /// </summary>
        public static int MaxContextLength { get; } = 20000;

        /// <summary>
        /// Maximum context items per request.
        /// </summary>
        public static int MaxContextItems { get; } = 50;

        /// <summary>
        /// Maximum prompt and instruction length in characters.
        /// </summary>
        public static int MaxPromptLength { get; } = 4000;

        /// <summary>
        /// Maximum fields per extract specification.
        /// </summary>
        public static int MaxFields { get; } = 100;

        /// <summary>
        /// Maximum options per Enum field.
        /// </summary>
        public static int MaxOptions { get; } = 100;

        #endregion

        #region Private-Members

        private static readonly Regex _KeyPattern = new Regex("^[A-Za-z0-9_.\\-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Validate an image analysis request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Validation errors. Empty when the request is valid.</returns>
        public static List<string> Validate(ImageAnalysisRequest? request)
        {
            List<string> errors = new List<string>();
            if (request == null)
            {
                errors.Add("Request body is required.");
                return errors;
            }

            ValidateSource(request.Image, "Image", errors);
            ValidateContext(request.Context, errors);
            ValidateModel(request.Model, "Model", errors);

            if (!request.Functions.Summarize.Enable && !request.Functions.Extract.Enable)
                errors.Add("Enable at least one function (Functions.Summarize or Functions.Extract).");

            ValidateFunctions(request.Functions.Summarize, request.Functions.Extract, "Functions", errors);
            return errors;
        }

        /// <summary>
        /// Validate a video analysis request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Validation errors. Empty when the request is valid.</returns>
        public static List<string> Validate(VideoAnalysisRequest? request)
        {
            List<string> errors = new List<string>();
            if (request == null)
            {
                errors.Add("Request body is required.");
                return errors;
            }

            ValidateSource(request.Video, "Video", errors);
            ValidateContext(request.Context, errors);
            ValidateModel(request.Model, "Model", errors);
            ValidateModel(request.TextModel, "TextModel", errors);

            VideoFunctionsSpec functions = request.Functions;
            bool sceneWork = functions.Scenes.Enable;
            if (!functions.Summarize.Enable && !functions.Extract.Enable && !sceneWork && !functions.Transcribe.Enable)
                errors.Add("Enable at least one function (Summarize, Extract, Scenes, or Transcribe).");

            ValidateFunctions(functions.Summarize, functions.Extract, "Functions", errors);

            if (functions.Scenes.Enable)
            {
                if (functions.Scenes.Detection.MinSceneMs >= functions.Scenes.Detection.MaxSceneMs)
                    errors.Add("Functions.Scenes.Detection.MinSceneMs must be less than MaxSceneMs.");

                ValidateFunctions(functions.Scenes.Summarize, functions.Scenes.Extract, "Functions.Scenes", errors);
            }

            if (functions.Transcribe.Language != null && !Regex.IsMatch(functions.Transcribe.Language, "^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$"))
                errors.Add("Functions.Transcribe.Language must be an ISO-639 code such as 'en' or 'pt-BR'.");

            if (functions.Transcribe.Prompt != null && functions.Transcribe.Prompt.Length > MaxPromptLength)
                errors.Add("Functions.Transcribe.Prompt exceeds " + MaxPromptLength + " characters.");

            return errors;
        }

        /// <summary>
        /// Validate a probe request.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <returns>Validation errors. Empty when the request is valid.</returns>
        public static List<string> Validate(ProbeRequest? request)
        {
            List<string> errors = new List<string>();
            if (request == null)
            {
                errors.Add("Request body is required.");
                return errors;
            }

            ValidateSource(request.Media, "Media", errors);
            return errors;
        }

        /// <summary>
        /// Whether a key is syntactically valid.
        /// </summary>
        /// <param name="key">Key.</param>
        /// <returns>True when valid.</returns>
        public static bool IsValidKey(string? key)
        {
            return !String.IsNullOrEmpty(key) && _KeyPattern.IsMatch(key);
        }

        #endregion

        #region Private-Methods

        private static void ValidateSource(MediaSource? source, string name, List<string> errors)
        {
            if (source == null)
            {
                errors.Add(name + " is required.");
                return;
            }

            bool hasUrl = !String.IsNullOrWhiteSpace(source.Url);
            bool hasData = !String.IsNullOrWhiteSpace(source.Data);

            if (hasUrl && hasData) errors.Add(name + " must specify Url or Data, not both.");
            else if (!hasUrl && !hasData) errors.Add(name + " must specify Url or Data.");

            if (hasUrl)
            {
                if (!Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    errors.Add(name + ".Url must be an absolute http or https URL.");
                }
            }

            if (source.MediaType != null && (source.MediaType.Length > 255 || !source.MediaType.Contains('/')))
                errors.Add(name + ".MediaType must be a MIME type such as 'image/jpeg'.");
        }

        private static void ValidateContext(List<ContextItem> context, List<string> errors)
        {
            if (context.Count > MaxContextItems)
                errors.Add("Context may contain at most " + MaxContextItems + " items.");

            long total = 0;
            for (int i = 0; i < context.Count; i++)
            {
                ContextItem item = context[i];
                if (item == null || String.IsNullOrWhiteSpace(item.Text))
                {
                    errors.Add("Context[" + i + "].Text is required.");
                    continue;
                }

                if (item.Label != null && item.Label.Length > 128)
                    errors.Add("Context[" + i + "].Label exceeds 128 characters.");
                total += item.Text.Length + (item.Label?.Length ?? 0);
            }

            if (total > MaxContextLength)
                errors.Add("Context exceeds " + MaxContextLength + " characters in total.");
        }

        private static void ValidateModel(ModelSelection? model, string name, List<string> errors)
        {
            if (model == null) return;
            if (model.Endpoint != null && (model.Endpoint.Length < 1 || model.Endpoint.Length > 64))
                errors.Add(name + ".Endpoint must be 1 to 64 characters.");
            if (model.Name != null && (model.Name.Length < 1 || model.Name.Length > 256))
                errors.Add(name + ".Name must be 1 to 256 characters.");
        }

        private static void ValidateFunctions(SummarizeSpec summarize, ExtractSpec extract, string prefix, List<string> errors)
        {
            if (summarize.Enable)
            {
                if (!IsValidKey(summarize.Key))
                    errors.Add(prefix + ".Summarize.Key must be 1 to 64 characters of letters, digits, underscore, hyphen, or period.");
                if (summarize.Instructions != null && summarize.Instructions.Length > MaxPromptLength)
                    errors.Add(prefix + ".Summarize.Instructions exceeds " + MaxPromptLength + " characters.");
            }

            if (!extract.Enable) return;

            if (extract.Fields.Count < 1)
            {
                errors.Add(prefix + ".Extract.Fields must contain at least one field when Extract is enabled.");
                return;
            }

            if (extract.Fields.Count > MaxFields)
                errors.Add(prefix + ".Extract.Fields may contain at most " + MaxFields + " fields.");

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < extract.Fields.Count; i++)
            {
                FieldSpec field = extract.Fields[i];
                string label = prefix + ".Extract.Fields[" + i + "]";

                if (field == null)
                {
                    errors.Add(label + " is null.");
                    continue;
                }

                if (!IsValidKey(field.Key))
                {
                    errors.Add(label + ".Key must be 1 to 64 characters of letters, digits, underscore, hyphen, or period.");
                }
                else
                {
                    if (!seen.Add(field.Key)) errors.Add(label + ".Key '" + field.Key + "' is duplicated.");
                    if (summarize.Enable && String.Equals(field.Key, summarize.Key, StringComparison.OrdinalIgnoreCase))
                        errors.Add(label + ".Key '" + field.Key + "' collides with the summary key.");
                }

                if (String.IsNullOrWhiteSpace(field.Prompt)) errors.Add(label + ".Prompt is required.");
                else if (field.Prompt.Length > MaxPromptLength) errors.Add(label + ".Prompt exceeds " + MaxPromptLength + " characters.");

                if (field.Type == FieldTypeEnum.Enum)
                {
                    if (field.Options == null || field.Options.Count < 1)
                        errors.Add(label + ".Options is required for Enum fields.");
                    else if (field.Options.Count > MaxOptions)
                        errors.Add(label + ".Options may contain at most " + MaxOptions + " values.");
                    else if (field.Options.Any(o => String.IsNullOrWhiteSpace(o)))
                        errors.Add(label + ".Options may not contain empty values.");
                }

                if (field.Minimum.HasValue && field.Maximum.HasValue && field.Minimum.Value > field.Maximum.Value)
                    errors.Add(label + ".Minimum must not exceed Maximum.");
                if (field.MaxLength.HasValue && field.MaxLength.Value < 1)
                    errors.Add(label + ".MaxLength must be at least 1.");
                if (field.MaxItems.HasValue && field.MaxItems.Value < 1)
                    errors.Add(label + ".MaxItems must be at least 1.");
            }
        }

        #endregion
    }
}
