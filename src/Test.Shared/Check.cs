namespace Test.Shared
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// Minimal assertion helpers. Failures throw InvalidOperationException with context.
    /// </summary>
    public static class Check
    {
        /// <summary>
        /// Require a condition.
        /// </summary>
        /// <param name="condition">Condition.</param>
        /// <param name="message">Failure message.</param>
        /// <exception cref="InvalidOperationException">Thrown when the condition is false.</exception>
        public static void True(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        /// <summary>
        /// Require equality.
        /// </summary>
        /// <typeparam name="T">Type.</typeparam>
        /// <param name="expected">Expected value.</param>
        /// <param name="actual">Actual value.</param>
        /// <param name="what">Description of the value.</param>
        /// <exception cref="InvalidOperationException">Thrown when values differ.</exception>
        public static void Equal<T>(T expected, T actual, string what)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException(what + ": expected '" + expected + "' but was '" + actual + "'.");
        }

        /// <summary>
        /// Require that an action throws a specific exception type.
        /// </summary>
        /// <typeparam name="TException">Expected exception type.</typeparam>
        /// <param name="action">Action.</param>
        /// <param name="what">Description.</param>
        /// <exception cref="InvalidOperationException">Thrown when no exception or a different exception is thrown.</exception>
        public static void Throws<TException>(Action action, string what) where TException : Exception
        {
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + " but got " + ex.GetType().Name + ": " + ex.Message);
            }

            throw new InvalidOperationException(what + ": expected " + typeof(TException).Name + " but nothing was thrown.");
        }
    }
}
