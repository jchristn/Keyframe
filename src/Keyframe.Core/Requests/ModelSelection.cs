namespace Keyframe.Core.Requests
{
    /// <summary>
    /// Selects a configured endpoint and optionally overrides its model.
    /// </summary>
    public class ModelSelection
    {
        /// <summary>
        /// Endpoint name from settings. Null uses the default endpoint for the call type.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// Model name override. Null uses the endpoint's configured model.
        /// </summary>
        public string? Name { get; set; } = null;
    }
}
