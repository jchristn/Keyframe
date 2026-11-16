namespace Keyframe.Core.Exceptions
{
    /// <summary>
    /// Thrown when the job queue is at capacity. Maps to HTTP 429.
    /// </summary>
    public class QueueFullException : KeyframeException
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="message">Message.</param>
        public QueueFullException(string message) : base(message)
        {
        }
    }
}
