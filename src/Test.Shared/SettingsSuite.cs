namespace Test.Shared
{
    using System;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;
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
                    Check.Equal(4, s.Inference.Endpoints.Count, "default endpoint count");
                    Check.True(!s.Inference.Endpoints.Exists(e => e.Provider == ProviderTypeEnum.Mock), "no mock endpoints in shipped defaults");
                    Check.Equal("ollama-gemma3-4b", s.Inference.DefaultVision, "default vision");
                    Check.Equal("ollama-gpt-oss-20b", s.Inference.DefaultText, "default text");
                    Check.Equal("whisper", s.Inference.DefaultTranscription, "default transcription");
                    Check.True(s.Inference.FindActive("OLLAMA-GEMMA3-4B") != null, "endpoint lookup is case-insensitive");
                    EndpointSettings whisper = s.Inference.FindActive("whisper")!;
                    Check.Equal(ProviderTypeEnum.OpenAiCompatible, whisper.Provider, "whisper provider");
                    Check.Equal(EndpointKindEnum.Transcription, whisper.Kind, "whisper kind");
                    Check.Equal("Systran/faster-whisper-small", whisper.Model, "whisper model");
                    EndpointSettings gptoss = s.Inference.FindActive("ollama-gpt-oss-20b")!;
                    Check.Equal(4096, gptoss.MaxOutputTokens, "ollama max output tokens");
                    Check.Equal(300000, gptoss.TimeoutMs, "ollama timeout");
                    Check.Equal(true, s.Analytics.Enable, "Analytics.Enable");
                    Check.Equal(90, s.Analytics.RetentionDays, "Analytics.RetentionDays");
                    Check.Equal(600000L, s.Media.MaxSegmentMs, "Media.MaxSegmentMs");
                    Check.Equal(3650, new AnalyticsSettings { RetentionDays = 99999 }.RetentionDays, "retention maximum");
                    Check.Equal(1, new AnalyticsSettings { RetentionDays = 0 }.RetentionDays, "retention minimum");
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
                    Check.True(json.Contains("\"Provider\": \"Ollama\""), "enums serialize as strings");
                    KeyframeSettings? copy = KeyframeSerializer.Deserialize<KeyframeSettings>(json);
                    Check.True(copy != null, "deserialized");
                    Check.Equal(9123, copy!.Rest.Port, "round-tripped port");
                    Check.Equal(4, copy.Inference.Endpoints.Count, "round-tripped endpoints are not duplicated");
                    return Task.CompletedTask;
                }, new[] { "fast" })
            });
        }
    }
}
