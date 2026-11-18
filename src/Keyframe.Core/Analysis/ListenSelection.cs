namespace Keyframe.Core.Analysis
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference;

    /// <summary>
    /// The audio clips chosen for one set of model calls and the mode they represent.
    /// </summary>
    public class ListenSelection
    {
        /// <summary>
        /// Rendered clips, in order. Never null.
        /// </summary>
        public List<InferenceAudio> Clips { get; set; } = new List<InferenceAudio>();

        /// <summary>
        /// Mode the clips represent (Full or Excerpts for a recording).
        /// </summary>
        public ListeningModeEnum Mode { get; set; } = ListeningModeEnum.None;
    }
}
