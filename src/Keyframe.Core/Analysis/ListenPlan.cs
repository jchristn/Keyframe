namespace Keyframe.Core.Analysis
{
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Outcome of resolving whether and through which endpoint an analysis listens to its audio.
    /// </summary>
    public class ListenPlan
    {
        /// <summary>
        /// The listening endpoint, or null when the analysis does not listen.
        /// </summary>
        public EndpointSettings? Endpoint { get; set; } = null;

        /// <summary>
        /// Model override from the request, or null to use the endpoint model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Why the analysis does not listen; null when it listens or when the request is rejected.
        /// </summary>
        public ListenSkipReasonEnum? Reason { get; set; } = null;

        /// <summary>
        /// Message for a request that must be rejected with 400, or null.
        /// </summary>
        public string? Rejection { get; set; } = null;

        /// <summary>
        /// Effective rendition, every member filled.
        /// </summary>
        public AudioRenditionSpec Rendition { get; set; } = new AudioRenditionSpec();

        /// <summary>
        /// True when the analysis listens through <see cref="Endpoint"/>.
        /// </summary>
        public bool Listens
        {
            get
            {
                return Endpoint != null && Reason == null && Rejection == null;
            }
        }

        /// <summary>
        /// True when listening was asked for and needed but no usable endpoint was found, so the analysis falls back to
        /// the transcript (NoEndpointConfigured, EndpointLacksAudioInput, EndpointUnhealthy, NoAudioTrack).
        /// </summary>
        public bool IsFallback
        {
            get
            {
                return Reason.HasValue
                    && Reason.Value != ListenSkipReasonEnum.NotRequested
                    && Reason.Value != ListenSkipReasonEnum.NoModelFunctions;
            }
        }
    }
}
