namespace Keyframe.Core.Exceptions
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Thrown when a request fails validation. Carries every validation error found.
    /// </summary>
    public class RequestValidationException : KeyframeException
    {
        #region Public-Members

        /// <summary>
        /// Validation errors. Never null.
        /// </summary>
        public List<string> Errors
        {
            get
            {
                return _Errors;
            }
        }

        #endregion

        #region Private-Members

        private readonly List<string> _Errors = new List<string>();

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="errors">Validation errors.</param>
        /// <exception cref="ArgumentNullException">Thrown when errors is null.</exception>
        public RequestValidationException(List<string> errors) : base(BuildMessage(errors))
        {
            ArgumentNullException.ThrowIfNull(errors);
            _Errors.AddRange(errors);
        }

        #endregion

        #region Private-Methods

        private static string BuildMessage(List<string> errors)
        {
            if (errors == null || errors.Count < 1) return "The request is invalid.";
            return "The request is invalid: " + String.Join(" ", errors);
        }

        #endregion
    }
}
