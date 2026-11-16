namespace Keyframe.Server.Routes
{
    /// <summary>
    /// Who may call a route.
    /// </summary>
    public enum RouteAuthEnum
    {
        /// <summary>
        /// Anyone, without a key.
        /// </summary>
        Anonymous,

        /// <summary>
        /// Any active access key or the admin key.
        /// </summary>
        Key,

        /// <summary>
        /// The admin key only.
        /// </summary>
        Admin
    }
}
