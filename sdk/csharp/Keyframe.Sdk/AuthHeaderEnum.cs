namespace Keyframe.Sdk
{
    /// <summary>
    /// How the client presents its API key to the server.
    /// </summary>
    public enum AuthHeaderEnum
    {
        /// <summary>
        /// Send "Authorization: Bearer {key}" (default).
        /// </summary>
        Bearer,

        /// <summary>
        /// Send "x-api-key: {key}".
        /// </summary>
        XApiKey
    }
}
