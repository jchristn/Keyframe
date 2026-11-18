namespace Keyframe.Sdk.Enums
{
    /// <summary>
    /// Stable reason code for a request that did not listen to its audio.
    /// </summary>
    public enum ListenSkipReasonEnum
    {
        /// <summary>
        /// The request turned listening off.
        /// </summary>
        NotRequested,

        /// <summary>
        /// No model function (Summarize, Extract, or a section function) was enabled, so nothing needed the audio.
        /// </summary>
        NoModelFunctions,

        /// <summary>
        /// No listening endpoint was named and Inference.DefaultListening is unset or inactive.
        /// </summary>
        NoEndpointConfigured,

        /// <summary>
        /// The resolved endpoint's provider cannot take audio input.
        /// </summary>
        EndpointLacksAudioInput,

        /// <summary>
        /// The resolved endpoint was unhealthy when the job started.
        /// </summary>
        EndpointUnhealthy,

        /// <summary>
        /// The media has no audio track.
        /// </summary>
        NoAudioTrack
    }
}
