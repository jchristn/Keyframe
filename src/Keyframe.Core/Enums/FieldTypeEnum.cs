namespace Keyframe.Core.Enums
{
    /// <summary>
    /// Data type of an extraction field value.
    /// </summary>
    public enum FieldTypeEnum
    {
        /// <summary>
        /// True or false.
        /// </summary>
        Boolean,

        /// <summary>
        /// Free text.
        /// </summary>
        String,

        /// <summary>
        /// Whole number.
        /// </summary>
        Integer,

        /// <summary>
        /// Decimal number.
        /// </summary>
        Number,

        /// <summary>
        /// One value from a fixed list of options.
        /// </summary>
        Enum,

        /// <summary>
        /// List of strings.
        /// </summary>
        StringList,

        /// <summary>
        /// Arbitrary JSON object, optionally validated against a JSON schema.
        /// </summary>
        Object
    }
}
