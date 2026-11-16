namespace Keyframe.Core.Responses
{
    using Keyframe.Core.Enums;

    /// <summary>
    /// Model call attempt statistics for one endpoint, provider, and model.
    /// </summary>
    public class ModelStats
    {
        /// <summary>
        /// Endpoint name.
        /// </summary>
        public string Endpoint { get; set; } = string.Empty;

        /// <summary>
        /// Provider.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// Model.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Attempts.
        /// </summary>
        public long Calls { get; set; } = 0;

        /// <summary>
        /// Attempts that failed.
        /// </summary>
        public long Failed { get; set; } = 0;

        /// <summary>
        /// Attempt duration in milliseconds.
        /// </summary>
        public Distribution? DurationMs { get; set; } = null;

        /// <summary>
        /// Time to first token in milliseconds.
        /// </summary>
        public Distribution? TimeToFirstTokenMs { get; set; } = null;

        /// <summary>
        /// Generation time in milliseconds.
        /// </summary>
        public Distribution? GenerationMs { get; set; } = null;

        /// <summary>
        /// Tokens per second.
        /// </summary>
        public Distribution? TokensPerSecond { get; set; } = null;

        /// <summary>
        /// Prompt tokens.
        /// </summary>
        public long PromptTokens { get; set; } = 0;

        /// <summary>
        /// Completion tokens.
        /// </summary>
        public long CompletionTokens { get; set; } = 0;
    }
}
