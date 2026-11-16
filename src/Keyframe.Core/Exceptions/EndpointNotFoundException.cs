namespace Keyframe.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a request names an inference endpoint that is not configured or not active.
    /// </summary>
    public class EndpointNotFoundException : KeyframeException
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        public EndpointNotFoundException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        /// <param name="innerException">Exception that caused this one.</param>
        public EndpointNotFoundException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
