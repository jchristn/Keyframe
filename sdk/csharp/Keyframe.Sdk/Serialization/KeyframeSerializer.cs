namespace Keyframe.Sdk.Serialization
{
    using System;
    using System.Text.Json;
    using System.Text.Json.Serialization;

    /// <summary>
    /// Central JSON serialization rules. Property names are PascalCase on output, deserialization is
    /// case-insensitive, and enums are written as strings.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public static class KeyframeSerializer
    {
        #region Public-Members

        /// <summary>
        /// Options used for all Keyframe contracts. Do not mutate.
        /// </summary>
        public static JsonSerializerOptions Options
        {
            get
            {
                return _Options;
            }
        }

        /// <summary>
        /// Options used for indented output. Do not mutate.
        /// </summary>
        public static JsonSerializerOptions PrettyOptions
        {
            get
            {
                return _PrettyOptions;
            }
        }

        #endregion

        #region Private-Members

        private static readonly JsonSerializerOptions _Options = Build(false);
        private static readonly JsonSerializerOptions _PrettyOptions = Build(true);

        #endregion

        #region Public-Methods

        /// <summary>
        /// Serialize an object to JSON.
        /// </summary>
        /// <param name="obj">Object to serialize. Null serializes as "null".</param>
        /// <param name="pretty">True for indented output.</param>
        /// <returns>JSON text.</returns>
        public static string Serialize(object? obj, bool pretty = false)
        {
            if (obj == null) return "null";
            return JsonSerializer.Serialize(obj, obj.GetType(), pretty ? _PrettyOptions : _Options);
        }

        /// <summary>
        /// Deserialize JSON into a typed object.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="json">JSON text.</param>
        /// <returns>Deserialized object, or null when the JSON is the literal null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when json is null or empty.</exception>
        /// <exception cref="JsonException">Thrown when the JSON is malformed or does not match the type.</exception>
        public static T? Deserialize<T>(string json)
        {
            if (String.IsNullOrEmpty(json)) throw new ArgumentNullException(nameof(json));
            return JsonSerializer.Deserialize<T>(json, _Options);
        }

        /// <summary>
        /// Deep copy an object by round-tripping it through JSON.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="obj">Object to copy.</param>
        /// <returns>Copy.</returns>
        /// <exception cref="ArgumentNullException">Thrown when obj is null.</exception>
        public static T Copy<T>(T obj) where T : class
        {
            ArgumentNullException.ThrowIfNull(obj);
            string json = JsonSerializer.Serialize(obj, typeof(T), _Options);
            T? copy = JsonSerializer.Deserialize<T>(json, _Options);
            if (copy == null) throw new InvalidOperationException("Unable to copy object of type " + typeof(T).Name + ".");
            return copy;
        }

        #endregion

        #region Private-Methods

        private static JsonSerializerOptions Build(bool pretty)
        {
            JsonSerializerOptions options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = null,
                WriteIndented = pretty,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                NumberHandling = JsonNumberHandling.AllowReadingFromString
            };

            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        #endregion
    }
}
