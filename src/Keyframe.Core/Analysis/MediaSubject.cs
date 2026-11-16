namespace Keyframe.Core.Analysis
{
    using System.Collections.Generic;
    using Keyframe.Core.Inference;

    /// <summary>
    /// What a prompt is about: the attached images plus any text evidence (transcript, scene notes, caller context).
    /// </summary>
    public class MediaSubject
    {
        /// <summary>
        /// Short noun phrase used in instructions, for example "the image" or "the scene".
        /// </summary>
        public string Noun { get; set; } = "the image";

        /// <summary>
        /// One or two sentences describing the attached evidence.
        /// </summary>
        public string Description { get; set; } = "One image is attached.";

        /// <summary>
        /// Caller-supplied background, treated as data.
        /// </summary>
        public string? CallerContext { get; set; } = null;

        /// <summary>
        /// Transcript text for the covered time range.
        /// </summary>
        public string? Transcript { get; set; } = null;

        /// <summary>
        /// Notes from earlier analysis, such as scene summaries for a whole-video question.
        /// </summary>
        public string? Notes { get; set; } = null;

        /// <summary>
        /// Images, in order. May be empty for text-only reasoning. Never null.
        /// </summary>
        public List<InferenceImage> Images { get; set; } = new List<InferenceImage>();
    }
}
