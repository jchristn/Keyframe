namespace Keyframe.Sdk.Settings
{
    using System;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// A configured model endpoint.
    /// </summary>
    public class EndpointSettings
    {
        #region Public-Members

        /// <summary>
        /// Unique endpoint name used in requests and settings references. 1 to 64 characters.
        /// </summary>
        public string Name
        {
            get
            {
                return _Name;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Name));
                if (value.Length > 64) throw new ArgumentOutOfRangeException(nameof(Name), "Endpoint name must be 64 characters or fewer.");
                _Name = value;
            }
        }

        /// <summary>
        /// Provider. Default Mock.
        /// </summary>
        public ProviderTypeEnum Provider { get; set; } = ProviderTypeEnum.Mock;

        /// <summary>
        /// What the endpoint is used for. Default Completion.
        /// </summary>
        public EndpointKindEnum Kind { get; set; } = EndpointKindEnum.Completion;

        /// <summary>
        /// Base URL of the provider API. Null uses the provider default where one exists.
        /// </summary>
        public string? Endpoint { get; set; } = null;

        /// <summary>
        /// API key or credential for the provider. Never returned by the settings API.
        /// </summary>
        public string? ApiKey { get; set; } = null;

        /// <summary>
        /// Default model name for this endpoint. Requests may override it.
        /// </summary>
        public string? Model { get; set; } = null;

        /// <summary>
        /// Maximum concurrent requests to this endpoint. Default 2. Minimum 1, maximum 256.
        /// </summary>
        public int MaxConcurrentRequests
        {
            get
            {
                return _MaxConcurrentRequests;
            }
            set
            {
                _MaxConcurrentRequests = Math.Clamp(value, 1, 256);
            }
        }

        /// <summary>
        /// Per-call timeout in milliseconds. Default 120000. Minimum 1000, maximum 3600000.
        /// </summary>
        public int TimeoutMs
        {
            get
            {
                return _TimeoutMs;
            }
            set
            {
                _TimeoutMs = Math.Clamp(value, 1000, 3600000);
            }
        }

        /// <summary>
        /// Sampling temperature sent to the model when set. Null leaves the provider default. Minimum 0, maximum 2.
        /// </summary>
        public double? Temperature
        {
            get
            {
                return _Temperature;
            }
            set
            {
                _Temperature = value.HasValue ? Math.Clamp(value.Value, 0.0, 2.0) : null;
            }
        }

        /// <summary>
        /// Maximum output tokens per call. Default 2048. Minimum 16, maximum 1000000.
        /// </summary>
        public int MaxOutputTokens
        {
            get
            {
                return _MaxOutputTokens;
            }
            set
            {
                _MaxOutputTokens = Math.Clamp(value, 16, 1000000);
            }
        }

        /// <summary>
        /// Whether the endpoint can be used. Default true.
        /// </summary>
        public bool Active { get; set; } = true;

        #endregion

        #region Private-Members

        private string _Name = "mock";
        private int _MaxConcurrentRequests = 2;
        private int _TimeoutMs = 120000;
        private double? _Temperature = null;
        private int _MaxOutputTokens = 2048;

        #endregion
    }
}
