namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// Live image analysis against real providers. Each case is skipped unless its environment variables are set:
    /// KEYFRAME_TEST_OLLAMA_URL and KEYFRAME_TEST_OLLAMA_MODEL; OPENAI_API_KEY; ANTHROPIC_API_KEY; GEMINI_API_KEY;
    /// KEYFRAME_TEST_WHISPER_URL (audio; optional KEYFRAME_TEST_WHISPER_MODEL, and KEYFRAME_TEST_OLLAMA_TEXT_MODEL or
    /// KEYFRAME_TEST_OLLAMA_MODEL with KEYFRAME_TEST_OLLAMA_URL to also summarize the transcript).
    /// KEYFRAME_TEST_OPENAI_MODEL, KEYFRAME_TEST_ANTHROPIC_MODEL, and KEYFRAME_TEST_GEMINI_MODEL override the
    /// provider default models.
    /// </summary>
    public static class LiveProviderSuite
    {
        private const string Id = "LiveProvider";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            return new TestSuiteDescriptor(Id, "Live providers (opt-in)", new[]
            {
                Case("Ollama", ProviderTypeEnum.Ollama, Env("KEYFRAME_TEST_OLLAMA_URL"), null, Env("KEYFRAME_TEST_OLLAMA_MODEL"),
                    Env("KEYFRAME_TEST_OLLAMA_URL") == null || Env("KEYFRAME_TEST_OLLAMA_MODEL") == null ? "Set KEYFRAME_TEST_OLLAMA_URL and KEYFRAME_TEST_OLLAMA_MODEL." : null),
                Case("OpenAi", ProviderTypeEnum.OpenAi, null, Env("OPENAI_API_KEY"), Env("KEYFRAME_TEST_OPENAI_MODEL"),
                    Env("OPENAI_API_KEY") == null ? "Set OPENAI_API_KEY." : null),
                Case("Anthropic", ProviderTypeEnum.Anthropic, null, Env("ANTHROPIC_API_KEY"), Env("KEYFRAME_TEST_ANTHROPIC_MODEL"),
                    Env("ANTHROPIC_API_KEY") == null ? "Set ANTHROPIC_API_KEY." : null),
                Case("Gemini", ProviderTypeEnum.Gemini, null, Env("GEMINI_API_KEY"), Env("KEYFRAME_TEST_GEMINI_MODEL"),
                    Env("GEMINI_API_KEY") == null ? "Set GEMINI_API_KEY." : null),
                AudioCase()
            });
        }

        private static TestCaseDescriptor AudioCase()
        {
            string? whisper = Env("KEYFRAME_TEST_WHISPER_URL");
            string? skip = (whisper == null ? "Set KEYFRAME_TEST_WHISPER_URL (for example http://127.0.0.1:8000/v1)." : null) ?? TestEnvironment.FfmpegSkipReason;
            return new TestCaseDescriptor(Id, "WhisperAudio", "A speech clip is transcribed by an OpenAI-compatible Whisper server, then summarized when an Ollama text model is configured", async ct =>
            {
                KeyframeSettings settings = TestEnvironment.CreateSettings();
                settings.Inference.Endpoints.Add(new EndpointSettings
                {
                    Name = "live-whisper",
                    Provider = ProviderTypeEnum.OpenAiCompatible,
                    Kind = EndpointKindEnum.Transcription,
                    Endpoint = whisper,
                    Model = Env("KEYFRAME_TEST_WHISPER_MODEL") ?? "Systran/faster-whisper-small",
                    TimeoutMs = 300000
                });
                settings.Inference.DefaultTranscription = "live-whisper";

                string? ollama = Env("KEYFRAME_TEST_OLLAMA_URL");
                string? textModel = Env("KEYFRAME_TEST_OLLAMA_TEXT_MODEL") ?? Env("KEYFRAME_TEST_OLLAMA_MODEL");
                bool summarize = ollama != null && textModel != null;
                if (summarize)
                {
                    settings.Inference.Endpoints.Add(new EndpointSettings
                    {
                        Name = "live-text",
                        Provider = ProviderTypeEnum.Ollama,
                        Endpoint = ollama,
                        Model = textModel,
                        TimeoutMs = 300000,
                        MaxOutputTokens = 4096
                    });
                    settings.Inference.DefaultText = "live-text";
                }

                using (AnalysisServices services = new AnalysisServices(() => settings))
                {
                    string? speech = await TestEnvironment.SpeechAsync(ct).ConfigureAwait(false);
                    string path = speech ?? await TestEnvironment.Mp3Async(ct).ConfigureAwait(false);
                    AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(path, ct).ConfigureAwait(false) } };
                    request.Functions.Transcribe.Language = "en";
                    request.Functions.Summarize.Enable = summarize;
                    if (summarize)
                    {
                        request.Functions.Extract.Enable = true;
                        request.Functions.Extract.Fields = new List<FieldSpec>
                        {
                            new FieldSpec { Key = "mentionsWeather", Type = FieldTypeEnum.Boolean, Prompt = "Does the speaker mention the weather?" }
                        };
                    }

                    AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                    string detail = System.Text.Json.JsonSerializer.Serialize(response.Results) + " " + (response.Transcript?.Text ?? "(no transcript)");
                    Check.True(response.Success, "success: " + String.Join(" ", response.Errors));
                    Check.True(response.Transcript != null, "transcript");
                    if (speech != null)
                    {
                        string text = response.Transcript!.Text.ToLowerInvariant();
                        Check.True(text.Contains("audio") && text.Contains("weather"), "transcript matches the spoken text: " + detail);
                        Check.Equal("en", response.Transcript.Language, "language code");
                    }

                    if (summarize)
                    {
                        Check.True(response.Results["Summary"] is string s && s.Length > 0, "summary: " + detail);
                        Check.True(response.Results["mentionsWeather"] is bool, "typed field: " + detail);
                        ModelCallTelemetry call = response.Telemetry.ModelCalls.First(c => c.Purpose == ModelCallPurposeEnum.Summarize);
                        Check.True(call.TimeToFirstTokenMs > 0 && call.GenerationMs.HasValue, "streaming timing from Ollama");
                    }
                }
            }, new[] { "live" }, skip);
        }

        private static TestCaseDescriptor Case(string name, ProviderTypeEnum provider, string? url, string? key, string? model, string? missing)
        {
            string? skip = missing ?? TestEnvironment.FfmpegSkipReason;
            return new TestCaseDescriptor(Id, name, name + " summarizes an image and fills a typed field", async ct =>
            {
                KeyframeSettings settings = TestEnvironment.CreateSettings();
                settings.Inference.Endpoints.Add(new EndpointSettings
                {
                    Name = "live",
                    Provider = provider,
                    Endpoint = url,
                    ApiKey = key,
                    Model = model,
                    TimeoutMs = 300000,
                    MaxOutputTokens = 4096
                });
                settings.Inference.DefaultVision = "live";

                using (AnalysisServices services = new AnalysisServices(() => settings))
                {
                    string png = await TestEnvironment.PngAsync(ct).ConfigureAwait(false);
                    ImageAnalysisRequest request = new ImageAnalysisRequest { Image = new MediaSource { Data = await TestEnvironment.Base64Async(png, ct).ConfigureAwait(false) } };
                    request.Functions.Summarize.Enable = true;
                    request.Functions.Extract.Enable = true;
                    request.Functions.Extract.Fields = new List<FieldSpec>
                    {
                        new FieldSpec { Key = "hasPeople", Type = FieldTypeEnum.Boolean, Prompt = "Does the image show any people?" },
                        new FieldSpec { Key = "colors", Type = FieldTypeEnum.StringList, Prompt = "The colors visible in the image.", MaxItems = 8 }
                    };

                    ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);
                    string detail = System.Text.Json.JsonSerializer.Serialize(response.Results) + " " + System.Text.Json.JsonSerializer.Serialize(response.Fields);
                    Check.True(response.Success, "success: " + String.Join(" ", response.Errors));
                    Check.True(response.Results["Summary"] is string s && s.Length > 0, "summary");
                    Check.True(response.Results["hasPeople"] is bool, "boolean field typed: " + detail);
                    Check.True(response.Results["colors"] is System.Collections.IEnumerable list && list.GetEnumerator().MoveNext(), "string list field typed and non-empty: " + detail);
                    Check.True(response.Telemetry.Totals.TotalTokens > 0, "tokens reported");
                }
            }, new[] { "live" }, skip);
        }

        private static string? Env(string name)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            return String.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
