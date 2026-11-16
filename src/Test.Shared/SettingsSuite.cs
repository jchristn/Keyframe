namespace Test.Shared
{
    using System;
    using System.Threading.Tasks;
    using Keyframe.Core.Serialization;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Settings defaults, clamping, and serialization.
    /// </summary>
    public static class SettingsSuite
    {
        private const string Id = "Settings";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Id, "Settings", new[]
            {
                new TestCaseDescriptor(Id, "Defaults", "Defaults match the plan", ct =>
                {
                    KeyframeSettings s = new KeyframeSettings();
                    Check.Equal(8700, s.Rest.Port, "Rest.Port");
                    Check.Equal("127.0.0.1", s.Rest.Hostname, "Rest.Hostname");
                    Check.Equal(true, s.Auth.RequireAuthentication, "Auth.RequireAuthentication");
                    Check.Equal(2, s.Inference.Endpoints.Count, "default endpoint count");
                    Check.True(s.Inference.FindActive("mock") != null, "mock endpoint present");
                    Check.True(s.Inference.FindActive("MOCK-STT") != null, "endpoint lookup is case-insensitive");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "Clamping", "Numeric settings clamp to their ranges", ct =>
                {
                    MediaSettings m = new MediaSettings { ImageMaxEdge = 10, MaxParallelFfmpeg = 1000, JpegQuality = 0 };
                    Check.Equal(64, m.ImageMaxEdge, "ImageMaxEdge minimum");
                    Check.Equal(64, m.MaxParallelFfmpeg, "MaxParallelFfmpeg maximum");
                    Check.Equal(1, m.JpegQuality, "JpegQuality minimum");

                    InferenceSettings i = new InferenceSettings { MaxRetries = 99, MaxRepairAttempts = -4 };
                    Check.Equal(10, i.MaxRetries, "MaxRetries maximum");
                    Check.Equal(0, i.MaxRepairAttempts, "MaxRepairAttempts minimum");

                    Check.Throws<ArgumentOutOfRangeException>(() => new RestSettings { Port = 0 }, "Rest.Port 0");
                    Check.Throws<ArgumentNullException>(() => new AuthSettings { AdminApiKey = " " }, "blank admin key");
                    return Task.CompletedTask;
                }, new[] { "fast" }),

                new TestCaseDescriptor(Id, "RoundTrip", "Settings round-trip through JSON with string enums", ct =>
                {
                    KeyframeSettings s = new KeyframeSettings();
                    s.Rest.Port = 9123;
                    string json = KeyframeSerializer.Serialize(s, true);
                    Check.True(json.Contains("\"Provider\": \"Mock\""), "enums serialize as strings");
                    KeyframeSettings? copy = KeyframeSerializer.Deserialize<KeyframeSettings>(json);
                    Check.True(copy != null, "deserialized");
                    Check.Equal(9123, copy!.Rest.Port, "round-tripped port");
                    Check.Equal(2, copy.Inference.Endpoints.Count, "round-tripped endpoints are not duplicated");
                    return Task.CompletedTask;
                }, new[] { "fast" })
            });
        }
    }
}
