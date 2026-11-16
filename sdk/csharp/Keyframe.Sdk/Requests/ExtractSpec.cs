namespace Keyframe.Sdk.Requests
{
    using System.Collections.Generic;
    using Keyframe.Sdk.Enums;

    /// <summary>
    /// Extract function options.
    /// </summary>
    public class ExtractSpec
    {
        #region Public-Members

        /// <summary>
        /// Whether to extract fields. Default false.
        /// </summary>
        public bool Enable { get; set; } = false;

        /// <summary>
        /// How fields are sent to the model. Default Combined.
        /// </summary>
        public ExtractModeEnum Mode { get; set; } = ExtractModeEnum.Combined;

        /// <summary>
        /// Fields to extract. Never null.
        /// </summary>
        public List<FieldSpec> Fields
        {
            get
            {
                return _Fields;
            }
            set
            {
                _Fields = value ?? new List<FieldSpec>();
            }
        }

        #endregion

        #region Private-Members

        private List<FieldSpec> _Fields = new List<FieldSpec>();

        #endregion
    }
}
