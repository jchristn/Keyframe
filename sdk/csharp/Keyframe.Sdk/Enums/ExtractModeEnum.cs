namespace Keyframe.Sdk.Enums
{
    /// <summary>
    /// How extraction fields are sent to the model.
    /// </summary>
    public enum ExtractModeEnum
    {
        /// <summary>
        /// One model call returns a JSON object containing every field.
        /// </summary>
        Combined,

        /// <summary>
        /// One model call per field, run in parallel up to the endpoint concurrency limit.
        /// </summary>
        PerField
    }
}
