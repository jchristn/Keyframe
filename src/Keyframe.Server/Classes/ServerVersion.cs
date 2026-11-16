namespace Keyframe.Server.Classes
{
    using System.Reflection;

    /// <summary>
    /// The server's informational version.
    /// </summary>
    public static class ServerVersion
    {
        /// <summary>
        /// Version string, for example "0.1.0".
        /// </summary>
        public static string Value
        {
            get
            {
                string? info = typeof(ServerVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(info)) return "0.0.0";
                int plus = info.IndexOf('+');
                return plus > 0 ? info.Substring(0, plus) : info;
            }
        }
    }
}
