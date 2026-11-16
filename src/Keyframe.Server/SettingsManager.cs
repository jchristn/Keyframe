namespace Keyframe.Server
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Loads, overrides, masks, validates, and persists settings. The effective settings are the file settings with
    /// environment overrides applied; overrides are never written back to the file.
    /// Thread safety: safe for concurrent use. Readers get an immutable snapshot reference.
    /// </summary>
    public class SettingsManager
    {
        #region Public-Members

        /// <summary>
        /// Environment variable naming the settings file.
        /// </summary>
        public const string SettingsFileVariable = "KEYFRAME_SETTINGS_FILE";

        /// <summary>
        /// Environment variable overriding the admin key.
        /// </summary>
        public const string AdminKeyVariable = "KEYFRAME_ADMIN_API_KEY";

        /// <summary>
        /// Environment variable adding or replacing the "default" access key.
        /// </summary>
        public const string AccessKeyVariable = "KEYFRAME_ACCESS_KEY";

        /// <summary>
        /// Prefix of masked secret values.
        /// </summary>
        public const string MaskPrefix = "****";

        /// <summary>
        /// Effective settings snapshot. Replace, never mutate, through <see cref="UpdateAsync"/>.
        /// </summary>
        public KeyframeSettings Settings
        {
            get
            {
                return Volatile.Read(ref _Effective);
            }
        }

        /// <summary>
        /// Settings file path.
        /// </summary>
        public string SettingsFile { get; }

        /// <summary>
        /// Whether a saved change needs a restart to take effect.
        /// </summary>
        public bool RestartRequired { get; private set; } = false;

        #endregion

        #region Private-Members

        private static readonly string[] _RestartSections = new string[] { "Rest", "Database", "Telemetry", "Logging" };
        private static readonly HashSet<string> _SecretPaths = new HashSet<string>(StringComparer.Ordinal)
        {
            "Auth.AdminApiKey", "Auth.AccessKeys[].Key", "Database.Password", "Inference.Endpoints[].ApiKey"
        };

        private readonly Func<string, string?> _Environment;
        private readonly SemaphoreSlim _WriteLock = new SemaphoreSlim(1, 1);
        private KeyframeSettings _File;
        private KeyframeSettings _Effective;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Load settings from a file, creating it with defaults when missing.
        /// </summary>
        /// <param name="settingsFile">Settings file path.</param>
        /// <param name="environment">Environment lookup; defaults to process environment variables.</param>
        /// <exception cref="ArgumentNullException">Thrown when settingsFile is null or empty.</exception>
        /// <exception cref="InvalidDataException">Thrown when the file cannot be parsed.</exception>
        public SettingsManager(string settingsFile, Func<string, string?>? environment = null)
        {
            if (String.IsNullOrWhiteSpace(settingsFile)) throw new ArgumentNullException(nameof(settingsFile));
            SettingsFile = Path.GetFullPath(settingsFile);
            _Environment = environment ?? Environment.GetEnvironmentVariable;

            if (File.Exists(SettingsFile))
            {
                string json = File.ReadAllText(SettingsFile, Encoding.UTF8);
                KeyframeSettings? parsed;
                try
                {
                    parsed = KeyframeSerializer.Deserialize<KeyframeSettings>(json);
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("Settings file '" + SettingsFile + "' could not be parsed: " + ex.Message, ex);
                }

                _File = parsed ?? new KeyframeSettings();
            }
            else
            {
                _File = new KeyframeSettings();
                string? directory = Path.GetDirectoryName(SettingsFile);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(SettingsFile, KeyframeSerializer.Serialize(_File, true), new UTF8Encoding(false));
            }

            _Effective = ApplyOverrides(Clone(_File));
        }

        /// <summary>
        /// Wrap in-memory settings without a file on disk. Used by tests; persistence writes to the given path.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <param name="settingsFile">Path used when persisting.</param>
        /// <param name="environment">Environment lookup.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or settingsFile is null.</exception>
        public SettingsManager(KeyframeSettings settings, string settingsFile, Func<string, string?>? environment = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (String.IsNullOrWhiteSpace(settingsFile)) throw new ArgumentNullException(nameof(settingsFile));
            SettingsFile = Path.GetFullPath(settingsFile);
            _Environment = environment ?? (name => null);
            _File = Clone(settings);
            _Effective = ApplyOverrides(Clone(_File));
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Settings with secrets masked, plus field metadata.
        /// </summary>
        /// <returns>Response.</returns>
        public SettingsResponse GetMasked()
        {
            KeyframeSettings masked = Clone(Settings);
            masked.Auth.AdminApiKey = Mask(masked.Auth.AdminApiKey) ?? MaskPrefix;
            foreach (AccessKeySettings key in masked.Auth.AccessKeys) key.Key = Mask(key.Key) ?? MaskPrefix;
            masked.Database.Password = Mask(masked.Database.Password);
            foreach (EndpointSettings endpoint in masked.Inference.Endpoints) endpoint.ApiKey = Mask(endpoint.ApiKey);

            return new SettingsResponse
            {
                Settings = masked,
                Fields = BuildMetadata(),
                RestartRequired = RestartRequired,
                SettingsFile = SettingsFile
            };
        }

        /// <summary>
        /// Validate, merge masked secrets, persist, and apply new settings.
        /// </summary>
        /// <param name="incoming">Settings as edited by the caller; masked secrets keep their stored values.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>The masked result.</returns>
        /// <exception cref="ArgumentNullException">Thrown when incoming is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the settings are invalid.</exception>
        public async Task<SettingsResponse> UpdateAsync(KeyframeSettings incoming, CancellationToken token = default)
        {
            if (incoming == null) throw new ArgumentNullException(nameof(incoming));

            await _WriteLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                KeyframeSettings next = Clone(incoming);
                KeyframeSettings current = _File;
                KeyframeSettings effective = _Effective;

                next.Auth.AdminApiKey = Restore(next.Auth.AdminApiKey, effective.Auth.AdminApiKey, current.Auth.AdminApiKey) ?? current.Auth.AdminApiKey;
                foreach (AccessKeySettings key in next.Auth.AccessKeys)
                {
                    AccessKeySettings? fileKey = current.Auth.AccessKeys.FirstOrDefault(k => String.Equals(k.Name, key.Name, StringComparison.Ordinal));
                    AccessKeySettings? effectiveKey = effective.Auth.AccessKeys.FirstOrDefault(k => String.Equals(k.Name, key.Name, StringComparison.Ordinal));
                    string? restored = Restore(key.Key, effectiveKey?.Key, fileKey?.Key);
                    if (restored == null) throw new ArgumentException("Access key '" + key.Name + "' has a masked value but no stored key to keep.");
                    key.Key = restored;
                }

                next.Database.Password = Restore(next.Database.Password, effective.Database.Password, current.Database.Password);
                foreach (EndpointSettings endpoint in next.Inference.Endpoints)
                {
                    EndpointSettings? fileEndpoint = current.Inference.Endpoints.FirstOrDefault(e => String.Equals(e.Name, endpoint.Name, StringComparison.OrdinalIgnoreCase));
                    EndpointSettings? effectiveEndpoint = effective.Inference.Endpoints.FirstOrDefault(e => String.Equals(e.Name, endpoint.Name, StringComparison.OrdinalIgnoreCase));
                    endpoint.ApiKey = Restore(endpoint.ApiKey, effectiveEndpoint?.ApiKey, fileEndpoint?.ApiKey);
                }

                List<string> errors = Validate(next);
                if (errors.Count > 0) throw new ArgumentException(String.Join(" ", errors));

                bool restart = false;
                foreach (string section in _RestartSections)
                {
                    if (!String.Equals(SectionJson(current, section), SectionJson(next, section), StringComparison.Ordinal)) restart = true;
                }

                string json = KeyframeSerializer.Serialize(next, true);
                string temp = SettingsFile + ".tmp";
                await File.WriteAllTextAsync(temp, json, new UTF8Encoding(false), token).ConfigureAwait(false);
                try
                {
                    File.Move(temp, SettingsFile, true);
                }
                catch (IOException)
                {
                    // A bind-mounted single file (Docker) cannot be replaced by a rename; overwrite it in place.
                    File.Copy(temp, SettingsFile, true);
                    File.Delete(temp);
                }

                _File = next;
                Volatile.Write(ref _Effective, ApplyOverrides(Clone(next)));
                if (restart) RestartRequired = true;
                return GetMasked();
            }
            finally
            {
                _WriteLock.Release();
            }
        }

        /// <summary>
        /// Mask a secret as "****" plus its last four characters.
        /// </summary>
        /// <param name="value">Secret.</param>
        /// <returns>Masked value, or null when the secret is not set.</returns>
        public static string? Mask(string? value)
        {
            if (String.IsNullOrEmpty(value)) return null;
            if (value.Length <= 4) return MaskPrefix;
            return MaskPrefix + value.Substring(value.Length - 4);
        }

        /// <summary>
        /// Validate settings beyond what the setters enforce.
        /// </summary>
        /// <param name="settings">Settings.</param>
        /// <returns>Errors; empty when valid.</returns>
        public static List<string> Validate(KeyframeSettings settings)
        {
            List<string> errors = new List<string>();
            if (settings == null)
            {
                errors.Add("Settings are required.");
                return errors;
            }

            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EndpointSettings endpoint in settings.Inference.Endpoints)
            {
                if (!names.Add(endpoint.Name)) errors.Add("Endpoint name '" + endpoint.Name + "' is used more than once.");
            }

            HashSet<string> keyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (AccessKeySettings key in settings.Auth.AccessKeys)
            {
                if (!keyNames.Add(key.Name)) errors.Add("Access key name '" + key.Name + "' is used more than once.");
                if (key.Key.StartsWith(MaskPrefix, StringComparison.Ordinal)) errors.Add("Access key '" + key.Name + "' must not start with '" + MaskPrefix + "'.");
            }

            if (settings.Auth.AdminApiKey.StartsWith(MaskPrefix, StringComparison.Ordinal)) errors.Add("Auth.AdminApiKey must not start with '" + MaskPrefix + "'.");
            return errors;
        }

        #endregion

        #region Private-Methods

        private KeyframeSettings ApplyOverrides(KeyframeSettings settings)
        {
            string? admin = _Environment(AdminKeyVariable);
            if (!String.IsNullOrWhiteSpace(admin)) settings.Auth.AdminApiKey = admin;

            string? access = _Environment(AccessKeyVariable);
            if (!String.IsNullOrWhiteSpace(access))
            {
                AccessKeySettings? existing = settings.Auth.AccessKeys.FirstOrDefault(k => String.Equals(k.Name, "default", StringComparison.Ordinal));
                if (existing != null)
                {
                    existing.Key = access;
                    existing.Active = true;
                }
                else
                {
                    settings.Auth.AccessKeys.Add(new AccessKeySettings { Name = "default", Key = access, Active = true });
                }
            }

            return settings;
        }

        private static string? Restore(string? incoming, string? effective, string? stored)
        {
            if (incoming == null) return null;
            if (!incoming.StartsWith(MaskPrefix, StringComparison.Ordinal)) return incoming;
            if (effective != null && String.Equals(incoming, Mask(effective), StringComparison.Ordinal)) return stored ?? effective;
            if (stored != null && String.Equals(incoming, Mask(stored), StringComparison.Ordinal)) return stored;
            return stored;
        }

        private List<SettingsFieldMetadata> BuildMetadata()
        {
            List<SettingsFieldMetadata> fields = new List<SettingsFieldMetadata>();
            Walk(typeof(KeyframeSettings), String.Empty, fields, 0);
            return fields;
        }

        private void Walk(Type type, string prefix, List<SettingsFieldMetadata> fields, int depth)
        {
            if (depth > 4) return;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanRead && p.CanWrite))
            {
                string path = prefix.Length == 0 ? property.Name : prefix + "." + property.Name;
                Type propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

                if (IsLeaf(propertyType))
                {
                    fields.Add(Describe(path));
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(propertyType) && propertyType.IsGenericType)
                {
                    Type element = propertyType.GetGenericArguments()[0];
                    if (IsLeaf(element)) fields.Add(Describe(path));
                    else Walk(element, path + "[]", fields, depth + 1);
                    continue;
                }

                Walk(propertyType, path, fields, depth + 1);
            }
        }

        private SettingsFieldMetadata Describe(string path)
        {
            string section = path.Split('.')[0];
            bool restart = _RestartSections.Contains(section, StringComparer.Ordinal)
                || path == "Media.TempDirectory" || path == "Media.FfmpegPath" || path == "Media.FfprobePath";
            if (path.StartsWith("Logging.", StringComparison.Ordinal) && path != "Logging.LogDirectory" && path != "Logging.LogFilename") restart = true;

            SettingsFieldMetadata meta = new SettingsFieldMetadata
            {
                Path = path,
                ApplyMode = restart ? "Restart" : "Live",
                Secret = _SecretPaths.Contains(path)
            };

            if (path == "Auth.AdminApiKey" && !String.IsNullOrWhiteSpace(_Environment(AdminKeyVariable)))
            {
                meta.OverriddenByEnvironment = true;
                meta.EnvironmentVariable = AdminKeyVariable;
            }

            if (path == "Auth.AccessKeys[].Key" && !String.IsNullOrWhiteSpace(_Environment(AccessKeyVariable)))
            {
                meta.OverriddenByEnvironment = true;
                meta.EnvironmentVariable = AccessKeyVariable;
            }

            return meta;
        }

        private static bool IsLeaf(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime);
        }

        private static string SectionJson(KeyframeSettings settings, string section)
        {
            PropertyInfo? property = typeof(KeyframeSettings).GetProperty(section);
            if (property == null) return String.Empty;
            return KeyframeSerializer.Serialize(property.GetValue(settings), false);
        }

        private static KeyframeSettings Clone(KeyframeSettings settings)
        {
            return KeyframeSerializer.Deserialize<KeyframeSettings>(KeyframeSerializer.Serialize(settings, false)) ?? new KeyframeSettings();
        }

        #endregion
    }
}
