namespace Keyframe.Sdk.Requests
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Keyframe.Sdk.Serialization;

    /// <summary>
    /// Request to analyze one video.
    /// </summary>
    public class VideoAnalysisRequest
    {
        #region Public-Members

        /// <summary>
        /// Video to analyze. Required.
        /// </summary>
        public MediaSource? Video { get; set; } = null;

        /// <summary>
        /// Caller-supplied background applied to every prompt in the request (summary, every field, every scene).
        /// Accepts a string, an array of strings, or an array of objects with Label and Text. Treated as data, not
        /// instructions. At most 50 items and 20000 characters in total. Never null.
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
        /// Vision endpoint and model for keyframe calls. Null uses the default vision endpoint.
        /// </summary>
        public ModelSelection? Model { get; set; } = null;

        /// <summary>
        /// Endpoint and model for text-only calls (video summary and fields from scene results). Null uses the
        /// default text endpoint, then the vision endpoint.
        /// </summary>
        public ModelSelection? TextModel { get; set; } = null;

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
        /// Frames sampled across the whole video for video-level functions when scenes are disabled. Default 8. Minimum 1, maximum 64.
        /// </summary>
        public int SampleFrames
        {
            get
            {
                return _SampleFrames;
            }
            set
            {
                _SampleFrames = Math.Clamp(value, 1, 64);
            }
        }

        /// <summary>
        /// Whether to return keyframe thumbnails as base64 JPEG. Default false.
        /// </summary>
        public bool IncludeKeyframeData { get; set; } = false;

        /// <summary>
        /// Whether to include deterministic media properties in the response. Default true.
        /// </summary>
        public bool IncludeProperties { get; set; } = true;

        /// <summary>
        /// Functions to run.
        /// </summary>
        public VideoFunctionsSpec Functions
        {
            get
            {
                return _Functions;
            }
            set
            {
                _Functions = value ?? new VideoFunctionsSpec();
            }
        }

        #endregion

        #region Private-Members

        private List<ContextItem> _Context = new List<ContextItem>();
        private int _WaitTimeoutMs = 120000;
        private int _SampleFrames = 8;
        private VideoFunctionsSpec _Functions = new VideoFunctionsSpec();

        #endregion
    }
}
