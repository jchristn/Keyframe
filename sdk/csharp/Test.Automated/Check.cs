namespace Test.Automated
{
    using System;
    using System.Threading.Tasks;
    using Keyframe.Sdk;

    /// <summary>
    /// Assertion helpers for the harness.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Fail unless the condition is true.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Failure description.</param>
        /// <exception cref="HarnessAssertionException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new HarnessAssertionException(message);
        }

        /// <summary>
        /// Fail unless the values are equal.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="what">What is being compared.</param>
        /// <exception cref="HarnessAssertionException">Thrown when the values differ.</exception>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual)) throw new HarnessAssertionException(what + ": expected '" + expected + "', got '" + actual + "'.");
        }

        /// <summary>
        /// Run an action and require a KeyframeApiException with the given status code.
        /// </summary>
        /// <param name="statusCode">Expected HTTP status code.</param>
        /// <param name="action">Action.</param>
        /// <returns>The caught exception.</returns>
        /// <exception cref="HarnessAssertionException">Thrown when no exception or a different status is raised.</exception>
        public static async Task<KeyframeApiException> ThrowsApiAsync(int statusCode, Func<Task> action)
        {
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (KeyframeApiException e)
            {
                if (e.StatusCode != statusCode) throw new HarnessAssertionException("Expected HTTP " + statusCode + ", got " + e.StatusCode + ": " + e.Message);
                return e;
            }

            throw new HarnessAssertionException("Expected KeyframeApiException with HTTP " + statusCode + ", but the call succeeded.");
        }
    }
}
