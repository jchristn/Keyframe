namespace Test.Automated
{
    using System;

    /// <summary>
    /// Thrown when a harness check fails.
    /// </summary>
    public class HarnessAssertionException : Exception
    {
        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="message">Failure description.</param>
        public HarnessAssertionException(string message) : base(message)
        {
        }
    }
}
