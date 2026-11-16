namespace Keyframe.Core.Analysis
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;

    /// <summary>
    /// Builds JSON Schema text for extraction fields, used for structured output and by the mock provider.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class FieldSchemaBuilder
    {
        /// <summary>
        /// Schema for one JSON object holding every field.
        /// </summary>
        /// <param name="fields">Fields.</param>
        /// <returns>JSON Schema text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when fields is null.</exception>
        public static string Combined(List<FieldSpec> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);
            Dictionary<string, object> properties = new Dictionary<string, object>();
            foreach (FieldSpec field in fields) properties[field.Key] = ForField(field);

            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", properties },
                { "required", fields.Select(f => f.Key).ToList() },
                { "additionalProperties", false }
            };

            return JsonSerializer.Serialize(schema);
        }

        /// <summary>
        /// Schema for a single-field answer of the form {"value": ...}.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <returns>JSON Schema text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when field is null.</exception>
        public static string Single(FieldSpec field)
        {
            ArgumentNullException.ThrowIfNull(field);
            Dictionary<string, object> schema = new Dictionary<string, object>
            {
                { "type", "object" },
                { "properties", new Dictionary<string, object> { { "value", ForField(field) } } },
                { "required", new List<string> { "value" } },
                { "additionalProperties", false }
            };

            return JsonSerializer.Serialize(schema);
        }

        /// <summary>
        /// Schema fragment for one field value.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <returns>Schema object.</returns>
        /// <exception cref="ArgumentNullException">Thrown when field is null.</exception>
        public static Dictionary<string, object> ForField(FieldSpec field)
        {
            ArgumentNullException.ThrowIfNull(field);
            Dictionary<string, object> schema = new Dictionary<string, object>();

            switch (field.Type)
            {
                case FieldTypeEnum.Boolean:
                    schema["type"] = "boolean";
                    break;
                case FieldTypeEnum.Integer:
                    schema["type"] = "integer";
                    if (field.Minimum.HasValue) schema["minimum"] = (long)Math.Ceiling(field.Minimum.Value);
                    if (field.Maximum.HasValue) schema["maximum"] = (long)Math.Floor(field.Maximum.Value);
                    break;
                case FieldTypeEnum.Number:
                    schema["type"] = "number";
                    if (field.Minimum.HasValue) schema["minimum"] = field.Minimum.Value;
                    if (field.Maximum.HasValue) schema["maximum"] = field.Maximum.Value;
                    break;
                case FieldTypeEnum.Enum:
                    schema["type"] = "string";
                    schema["enum"] = field.Options ?? new List<string>();
                    break;
                case FieldTypeEnum.StringList:
                    schema["type"] = "array";
                    schema["items"] = new Dictionary<string, object> { { "type", "string" } };
                    if (field.MaxItems.HasValue) schema["maxItems"] = field.MaxItems.Value;
                    break;
                case FieldTypeEnum.Object:
                    schema["type"] = "object";
                    break;
                default:
                    schema["type"] = "string";
                    if (field.MaxLength.HasValue) schema["maxLength"] = field.MaxLength.Value;
                    break;
            }

            schema["description"] = field.Prompt;
            return schema;
        }

        /// <summary>
        /// Human-readable type description used in prompts.
        /// </summary>
        /// <param name="field">Field.</param>
        /// <returns>Description such as "integer between 0 and 10".</returns>
        /// <exception cref="ArgumentNullException">Thrown when field is null.</exception>
        public static string Describe(FieldSpec field)
        {
            ArgumentNullException.ThrowIfNull(field);
            switch (field.Type)
            {
                case FieldTypeEnum.Boolean:
                    return "true or false";
                case FieldTypeEnum.Integer:
                    return "a whole number" + Range(field);
                case FieldTypeEnum.Number:
                    return "a number" + Range(field);
                case FieldTypeEnum.Enum:
                    return "exactly one of " + String.Join(", ", (field.Options ?? new List<string>()).Select(o => JsonSerializer.Serialize(o)));
                case FieldTypeEnum.StringList:
                    return "an array of strings" + (field.MaxItems.HasValue ? " with at most " + field.MaxItems.Value + " items" : string.Empty);
                case FieldTypeEnum.Object:
                    return "a JSON object" + (String.IsNullOrWhiteSpace(field.Schema) ? string.Empty : " matching this JSON Schema: " + field.Schema);
                default:
                    return "a string" + (field.MaxLength.HasValue ? " of at most " + field.MaxLength.Value + " characters" : string.Empty);
            }
        }

        private static string Range(FieldSpec field)
        {
            if (field.Minimum.HasValue && field.Maximum.HasValue) return " between " + field.Minimum.Value + " and " + field.Maximum.Value;
            if (field.Minimum.HasValue) return " of at least " + field.Minimum.Value;
            if (field.Maximum.HasValue) return " of at most " + field.Maximum.Value;
            return string.Empty;
        }
    }
}
