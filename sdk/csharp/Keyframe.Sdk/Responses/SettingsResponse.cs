namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;
    using Keyframe.Sdk.Settings;

    /// <summary>
    /// Masked settings plus per-field metadata.
    /// </summary>
    public class SettingsResponse
    {
        /// <summary>
        /// Settings with secrets masked. Masked values may be sent back unchanged to keep the stored secret.
        /// </summary>
        public KeyframeSettings Settings { get; set; } = new KeyframeSettings();

        /// <summary>
        /// Field metadata.
        /// </summary>
        public List<SettingsFieldMetadata> Fields { get; set; } = new List<SettingsFieldMetadata>();

        /// <summary>
        /// Whether a saved change is waiting for a restart.
        /// </summary>
        public bool RestartRequired { get; set; } = false;

        /// <summary>
        /// Settings file path.
        /// </summary>
        public string? SettingsFile { get; set; } = null;
    }
}
