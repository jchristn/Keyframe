namespace Test.Automated
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Sdk;

    /// <summary>
    /// Keyframe C# SDK test harness. Runs against a live server.
    /// Environment: KEYFRAME_URL (default http://127.0.0.1:8710), KEYFRAME_ADMIN_KEY (default keyframeadmin),
    /// KEYFRAME_ACCESS_KEY (default default). Requires ffmpeg on the PATH (or FFMPEG) for media fixtures.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point. Returns 0 when every test passed, 1 when any failed, 2 when the server is unreachable.
        /// </summary>
        /// <param name="args">Unused.</param>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            string url = Env("KEYFRAME_URL", "http://127.0.0.1:8710");
            string adminKey = Env("KEYFRAME_ADMIN_KEY", "keyframeadmin");
            string accessKey = Env("KEYFRAME_ACCESS_KEY", "default");

            using (CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(15)))
            {
                using (KeyframeClient admin = new KeyframeClient(url, adminKey))
                {
                    using (KeyframeClient user = new KeyframeClient(url, accessKey))
                    {
                        using (KeyframeClient anonymous = new KeyframeClient(url))
                        {
                            Console.WriteLine("Keyframe C# SDK harness against " + url + " (" + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ")");
                            if (!await anonymous.Health.IsAliveAsync(cts.Token).ConfigureAwait(false))
                            {
                                Console.WriteLine("Server at " + url + " is not reachable. Start it or set KEYFRAME_URL.");
                                return 2;
                            }

                            using (Fixtures fixtures = new Fixtures())
                            {
                                await fixtures.GenerateAsync(cts.Token).ConfigureAwait(false);
                                if (!fixtures.Available) Console.WriteLine("Media fixtures unavailable: " + fixtures.Reason);

                                TestRunner runner = new TestRunner();
                                SdkTests tests = new SdkTests(url, adminKey, accessKey, fixtures, admin, user, anonymous, cts.Token);
                                await tests.RunAllAsync(runner).ConfigureAwait(false);

                                Console.WriteLine();
                                Console.WriteLine("Passed " + runner.Passed + ", failed " + runner.Failed + ", skipped " + runner.Skipped);
                                foreach (string failure in runner.Failures) Console.WriteLine("  " + failure);
                                return runner.Failed == 0 ? 0 : 1;
                            }
                        }
                    }
                }
            }
        }

        private static string Env(string name, string fallback)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
