namespace Keyframe.Sdk.Enums
{
    /// <summary>
    /// Container and codec of the audio sent to a listening model.
    /// </summary>
    public enum ListenFormatEnum
    {
        /// <summary>
        /// MPEG layer 3 at a constant bitrate.
        /// </summary>
        Mp3,

        /// <summary>
        /// 16-bit PCM in a WAV container.
        /// </summary>
        Wav,

        /// <summary>
        /// Lossless FLAC. Not accepted by OpenAI-compatible endpoints.
        /// </summary>
        Flac
    }
}
