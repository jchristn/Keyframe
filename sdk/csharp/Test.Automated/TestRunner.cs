namespace Test.Automated
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading.Tasks;

    /// <summary>
    /// Minimal sequential test runner that prints one line per test and a summary.
    /// </summary>
    public class TestRunner
    {
        /// <summary>
        /// Passed test count.
        /// </summary>
        public int Passed { get; private set; } = 0;

        /// <summary>
        /// Failed test count.
        /// </summary>
        public int Failed { get; private set; } = 0;

        /// <summary>
        /// Skipped test count.
        /// </summary>
        public int Skipped { get; private set; } = 0;

        /// <summary>
        /// Names and messages of failed tests.
        /// </summary>
        public List<string> Failures { get; } = new List<string>();

        /// <summary>
        /// Run one test.
        /// </summary>
        /// <param name="name">Test name.</param>
        /// <param name="test">Test body.</param>
        /// <returns>Task.</returns>
        public async Task RunAsync(string name, Func<Task> test)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                await test().ConfigureAwait(false);
                Passed++;
                Console.WriteLine("  PASS  " + name + " (" + sw.ElapsedMilliseconds + "ms)");
            }
            catch (HarnessSkipException e)
            {
                Skipped++;
                Console.WriteLine("  SKIP  " + name + ": " + e.Message);
            }
            catch (Exception e)
            {
                Failed++;
                string message = e.GetType().Name + ": " + e.Message;
                Failures.Add(name + ": " + message);
                Console.WriteLine("  FAIL  " + name + ": " + message);
            }
        }
    }
}
