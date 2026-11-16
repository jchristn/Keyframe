namespace Keyframe.Core.Requests
{
    /// <summary>
    /// One piece of caller-supplied background. Every context item in a request is included in every prompt the
    /// request produces (summary, each field, each scene, and the whole-video step), as data rather than instructions.
    /// </summary>
    public class ContextItem
    {
        /// <summary>
        /// Optional short label, for example "Location" or "Event". Maximum 128 characters.
        /// </summary>
        public string? Label { get; set; } = null;

        /// <summary>
        /// Context text. Required.
        /// </summary>
        public string Text { get; set; } = string.Empty;
    }
}
