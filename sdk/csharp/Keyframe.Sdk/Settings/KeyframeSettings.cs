namespace Keyframe.Sdk.Settings
{
    using System;

    /// <summary>
    /// Root Keyframe settings, persisted as keyframe.json.
    /// </summary>
    public class KeyframeSettings
    {
        #region Public-Members

        /// <summary>
        /// UTC time the settings file was first created.
        /// </summary>
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// REST listener settings.
        /// </summary>
        public RestSettings Rest
        {
            get
            {
                return _Rest;
            }
            set
            {
                _Rest = value ?? new RestSettings();
            }
        }

        /// <summary>
        /// Authentication settings.
        /// </summary>
        public AuthSettings Auth
        {
            get
            {
                return _Auth;
            }
            set
            {
                _Auth = value ?? new AuthSettings();
            }
        }

        /// <summary>
        /// Database settings.
        /// </summary>
        public DatabaseSettings Database
        {
            get
            {
                return _Database;
            }
            set
            {
                _Database = value ?? new DatabaseSettings();
            }
        }

        /// <summary>
        /// Inference settings.
        /// </summary>
        public InferenceSettings Inference
        {
            get
            {
                return _Inference;
            }
            set
            {
                _Inference = value ?? new InferenceSettings();
            }
        }

        /// <summary>
        /// Media settings.
        /// </summary>
        public MediaSettings Media
        {
            get
            {
                return _Media;
            }
            set
            {
                _Media = value ?? new MediaSettings();
            }
        }

        /// <summary>
        /// Job settings.
        /// </summary>
        public JobSettings Jobs
        {
            get
            {
                return _Jobs;
            }
            set
            {
                _Jobs = value ?? new JobSettings();
            }
        }

        /// <summary>
        /// Request history settings.
        /// </summary>
        public RequestHistorySettings RequestHistory
        {
            get
            {
                return _RequestHistory;
            }
            set
            {
                _RequestHistory = value ?? new RequestHistorySettings();
            }
        }

        /// <summary>
        /// Logging settings.
        /// </summary>
        public LoggingSettings Logging
        {
            get
            {
                return _Logging;
            }
            set
            {
                _Logging = value ?? new LoggingSettings();
            }
        }

        /// <summary>
        /// Telemetry settings.
        /// </summary>
        public TelemetrySettings Telemetry
        {
            get
            {
                return _Telemetry;
            }
            set
            {
                _Telemetry = value ?? new TelemetrySettings();
            }
        }

        #endregion

        #region Private-Members

        private RestSettings _Rest = new RestSettings();
        private AuthSettings _Auth = new AuthSettings();
        private DatabaseSettings _Database = new DatabaseSettings();
        private InferenceSettings _Inference = new InferenceSettings();
        private MediaSettings _Media = new MediaSettings();
        private JobSettings _Jobs = new JobSettings();
        private RequestHistorySettings _RequestHistory = new RequestHistorySettings();
        private LoggingSettings _Logging = new LoggingSettings();
        private TelemetrySettings _Telemetry = new TelemetrySettings();

        #endregion
    }
}
