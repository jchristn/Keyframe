namespace Keyframe.Server.Classes
{
    using System;

    /// <summary>
    /// Per-request authentication and correlation state, stored in the Watson context metadata.
    /// </summary>
    public class RequestContext
    {
        #region Public-Members

        /// <summary>
        /// Request identifier echoed in the x-request-id header and error bodies.
        /// </summary>
        public string RequestId { get; set; } = String.Empty;

        /// <summary>
        /// Whether a valid key was presented, or authentication is disabled.
        /// </summary>
        public bool IsAuthenticated { get; set; } = false;

        /// <summary>
        /// Whether the admin key was presented, or authentication is disabled.
        /// </summary>
        public bool IsAdmin { get; set; } = false;

        /// <summary>
        /// Access key name, "admin", "anonymous" when authentication is disabled, or null.
        /// </summary>
        public string? PrincipalName { get; set; } = null;

        /// <summary>
        /// UTC time the request arrived.
        /// </summary>
        public DateTime ReceivedUtc { get; set; } = DateTime.UtcNow;

        #endregion
    }
}
