namespace Keyframe.Core.Inference
{
    using Keyframe.Core.Settings;

    /// <summary>
    /// A cached transcription client and the settings object it was built from.
    /// </summary>
    public class CachedTranscription
    {
        /// <summary>
        /// Settings the client was built from.
        /// </summary>
        public EndpointSettings Settings { get; }

        /// <summary>
        /// Client.
        /// </summary>
        public ITranscriptionClient Client { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="client">Client.</param>
        public CachedTranscription(EndpointSettings settings, ITranscriptionClient client)
        {
            Settings = settings;
            Client = client;
        }
    }
}
