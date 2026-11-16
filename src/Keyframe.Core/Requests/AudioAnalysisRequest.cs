namespace Keyframe.Core.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Keyframe.Core.Serialization;

    /// <summary>
    /// Request to analyze one audio file, or the audio track of a video file.
    /// </summary>
    public class AudioAnalysisRequest
    {
        #region Public-Members

        /// <summary>
        /// Audio to analyze: mp3, wav, flac, m4a or aac, ogg or opus, webm audio, or a video file (its audio track is used). Required.
        /// </summary>
        public MediaSource? Audio { get; set; } = null;

        /// <summary>
        /// Caller-supplied background applied to every prompt in the request. Accepts a string, an array of strings,
        /// or an array of objects with Label and Text. Treated as data, not instructions. At most 50 items and 20000
        /// characters in total. Never null.
        /// </summary>
        [JsonConverter(typeof(ContextListConverter))]
        public List<ContextItem> Context
        {
            get
            {
                return _Context;
            }
            set
            {
                _Context = value ?? new List<ContextItem>();
            }
        }

        /// <summary>
        /// Whether to wait for the job and return the result in the response. Default false.
        /// </summary>
        public bool Wait { get; set; } = false;

        /// <summary>
        /// Wait timeout in milliseconds when Wait is true. Default 120000. Minimum 1000, maximum 3600000 (also capped by server settings).
        /// </summary>
        public int WaitTimeoutMs
        {
            get
            {
                return _WaitTimeoutMs;
            }
            set
            {
                _WaitTimeoutMs = Math.Clamp(value, 1000, 3600000);
            }
        }

        /// <summary>
        /// Transcription endpoint and model. Null uses Functions.Transcribe.Endpoint and Model, then the default
        /// transcription endpoint. When set, it takes precedence over Functions.Transcribe.Endpoint and Model.
        /// </summary>
        public ModelSelection? TranscriptionModel { get; set; } = null;

        /// <summary>
        /// Text endpoint and model for Summarize and Extract on the transcript. Null uses the default text endpoint,
        /// then the default vision endpoint.
        /// </summary>
        public ModelSelection? TextModel { get; set; } = null;

        /// <summary>
        /// Whether to include deterministic media properties in the response. Default true.
        /// </summary>
        public bool IncludeProperties { get; set; } = true;

        /// <summary>
        /// Functions to run. Defaults: Transcribe on, Summarize on, Extract off.
        /// </summary>
        public AudioFunctionsSpec Functions
        {
            get
            {
                return _Functions;
            }
            set
            {
                _Functions = value ?? new AudioFunctionsSpec();
            }
        }

        #endregion

        #region Private-Members

        private List<ContextItem> _Context = new List<ContextItem>();
        private int _WaitTimeoutMs = 120000;
        private AudioFunctionsSpec _Functions = new AudioFunctionsSpec();

        #endregion
    }
}
