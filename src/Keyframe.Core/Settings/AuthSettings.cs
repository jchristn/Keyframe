namespace Keyframe.Core.Settings
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Authentication settings. Keyframe uses named access keys plus one admin API key rather than tenants and users.
    /// Changes apply live.
    /// </summary>
    public class AuthSettings
    {
        #region Public-Members

        /// <summary>
        /// Whether data-plane and admin routes require a key. Default true. Set false only for local development.
        /// </summary>
        public bool RequireAuthentication { get; set; } = true;

        /// <summary>
        /// Admin API key. Grants data-plane access plus settings, restart, endpoint tests, and request history deletion.
        /// Default "keyframeadmin". Change before any shared deployment.
        /// </summary>
        public string AdminApiKey
        {
            get
            {
                return _AdminApiKey;
            }
            set
            {
                if (String.IsNullOrWhiteSpace(value)) throw new ArgumentNullException(nameof(AdminApiKey));
                _AdminApiKey = value;
            }
        }

        /// <summary>
        /// Data-plane access keys. Never null.
        /// </summary>
        public List<AccessKeySettings> AccessKeys
        {
            get
            {
                return _AccessKeys;
            }
            set
            {
                _AccessKeys = value ?? new List<AccessKeySettings>();
            }
        }

        /// <summary>
        /// Maximum failed authentication attempts per source IP per minute before returning 429. Default 30.
        /// Minimum 1, maximum 100000.
        /// </summary>
        public int MaxFailedAttemptsPerMinute
        {
            get
            {
                return _MaxFailedAttemptsPerMinute;
            }
            set
            {
                _MaxFailedAttemptsPerMinute = Math.Clamp(value, 1, 100000);
            }
        }

        #endregion

        #region Private-Members

        private string _AdminApiKey = "keyframeadmin";
        private List<AccessKeySettings> _AccessKeys = new List<AccessKeySettings> { new AccessKeySettings() };
        private int _MaxFailedAttemptsPerMinute = 30;

        #endregion
    }
}
