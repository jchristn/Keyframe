namespace Keyframe.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when input media cannot be downloaded, decoded, or read.
    /// </summary>
    public class MediaResolutionException : KeyframeException
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        public MediaResolutionException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        /// <param name="innerException">Exception that caused this one.</param>
        public MediaResolutionException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
