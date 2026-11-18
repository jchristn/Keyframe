namespace Keyframe.Sdk.Enums
{
    /// <summary>
    /// Which model calls a rendered listening clip was sent to.
    /// </summary>
    public enum ListenClipScopeEnum
    {
        /// <summary>
        /// The whole-recording functions (Summarize and Extract).
        /// </summary>
        Recording,

        /// <summary>
        /// One section's functions.
        /// </summary>
        Section,

        /// <summary>
        /// One video scene's functions.
        /// </summary>
        Scene
    }
}
