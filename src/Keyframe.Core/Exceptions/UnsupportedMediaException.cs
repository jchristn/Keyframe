namespace Keyframe.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when input media is not a supported type or does not match the requested media kind.
    /// </summary>
    public class UnsupportedMediaException : KeyframeException
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        public UnsupportedMediaException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        /// <param name="innerException">Exception that caused this one.</param>
        public UnsupportedMediaException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
