namespace Keyframe.Core.Analysis
{
    using System;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Inference.Providers;
    using Keyframe.Core.Media;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Settings;

    /// <summary>
    /// Resolves listening for an analysis: the named or default endpoint, its audio capability, its health, and whether
    /// the rendition format suits it. Shared by submission (preflight) and the job, so both apply the same rules.
    /// </summary>
    public static class ListenPlanner
    {
        #region Public-Methods

        /// <summary>
        /// Resolve listening for audio analysis.
        /// </summary>
        /// <param name="listen">Listening options from the request.</param>
        /// <param name="anyModelFunction">Whether any model function (Summarize, Extract, or a section function) is enabled.</param>
        /// <param name="settings">Current settings.</param>
        /// <param name="health">Health lookup, or null to treat every endpoint as healthy.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentNullException">Thrown when listen or settings is null.</exception>
        public static ListenPlan Resolve(ListenSpec listen, bool anyModelFunction, KeyframeSettings settings, IEndpointHealthLookup? health)
        {
            ArgumentNullException.ThrowIfNull(listen);
            ArgumentNullException.ThrowIfNull(settings);

            ListenPlan plan = new ListenPlan { Rendition = RenditionResolver.Resolve(listen.Rendition, settings.Media) };
            if (!listen.Enable)
            {
                plan.Reason = ListenSkipReasonEnum.NotRequested;
                return plan;
            }

            if (!anyModelFunction)
            {
                plan.Reason = ListenSkipReasonEnum.NoModelFunctions;
                return plan;
            }

            string? named = listen.Model?.Endpoint;
            EndpointSettings? endpoint;
            if (!String.IsNullOrWhiteSpace(named))
            {
                endpoint = settings.Inference.FindActive(named);
                if (endpoint == null || endpoint.Kind != EndpointKindEnum.Completion)
                {
                    plan.Rejection = "Listening endpoint '" + named + "' is not an active completion endpoint.";
                    return plan;
                }

                if (!PolyPromptClientBuilder.SupportsAudioInput(endpoint))
                {
                    plan.Rejection = "Endpoint '" + endpoint.Name + "' cannot take audio input. Choose an endpoint whose provider supports audio " +
                        "(Gemini, OpenAiCompatible), or set Listen.Enable to false.";
                    return plan;
                }
            }
            else
            {
                endpoint = settings.Inference.FindActive(settings.Inference.DefaultListening);
                if (endpoint == null || endpoint.Kind != EndpointKindEnum.Completion)
                {
                    plan.Reason = ListenSkipReasonEnum.NoEndpointConfigured;
                    return plan;
                }

                if (!PolyPromptClientBuilder.SupportsAudioInput(endpoint))
                {
                    plan.Endpoint = endpoint;
                    plan.Reason = ListenSkipReasonEnum.EndpointLacksAudioInput;
                    return plan;
                }
            }

            string? rejection = FormatRejection(endpoint, plan.Rendition);
            if (rejection != null)
            {
                plan.Rejection = rejection;
                return plan;
            }

            plan.Endpoint = endpoint;
            plan.Model = String.IsNullOrWhiteSpace(listen.Model?.Name) ? null : listen.Model!.Name;
            if (health != null && health.GetHealth(endpoint.Name) == false) plan.Reason = ListenSkipReasonEnum.EndpointUnhealthy;
            return plan;
        }

        /// <summary>
        /// Resolve listening for video scenes, where the endpoint is the vision endpoint chosen for seeing. Nothing is
        /// ever rejected: an endpoint that cannot listen, an unhealthy one, a silent video, or a format the endpoint
        /// refuses all fall back to frames and transcript.
        /// </summary>
        /// <param name="enable">Whether scene audio is enabled.</param>
        /// <param name="rendition">Requested rendition, or null.</param>
        /// <param name="vision">The vision endpoint.</param>
        /// <param name="hasAudio">Whether the video has an audio track.</param>
        /// <param name="settings">Current settings.</param>
        /// <param name="health">Health lookup, or null to treat every endpoint as healthy.</param>
        /// <returns>The plan. <see cref="ListenPlan.Rejection"/> is always null.</returns>
        /// <exception cref="ArgumentNullException">Thrown when vision or settings is null.</exception>
        public static ListenPlan ResolveScenes(bool enable, AudioRenditionSpec? rendition, EndpointSettings vision, bool hasAudio, KeyframeSettings settings, IEndpointHealthLookup? health)
        {
            ArgumentNullException.ThrowIfNull(vision);
            ArgumentNullException.ThrowIfNull(settings);

            ListenPlan plan = new ListenPlan { Rendition = RenditionResolver.Resolve(rendition, settings.Media) };
            if (!enable)
            {
                plan.Reason = ListenSkipReasonEnum.NotRequested;
                return plan;
            }

            if (!hasAudio)
            {
                plan.Reason = ListenSkipReasonEnum.NoAudioTrack;
                return plan;
            }

            plan.Endpoint = vision;
            if (!PolyPromptClientBuilder.SupportsAudioInput(vision) || FormatRejection(vision, plan.Rendition) != null)
            {
                plan.Reason = ListenSkipReasonEnum.EndpointLacksAudioInput;
                return plan;
            }

            if (health != null && health.GetHealth(vision.Name) == false) plan.Reason = ListenSkipReasonEnum.EndpointUnhealthy;
            return plan;
        }

        /// <summary>
        /// The listen budget in milliseconds for an endpoint: its MaxListenSeconds when set, otherwise Media.MaxListenSeconds.
        /// </summary>
        /// <param name="endpoint">Endpoint.</param>
        /// <param name="media">Media settings.</param>
        /// <returns>Budget in milliseconds.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public static long BudgetMs(EndpointSettings endpoint, MediaSettings media)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            ArgumentNullException.ThrowIfNull(media);
            return (endpoint.MaxListenSeconds ?? media.MaxListenSeconds) * 1000L;
        }

        #endregion

        #region Private-Methods

        private static string? FormatRejection(EndpointSettings endpoint, AudioRenditionSpec rendition)
        {
            if (rendition.Format == ListenFormatEnum.Flac && endpoint.Provider == ProviderTypeEnum.OpenAiCompatible)
                return "Endpoint '" + endpoint.Name + "' (OpenAiCompatible) accepts only Mp3 or Wav audio. Set Listen.Rendition.Format to Mp3 or Wav.";
            return null;
        }

        #endregion
    }
}
