namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// The principal resolved from the presented key.
    /// </summary>
    public class WhoAmIResponse
    {
        /// <summary>
        /// Whether a valid key was presented (or authentication is disabled).
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// Whether the admin key was presented.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Access key name, "admin" for the admin key, or "anonymous" when authentication is disabled.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// Whether the server requires authentication.
        /// </summary>
        public bool AuthenticationRequired { get; set; } = true;

        /// <summary>
        /// Server version.
        /// </summary>
        public string Version { get; set; } = string.Empty;
    }
}
