namespace Keyframe.Core.Requests
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;

    /// <summary>
    /// One metadata field the model is asked to answer.
    /// </summary>
    public class FieldSpec
    {
        /// <summary>
        /// Key under which the value is returned in Results. 1 to 64 characters: letters, digits, underscore, hyphen, or period.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Value type. Default String.
        /// </summary>
        public FieldTypeEnum Type { get; set; } = FieldTypeEnum.String;

        /// <summary>
        /// Question or instruction for the model. Required. Maximum 4000 characters.
        /// </summary>
        public string Prompt { get; set; } = string.Empty;

        /// <summary>
        /// Whether a missing or invalid value marks the analysis as failed. Default false.
        /// </summary>
        public bool Required { get; set; } = false;

        /// <summary>
        /// Value returned when the model cannot answer. Schemaless by design: its JSON type follows the field Type.
        /// </summary>
        public object? Default { get; set; } = null;

        /// <summary>
        /// Allowed values. Required for Enum fields.
        /// </summary>
        public List<string>? Options { get; set; } = null;

        /// <summary>
        /// Minimum value for Integer and Number fields.
        /// </summary>
        public double? Minimum { get; set; } = null;

        /// <summary>
        /// Maximum value for Integer and Number fields.
        /// </summary>
        public double? Maximum { get; set; } = null;

        /// <summary>
        /// Maximum length for String fields.
        /// </summary>
        public int? MaxLength { get; set; } = null;

        /// <summary>
        /// Maximum items for StringList fields.
        /// </summary>
        public int? MaxItems { get; set; } = null;

        /// <summary>
        /// Optional JSON Schema text describing an Object field.
        /// </summary>
        public string? Schema { get; set; } = null;
    }
}
