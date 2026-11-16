namespace Keyframe.Sdk
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using Keyframe.Sdk.Serialization;

    /// <summary>
    /// Typed access to the schemaless Results dictionary and FieldResult.Value.
    /// Values arrive as raw JSON; these helpers re-read them as the requested type.
    /// Thread safety: stateless and safe for concurrent use.
    /// </summary>
    public static class ResultValues
    {
        #region Public-Methods

        /// <summary>
        /// Convert a Results value to a type, for example bool, long, double, string, List&lt;string&gt;, or a caller class.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="value">Value from Results or FieldResult.Value. Null returns default.</param>
        /// <returns>Converted value, or default when value is null.</returns>
        /// <exception cref="JsonException">Thrown when the value cannot be read as T.</exception>
        public static T? Convert<T>(object? value)
        {
            if (value == null) return default;
            if (value is T typed) return typed;
            string json = JsonSerializer.Serialize(value, value.GetType(), KeyframeSerializer.Options);
            return JsonSerializer.Deserialize<T>(json, KeyframeSerializer.Options);
        }

        /// <summary>
        /// Read one key from a Results dictionary as a type.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="results">Results dictionary.</param>
        /// <param name="key">Result key (field key or summary key).</param>
        /// <returns>Converted value, or default when the key is missing or null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when results or key is null.</exception>
        /// <exception cref="JsonException">Thrown when the value cannot be read as T.</exception>
        public static T? Get<T>(IDictionary<string, object?> results, string key)
        {
            ArgumentNullException.ThrowIfNull(results);
            ArgumentNullException.ThrowIfNull(key);
            if (!results.TryGetValue(key, out object? value)) return default;
            return Convert<T>(value);
        }

        #endregion
    }
}
