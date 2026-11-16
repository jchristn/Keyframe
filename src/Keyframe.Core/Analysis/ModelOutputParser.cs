namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;

    /// <summary>
    /// Leniently extracts a JSON object from model output: strips code fences and surrounding prose, then parses
    /// the outermost object. Model output is schemaless by nature, so values are returned as JSON elements.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class ModelOutputParser
    {
        /// <summary>
        /// Try to parse a JSON object from model output.
        /// </summary>
        /// <param name="text">Model output.</param>
        /// <param name="values">Parsed top-level properties (case-insensitive keys), cloned so they outlive the document.</param>
        /// <param name="error">Parse error, or null on success.</param>
        /// <returns>True when an object was parsed.</returns>
        public static bool TryParseObject(string? text, out Dictionary<string, JsonElement> values, out string? error)
        {
            values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            error = null;

            if (String.IsNullOrWhiteSpace(text))
            {
                error = "The response was empty.";
                return false;
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                error = "The response did not contain a JSON object.";
                return false;
            }

            string candidate = text.Substring(start, end - start + 1);
            try
            {
                JsonDocumentOptions options = new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };
                using (JsonDocument doc = JsonDocument.Parse(candidate, options))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    {
                        error = "The response JSON was not an object.";
                        return false;
                    }

                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                        values[prop.Name] = prop.Value.Clone();
                }

                return true;
            }
            catch (JsonException ex)
            {
                error = "The response JSON was invalid: " + ex.Message;
                return false;
            }
        }
    }
}
