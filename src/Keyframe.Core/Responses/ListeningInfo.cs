namespace Keyframe.Core.Responses
{
    using System.Collections.Generic;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;

    /// <summary>
    /// Whether, how, and by which model the audio was heard. Always present on audio results and on video results with
    /// scenes, so a consumer never has to infer from absence whether the audio was heard.
    /// </summary>
    public class ListeningInfo
    {
        /// <summary>
        /// True when at least one model call carried audio, including a call that then failed.
        /// </summary>
        public bool Listened { get; set; } = false;

        /// <summary>
        /// What the whole-recording functions heard. Scenes appears only on video. None whenever Listened is false.
        /// </summary>
        public ListeningModeEnum Mode { get; set; } = ListeningModeEnum.None;

        /// <summary>
        /// Stable reason code when the audio was not heard; null when listening happened.
        /// </summary>
        public ListenSkipReasonEnum? Reason { get; set; } = null;

        /// <summary>
        /// Listening endpoint name, or null when not listened.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Listening model, or null when not listened.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// The effective rendition, with every member filled.
        /// </summary>
        public AudioRenditionSpec Rendition { get; set; } = new AudioRenditionSpec();

        /// <summary>
        /// Length of the media in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Length of the distinct audio the models heard, in milliseconds. Overlapping clips are counted once.
        /// </summary>
        public long HeardMs { get; set; } = 0;

        /// <summary>
        /// Every clip rendered and sent, in order. Empty when not listened.
        /// </summary>
        public List<ListenClip> Clips { get; set; } = new List<ListenClip>();
    }
}
