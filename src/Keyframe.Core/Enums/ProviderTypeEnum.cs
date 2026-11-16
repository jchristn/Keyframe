namespace Keyframe.Core.Enums
{
    /// <summary>
    /// Model provider behind an inference endpoint.
    /// </summary>
    public enum ProviderTypeEnum
    {
        /// <summary>
        /// Deterministic offline provider used for tests and demos.
        /// </summary>
        Mock,

        /// <summary>
        /// Ollama server.
        /// </summary>
        Ollama,

        /// <summary>
        /// Ollama Cloud.
        /// </summary>
        OllamaCloud,

        /// <summary>
        /// OpenAI.
        /// </summary>
        OpenAi,

        /// <summary>
        /// Any OpenAI-compatible server such as vLLM, LM Studio, or a Whisper server.
        /// </summary>
        OpenAiCompatible,

        /// <summary>
        /// Azure OpenAI.
        /// </summary>
        AzureOpenAi,

        /// <summary>
        /// Anthropic.
        /// </summary>
        Anthropic,

        /// <summary>
        /// Google Gemini API.
        /// </summary>
        Gemini,

        /// <summary>
        /// Google Vertex AI.
        /// </summary>
        VertexAi,

        /// <summary>
        /// Amazon Bedrock.
        /// </summary>
        Bedrock,

        /// <summary>
        /// xAI.
        /// </summary>
        XAi,

        /// <summary>
        /// Cohere.
        /// </summary>
        Cohere
    }
}
