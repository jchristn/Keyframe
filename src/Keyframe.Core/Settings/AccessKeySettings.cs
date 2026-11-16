namespace Keyframe.Core.Settings
{
    using System;

    /// <summary>
    /// A named access key that grants data-plane access.
    /// </summary>
    public class AccessKeySettings
    {
        #region Public-Members

        /// <summary>
        /// Display name for the key. Recorded as the principal name in request history and jobs. Default "default".
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
                _Name = value;
            }
        }

        /// <summary>
        /// Key value presented by callers as a bearer token or in the x-api-key header. Default "default".
        /// </summary>
        public string Key
        {
            get
            {
                return _Key;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(Key));
                _Key = value;
            }
        }

        /// <summary>
        /// Whether the key is accepted. Default true.
        /// </summary>
        public bool Active { get; set; } = true;

        #endregion

        #region Private-Members

        private string _Name = "default";
        private string _Key = "default";

        #endregion
    }
}
