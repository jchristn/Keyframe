namespace Keyframe.Core.Requests
{
    using System;
    using System.Text.Json.Serialization;

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

        /// <summary>
        /// Alias of <see cref="Name"/> accepted on input, so { "Endpoint": ..., "Model": ... } also works. Always null
        /// on output; read <see cref="Name"/>.
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Model
        {
            get
            {
                return null;
            }
            set
            {
                if (!String.IsNullOrWhiteSpace(value)) Name = value;
            }
        }
    }
}
