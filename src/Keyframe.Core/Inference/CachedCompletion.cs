namespace Keyframe.Core.Inference
{
    using Keyframe.Core.Settings;

    /// <summary>
    /// A cached completion client and the settings object it was built from.
    /// </summary>
    public class CachedCompletion
    {
        /// <summary>
        /// Settings the client was built from.
        /// </summary>
        public EndpointSettings Settings { get; }

        /// <summary>
        /// Client.
        /// </summary>
        public IInferenceClient Client { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="client">Client.</param>
        public CachedCompletion(EndpointSettings settings, IInferenceClient client)
        {
            Settings = settings;
            Client = client;
        }
    }
}
