namespace Keyframe.Core.Inference
{
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;

    /// <summary>
    /// A speech-to-text endpoint. Implementations report failures in the response rather than throwing, except for cancellation.
    /// </summary>
    public interface ITranscriptionClient
    {
        /// <summary>
        /// Endpoint name from settings.
        /// </summary>
        string EndpointName { get; }

        /// <summary>
        /// Provider.
        /// </summary>
        ProviderTypeEnum Provider { get; }

        /// <summary>
        /// Default model.
        /// </summary>
        string? Model { get; }

        /// <summary>
        /// Transcribe audio.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, CancellationToken token = default);
    }
}
