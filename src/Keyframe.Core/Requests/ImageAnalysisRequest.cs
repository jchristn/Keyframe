namespace Keyframe.Core.Requests
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;
    using Keyframe.Core.Serialization;

    /// <summary>
    /// Request to analyze one image.
    /// </summary>
    public class ImageAnalysisRequest
    {
        #region Public-Members

        /// <summary>
        /// Image to analyze. Required.
        /// </summary>
        public MediaSource? Image { get; set; } = null;

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
        /// Vision endpoint and model selection. Null uses the default vision endpoint.
        /// </summary>
        public ModelSelection? Model { get; set; } = null;

        /// <summary>
        /// Functions to run.
        /// </summary>
        public FunctionsSpec Functions
        {
            get
            {
                return _Functions;
            }
            set
            {
                _Functions = value ?? new FunctionsSpec();
            }
        }

        /// <summary>
        /// Whether to include deterministic media properties in the response. Default true.
        /// </summary>
        public bool IncludeProperties { get; set; } = true;

        #endregion

        #region Private-Members

        private List<ContextItem> _Context = new List<ContextItem>();
        private FunctionsSpec _Functions = new FunctionsSpec();

        #endregion
    }
}
