namespace Keyframe.Core.Analysis
{
    using System.Collections.Generic;
    using Keyframe.Core.Responses;

    /// <summary>
    /// Outcome of an extract function.
    /// </summary>
    public class ExtractionOutcome
    {
        /// <summary>
        /// Whether extraction completed. Invalid optional fields do not make it fail; failed calls and invalid required fields do.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Per-field results in the requested order. Never null.
        /// </summary>
        public List<FieldResult> Fields { get; set; } = new List<FieldResult>();

        /// <summary>
        /// Errors. Never null.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }
}
