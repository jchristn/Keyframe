namespace Keyframe.Sdk.Responses
{
    using System.Collections.Generic;
    using Keyframe.Sdk;

    /// <summary>
    /// Result for one audio section. Each section can be cut back out with POST /v1.0/segment.
    /// </summary>
    public class SectionResult
    {
        /// <summary>
        /// Zero-based section index.
        /// </summary>
        public int Index { get; set; } = 0;

        /// <summary>
        /// Start in milliseconds.
        /// </summary>
        public long StartMs { get; set; } = 0;

        /// <summary>
        /// End in milliseconds.
        /// </summary>
        public long EndMs { get; set; } = 0;

        /// <summary>
        /// Length in milliseconds.
        /// </summary>
        public long DurationMs { get; set; } = 0;

        /// <summary>
        /// Transcript text spoken in the section, when transcription ran.
        /// </summary>
        public string? TranscriptText { get; set; } = null;

        /// <summary>
        /// Per-section results keyed like the top-level Results.
        /// </summary>
        public Dictionary<string, object?> Results { get; set; } = new Dictionary<string, object?>();

        /// <summary>
        /// Per-section field details.
        /// </summary>
        public List<FieldResult> Fields { get; set; } = new List<FieldResult>();

        /// <summary>
        /// Per-section errors.
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
