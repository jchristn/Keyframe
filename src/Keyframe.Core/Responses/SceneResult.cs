namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;

    /// <summary>
    /// Result for one video scene.
    /// </summary>
    public class SceneResult
    {
        /// <summary>
        /// Zero-based scene index.
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// Start offset in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End offset in milliseconds.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Duration in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Keyframes analyzed for the scene. Never null.
        /// </summary>
        public List<KeyframeInfo> Keyframes { get; set; } = new List<KeyframeInfo>();

        /// <summary>
        /// Transcript text spoken during the scene, when transcription ran.
        /// </summary>
        public string? TranscriptText { get; set; } = null;

        /// <summary>
        /// Scene results keyed by the caller's scene summary and field keys. Never null.
        /// </summary>
        public Dictionary<string, object?> Results { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// Per-field detail. Never null.
        /// </summary>
        public List<FieldResult> Fields { get; set; } = new List<FieldResult>();

        /// <summary>
        /// Errors for this scene. Never null.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }
}
