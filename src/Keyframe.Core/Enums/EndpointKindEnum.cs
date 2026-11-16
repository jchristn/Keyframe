namespace Keyframe.Core.Enums
{
    /// <summary>
    /// What an inference endpoint is used for.
    /// </summary>
    public enum EndpointKindEnum
    {
        /// <summary>
        /// Chat or vision completion.
        /// </summary>
        Completion,

        /// <summary>
        /// Speech-to-text transcription.
        /// </summary>
        Transcription
    }
}
