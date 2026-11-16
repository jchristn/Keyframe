namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Detailed outcome for one extraction field.
    /// </summary>
    public class FieldResult
    {
        /// <summary>
        /// Field key.
        /// </summary>
        public string Key { get; set; } = string.Empty;

        /// <summary>
        /// Field type.
        /// </summary>
        public FieldTypeEnum Type { get; set; } = FieldTypeEnum.String;

        /// <summary>
        /// Coerced value, or the field default when the model could not answer. Schemaless by design.
        /// </summary>
        public object? Value { get; set; } = null;

        /// <summary>
        /// Whether the model output was coerced to the field type successfully.
        /// </summary>
        public bool Valid { get; set; } = false;

        /// <summary>
        /// Model output for the field before coercion, as text.
        /// </summary>
        public string? RawValue { get; set; } = null;

        /// <summary>
        /// Error when the value could not be produced.
        /// </summary>
        public string? Error { get; set; } = null;

        /// <summary>
        /// Non-fatal adjustments, such as clamping or truncation. Never null.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();
    }
}
