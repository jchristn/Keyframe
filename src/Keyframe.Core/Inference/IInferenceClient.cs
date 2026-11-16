namespace Keyframe.Core.Inference
{
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;

    /// <summary>
    /// A completion endpoint that accepts text and images. Implementations report failures in the response rather
    /// than throwing, except for cancellation.
    /// </summary>
    public interface IInferenceClient
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
        /// Run a completion.
        /// </summary>
        /// <param name="request">Request.</param>
        /// <param name="token">Cancellation token.</param>
        /// <returns>Response.</returns>
        Task<InferenceResponse> CompleteAsync(InferenceRequest request, CancellationToken token = default);
    }
}
