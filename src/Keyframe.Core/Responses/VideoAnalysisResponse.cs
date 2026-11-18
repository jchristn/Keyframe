namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result of a video analysis.
    /// </summary>
    public class VideoAnalysisResponse : AnalysisResponse
    {
        /// <summary>
        /// Transcript, when transcription ran.
        /// </summary>
        public TranscriptResult? Transcript { get; set; } = null;

        /// <summary>
        /// Scenes, when scene breakdown ran. Never null.
        /// </summary>
        public List<SceneResult> Scenes { get; set; } = new List<SceneResult>();

        /// <summary>
        /// Whether scene calls heard the scene audio, present whenever scenes are enabled.
        /// </summary>
        public ListeningInfo? Listening { get; set; } = null;
    }
}
