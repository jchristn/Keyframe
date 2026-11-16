namespace Keyframe.Sdk.Responses
{
    /// <summary>
    /// How a settings field is applied.
    /// </summary>
    public class SettingsFieldMetadata
    {
        /// <summary>
        /// Dotted path, for example "Rest.Port".
        /// </summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// "Live" when applied immediately, "Restart" when a restart is required.
        /// </summary>
        public string ApplyMode { get; set; } = "Live";

        /// <summary>
        /// Whether an environment variable overrides the file value.
        /// </summary>
        public bool OverriddenByEnvironment { get; set; } = false;

        /// <summary>
        /// Environment variable name when overridden.
        /// </summary>
        public string? EnvironmentVariable { get; set; } = null;

        /// <summary>
        /// Whether the value is a secret that is masked in responses.
        /// </summary>
        public bool Secret { get; set; } = false;
    }
}
