namespace Test.Automated
{
    using System;

    /// <summary>
    /// Thrown by a test to mark itself skipped with a reason.
    /// </summary>
    public class HarnessSkipException : Exception
    {
        /// <summary>
        /// Create the exception.
        /// </summary>
        /// <param name="reason">Why the test was skipped.</param>
        public HarnessSkipException(string reason) : base(reason)
        {
        }
    }
}
