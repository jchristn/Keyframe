namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;
    using Keyframe.Sdk;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Common analysis response fields.
    /// </summary>
    public class AnalysisResponse
    {
        /// <summary>
        /// Analysis identifier (prefix "kfa_").
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Identifier of the HTTP request that submitted the analysis (the x-request-id of the submitting request), when known.
        /// </summary>
        public string? RequestId { get; set; } = null;

        /// <summary>
        /// Whether every enabled function produced a result. Partial field failures keep this true.
        /// </summary>
        public bool Success { get; set; } = false;

        /// <summary>
        /// Media kind analyzed.
        /// </summary>
        public MediaKindEnum MediaKind { get; set; } = MediaKindEnum.Unknown;

        /// <summary>
        /// Deterministic media properties, when requested.
        /// </summary>
        public MediaProperties? Properties { get; set; } = null;

        /// <summary>
        /// Results keyed by the caller's summary and field keys. Values are schemaless by design. Never null.
        /// </summary>
        public Dictionary<string, object?> Results { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// Per-field detail. Never null.
        /// </summary>
        public List<FieldResult> Fields { get; set; } = new List<FieldResult>();

        /// <summary>
        /// Telemetry.
        /// </summary>
        public AnalysisTelemetry Telemetry { get; set; } = new AnalysisTelemetry();

        /// <summary>
        /// Non-fatal warnings. Never null.
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Errors that prevented a function from completing. Never null.
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();

        /// <summary>
        /// Read one Results value as a type, for example bool, long, double, string, or List&lt;string&gt;.
        /// </summary>
        /// <typeparam name="T">Target type.</typeparam>
        /// <param name="key">Result key (field key or summary key).</param>
        /// <returns>Converted value, or default when the key is missing or null.</returns>
        /// <exception cref="System.ArgumentNullException">Thrown when key is null.</exception>
        /// <exception cref="System.Text.Json.JsonException">Thrown when the value cannot be read as T.</exception>
        public T? GetResult<T>(string key)
        {
            return ResultValues.Get<T>(Results, key);
        }
    }
}
