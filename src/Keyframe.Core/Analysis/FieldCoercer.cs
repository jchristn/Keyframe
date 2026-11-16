namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.Json;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;

    /// <summary>
    /// Coerces model output into typed field values, applying constraints and recording warnings.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class FieldCoercer
    {
        #region Private-Members

        private static readonly string[] _NumberWords = new[]
        {
            "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty"
        };

        private static readonly string[] _True = new[] { "true", "yes", "y", "1", "present", "visible" };
        private static readonly string[] _False = new[] { "false", "no", "n", "0", "none", "absent", "not visible" };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Coerce a model value for a field.
        /// </summary>
        /// <param name="field">Field specification.</param>
        /// <param name="present">Whether the model returned the key at all.</param>
        /// <param name="value">Value returned by the model.</param>
        /// <returns>Field result. When the value is missing or invalid, Value holds the field default (or null).</returns>
        /// <exception cref="ArgumentNullException">Thrown when field is null.</exception>
        public static FieldResult Coerce(FieldSpec field, bool present, JsonElement value)
        {
            ArgumentNullException.ThrowIfNull(field);
            FieldResult result = new FieldResult { Key = field.Key, Type = field.Type };

            if (!present || value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined)
            {
                result.RawValue = present ? "null" : null;
                result.Error = present ? "The model could not determine a value." : "The model did not return this field.";
                ApplyDefault(field, result);
                return result;
            }

            result.RawValue = value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

            string? error;
            object? coerced = CoerceValue(field, value, result.Warnings, out error);
            if (error != null)
            {
                result.Error = error;
                ApplyDefault(field, result);
                return result;
            }

            result.Value = coerced;
            result.Valid = true;
            return result;
        }

        /// <summary>
        /// Build a failed result for a field, for example when the model call itself failed.
        /// </summary>
        /// <param name="field">Field specification.</param>
        /// <param name="error">Error message.</param>
        /// <returns>Field result holding the field default (or null).</returns>
        /// <exception cref="ArgumentNullException">Thrown when field is null.</exception>
        public static FieldResult Failed(FieldSpec field, string error)
        {
            ArgumentNullException.ThrowIfNull(field);
            FieldResult result = new FieldResult { Key = field.Key, Type = field.Type, Error = error };
            ApplyDefault(field, result);
            return result;
        }

        #endregion

        #region Private-Methods

        private static void ApplyDefault(FieldSpec field, FieldResult result)
        {
            result.Valid = false;
            result.Value = null;
            if (field.Default == null) return;

            JsonElement element;
            if (field.Default is JsonElement je)
            {
                element = je;
            }
            else
            {
                using (JsonDocument doc = JsonDocument.Parse(KeyframeSerializer.Serialize(field.Default)))
                {
                    element = doc.RootElement.Clone();
                }
            }

            if (element.ValueKind == JsonValueKind.Null) return;

            List<string> ignored = new List<string>();
            object? coerced = CoerceValue(field, element, ignored, out string? error);
            if (error == null)
            {
                result.Value = coerced;
                result.Warnings.Add("Default value used.");
            }
        }

        private static object? CoerceValue(FieldSpec field, JsonElement value, List<string> warnings, out string? error)
        {
            error = null;
            switch (field.Type)
            {
                case FieldTypeEnum.Boolean:
                    return CoerceBoolean(value, out error);
                case FieldTypeEnum.Integer:
                    return CoerceInteger(field, value, warnings, out error);
                case FieldTypeEnum.Number:
                    return CoerceNumber(field, value, warnings, out error);
                case FieldTypeEnum.Enum:
                    return CoerceEnum(field, value, out error);
                case FieldTypeEnum.StringList:
                    return CoerceList(field, value, warnings);
                case FieldTypeEnum.Object:
                    return CoerceObject(field, value, out error);
                default:
                    return CoerceString(field, value, warnings);
            }
        }

        private static object? CoerceBoolean(JsonElement value, out string? error)
        {
            error = null;
            if (value.ValueKind == JsonValueKind.True) return true;
            if (value.ValueKind == JsonValueKind.False) return false;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d)) return d != 0;

            string text = Normalize(AsText(value));
            if (_True.Contains(text)) return true;
            if (_False.Contains(text)) return false;
            if (text.StartsWith("yes ", StringComparison.Ordinal) || text.StartsWith("yes,", StringComparison.Ordinal)) return true;
            if (text.StartsWith("no ", StringComparison.Ordinal) || text.StartsWith("no,", StringComparison.Ordinal)) return false;

            error = "Value '" + AsText(value) + "' is not a boolean.";
            return null;
        }

        private static object? CoerceInteger(FieldSpec field, JsonElement value, List<string> warnings, out string? error)
        {
            error = null;
            double number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d))
            {
                number = d;
            }
            else
            {
                string text = Normalize(AsText(value));
                int word = Array.IndexOf(_NumberWords, text);
                if (word >= 0) number = word;
                else if (!TryParseLeadingNumber(text, out number))
                {
                    error = "Value '" + AsText(value) + "' is not a whole number.";
                    return null;
                }
            }

            if (Math.Abs(number - Math.Round(number)) > 1e-9)
                warnings.Add("Rounded " + number.ToString(CultureInfo.InvariantCulture) + " to a whole number.");

            double clamped = Clamp(field, Math.Round(number), warnings);
            return (long)clamped;
        }

        private static object? CoerceNumber(FieldSpec field, JsonElement value, List<string> warnings, out string? error)
        {
            error = null;
            double number;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double d))
            {
                number = d;
            }
            else if (!TryParseLeadingNumber(Normalize(AsText(value)), out number))
            {
                error = "Value '" + AsText(value) + "' is not a number.";
                return null;
            }

            return Clamp(field, number, warnings);
        }

        private static object? CoerceEnum(FieldSpec field, JsonElement value, out string? error)
        {
            error = null;
            List<string> options = field.Options ?? new List<string>();
            string text = AsText(value).Trim().Trim('"', '\'', '.', ' ');

            string? match = options.FirstOrDefault(o => String.Equals(o, text, StringComparison.OrdinalIgnoreCase));
            if (match == null) match = options.FirstOrDefault(o => String.Equals(Normalize(o), Normalize(text), StringComparison.Ordinal));
            if (match != null) return match;

            error = "Value '" + text + "' is not one of: " + String.Join(", ", options) + ".";
            return null;
        }

        private static object? CoerceList(FieldSpec field, JsonElement value, List<string> warnings)
        {
            List<string> items = new List<string>();
            if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Null) continue;
                    string text = AsText(item).Trim();
                    if (text.Length > 0) items.Add(text);
                }
            }
            else
            {
                string text = AsText(value);
                foreach (string part in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string trimmed = part.Trim().TrimStart('-', '*', ' ').Trim();
                    if (trimmed.Length > 0) items.Add(trimmed);
                }

                if (items.Count > 0) warnings.Add("Split a text answer into a list.");
            }

            if (field.MaxItems.HasValue && items.Count > field.MaxItems.Value)
            {
                warnings.Add("Truncated " + items.Count + " items to " + field.MaxItems.Value + ".");
                items = items.Take(field.MaxItems.Value).ToList();
            }

            return items;
        }

        private static object? CoerceObject(FieldSpec field, JsonElement value, out string? error)
        {
            error = null;
            JsonElement obj = value;

            if (value.ValueKind == JsonValueKind.String)
            {
                string? text = value.GetString();
                if (!ModelOutputParser.TryParseObject(text, out Dictionary<string, JsonElement> parsed, out string? parseError))
                {
                    error = "Value is not a JSON object: " + parseError;
                    return null;
                }

                using (JsonDocument doc = JsonDocument.Parse(JsonSerializer.Serialize(parsed)))
                {
                    obj = doc.RootElement.Clone();
                }
            }

            if (obj.ValueKind != JsonValueKind.Object)
            {
                error = "Value is not a JSON object.";
                return null;
            }

            if (!String.IsNullOrWhiteSpace(field.Schema))
            {
                List<string> missing = RequiredKeysMissing(field.Schema, obj);
                if (missing.Count > 0)
                {
                    error = "Object is missing required properties: " + String.Join(", ", missing) + ".";
                    return null;
                }
            }

            return obj;
        }

        private static object CoerceString(FieldSpec field, JsonElement value, List<string> warnings)
        {
            string text = AsText(value).Trim();
            if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value)
            {
                warnings.Add("Truncated " + text.Length + " characters to " + field.MaxLength.Value + ".");
                text = text.Substring(0, field.MaxLength.Value);
            }

            return text;
        }

        private static double Clamp(FieldSpec field, double number, List<string> warnings)
        {
            if (field.Minimum.HasValue && number < field.Minimum.Value)
            {
                warnings.Add("Raised " + number.ToString(CultureInfo.InvariantCulture) + " to the minimum " + field.Minimum.Value.ToString(CultureInfo.InvariantCulture) + ".");
                return field.Minimum.Value;
            }

            if (field.Maximum.HasValue && number > field.Maximum.Value)
            {
                warnings.Add("Lowered " + number.ToString(CultureInfo.InvariantCulture) + " to the maximum " + field.Maximum.Value.ToString(CultureInfo.InvariantCulture) + ".");
                return field.Maximum.Value;
            }

            return number;
        }

        private static bool TryParseLeadingNumber(string text, out double number)
        {
            number = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string cleaned = text.Replace(",", string.Empty).Trim();
            if (Double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return true;

            int end = 0;
            while (end < cleaned.Length && (Char.IsDigit(cleaned[end]) || cleaned[end] == '.' || (end == 0 && cleaned[end] == '-'))) end++;
            return end > 0 && Double.TryParse(cleaned.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        private static List<string> RequiredKeysMissing(string schema, JsonElement obj)
        {
            List<string> missing = new List<string>();
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(schema))
                {
                    if (doc.RootElement.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement key in required.EnumerateArray())
                        {
                            string? name = key.GetString();
                            if (name != null && !obj.TryGetProperty(name, out JsonElement _)) missing.Add(name);
                        }
                    }
                }
            }
            catch (JsonException)
            {
            }

            return missing;
        }

        private static string AsText(JsonElement value)
        {
            return value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : value.GetRawText();
        }

        private static string Normalize(string text)
        {
            return text.Trim().Trim('"', '\'', '.', '!', ' ').ToLowerInvariant();
        }

        #endregion
    }
}
