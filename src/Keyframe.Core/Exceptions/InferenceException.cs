namespace Keyframe.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when a model endpoint call fails after retries.
    /// </summary>
    public class InferenceException : KeyframeException
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        public InferenceException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        /// <param name="innerException">Exception that caused this one.</param>
        public InferenceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
