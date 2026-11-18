namespace Keyframe.Core.Analysis
{
    /// <summary>
    /// Read-only view of endpoint health, implemented by the server's health service so Core does not reference the
    /// server. Implementations return the last probe result and never probe inline.
    /// </summary>
    public interface IEndpointHealthLookup
    {
        /// <summary>
        /// Last known health of an endpoint.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <returns>True when the last probe succeeded, false when it failed, null when the endpoint has not been checked.</returns>
        bool? GetHealth(string endpointName);

        /// <summary>
        /// Report that a real call could not reach an endpoint (no HTTP response). The endpoint is marked unhealthy at
        /// once, so later requests fall back instead of failing until the next probe.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <param name="message">Error message.</param>
        void ReportUnreachable(string endpointName, string? message);
    }
}
