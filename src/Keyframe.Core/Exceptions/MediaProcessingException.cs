namespace Keyframe.Core.Exceptions
{
    using System;

    /// <summary>
    /// Thrown when an ffmpeg or ffprobe operation fails.
    /// </summary>
    public class MediaProcessingException : KeyframeException
    {
        /// <summary>
        /// Instantiate the exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        public MediaProcessingException(string message) : base(message)
        {
        }

        /// <summary>
        /// Instantiate the exception with an inner exception.
        /// </summary>
        /// <param name="message">Message describing the failure.</param>
        /// <param name="innerException">Exception that caused this one.</param>
        public MediaProcessingException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
