namespace Test.Shared
{
    using System;
    using System.Collections.Concurrent;
    using Keyframe.Core.Analysis;

    /// <summary>
    /// Health lookup with values set by the test. Endpoints not set report null (not checked).
    /// </summary>
    public sealed class FakeHealthLookup : IEndpointHealthLookup
    {
        private readonly ConcurrentDictionary<string, bool> _States = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Set an endpoint's health.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <param name="healthy">Health.</param>
        public void Set(string endpointName, bool healthy)
        {
            _States[endpointName] = healthy;
        }

        /// <summary>
        /// Last known health.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <returns>The value set, or null.</returns>
        public bool? GetHealth(string endpointName)
        {
            bool healthy;
            return _States.TryGetValue(endpointName, out healthy) ? healthy : null;
        }

        /// <summary>
        /// Record an unreachable endpoint as unhealthy.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <param name="message">Error message.</param>
        public void ReportUnreachable(string endpointName, string? message)
        {
            _States[endpointName] = false;
        }
    }
}
