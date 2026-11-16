namespace Keyframe.Sdk.Settings
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Model endpoint configuration and inference behavior. Changes apply live.
    /// </summary>
    public class InferenceSettings
    {
        #region Public-Members

        /// <summary>
        /// Name of the endpoint used for vision calls when a request does not name one. Default "mock".
        /// </summary>
        public string? DefaultVision { get; set; } = "mock";

        /// <summary>
        /// Name of the endpoint used for text-only calls when a request does not name one. Null falls back to the vision endpoint.
        /// </summary>
        public string? DefaultText { get; set; } = null;

        /// <summary>
        /// Name of the endpoint used for transcription when a request does not name one. Default "mock-stt".
        /// </summary>
        public string? DefaultTranscription { get; set; } = "mock-stt";

        /// <summary>
        /// Retries for transient failures (429, 5xx, timeouts). Default 2. Minimum 0, maximum 10.
        /// </summary>
        public int MaxRetries
        {
            get
            {
                return _MaxRetries;
            }
            set
            {
                _MaxRetries = Math.Clamp(value, 0, 10);
            }
        }

        /// <summary>
        /// Repair attempts when model output cannot be parsed or coerced. Default 1. Minimum 0, maximum 5.
        /// </summary>
        public int MaxRepairAttempts
        {
            get
            {
                return _MaxRepairAttempts;
            }
            set
            {
                _MaxRepairAttempts = Math.Clamp(value, 0, 5);
            }
        }

        /// <summary>
        /// Base backoff in milliseconds between retries, doubled each attempt. Default 500. Minimum 0, maximum 60000.
        /// </summary>
        public int RetryBackoffMs
        {
            get
            {
                return _RetryBackoffMs;
            }
            set
            {
                _RetryBackoffMs = Math.Clamp(value, 0, 60000);
            }
        }

        /// <summary>
        /// Overall timeout for a synchronous image analysis in milliseconds. Default 300000. Minimum 1000, maximum 3600000.
        /// </summary>
        public int RequestTimeoutMs
        {
            get
            {
                return _RequestTimeoutMs;
            }
            set
            {
                _RequestTimeoutMs = Math.Clamp(value, 1000, 3600000);
            }
        }

        /// <summary>
        /// Maximum transcript characters included in a text prompt. Longer transcripts keep the head and tail.
        /// Default 60000. Minimum 1000, maximum 2000000.
        /// </summary>
        public int MaxTranscriptCharacters
        {
            get
            {
                return _MaxTranscriptCharacters;
            }
            set
            {
                _MaxTranscriptCharacters = Math.Clamp(value, 1000, 2000000);
            }
        }

        /// <summary>
        /// Configured endpoints. Never null.
        /// </summary>
        public List<EndpointSettings> Endpoints
        {
            get
            {
                return _Endpoints;
            }
            set
            {
                _Endpoints = value ?? new List<EndpointSettings>();
            }
        }

        #endregion

        #region Private-Members

        private int _MaxRetries = 2;
        private int _MaxRepairAttempts = 1;
        private int _RetryBackoffMs = 500;
        private int _RequestTimeoutMs = 300000;
        private int _MaxTranscriptCharacters = 60000;
        private List<EndpointSettings> _Endpoints = new List<EndpointSettings>
        {
            new EndpointSettings { Name = "mock", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Completion, Model = "mock-vision", MaxConcurrentRequests = 4 },
            new EndpointSettings { Name = "mock-stt", Provider = ProviderTypeEnum.Mock, Kind = EndpointKindEnum.Transcription, Model = "mock-whisper", MaxConcurrentRequests = 4 }
        };

        #endregion

        #region Public-Methods

        /// <summary>
        /// Find an active endpoint by name (case-insensitive).
        /// </summary>
        /// <param name="name">Endpoint name.</param>
        /// <returns>Endpoint, or null when not found or inactive.</returns>
        public EndpointSettings? FindActive(string? name)
        {
            if (String.IsNullOrWhiteSpace(name)) return null;
            return _Endpoints.FirstOrDefault(e => e.Active && String.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        #endregion
    }
}
