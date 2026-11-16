namespace Keyframe.Core.Exceptions
{
    using System;

    /// <summary>
    /// Base exception for Keyframe failures.
    /// </summary>
    public class KeyframeException : Exception
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        public KeyframeException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        /// <param name="innerException">Exception that caused this one.</param>
        public KeyframeException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
