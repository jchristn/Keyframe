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
    /// Listening cases: GEMINI_API_KEY; OPENAI_API_KEY (optional KEYFRAME_TEST_OPENAI_AUDIO_MODEL, default gpt-audio-mini);
    /// KEYFRAME_TEST_LLAMA_URL (optional KEYFRAME_TEST_LLAMA_MODEL); KEYFRAME_TEST_VLLM_URL (optional KEYFRAME_TEST_VLLM_MODEL).
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
                AudioCase(),
                ListenCase("GeminiListen", ProviderTypeEnum.Gemini, null, Env("GEMINI_API_KEY"), Env("KEYFRAME_TEST_GEMINI_MODEL"),
                    Env("GEMINI_API_KEY") == null ? "Set GEMINI_API_KEY." : null),
                ListenCase("OpenAiListen", ProviderTypeEnum.OpenAiCompatible, "https://api.openai.com/v1", Env("OPENAI_API_KEY"), Env("KEYFRAME_TEST_OPENAI_AUDIO_MODEL") ?? "gpt-audio-mini",
                    Env("OPENAI_API_KEY") == null ? "Set OPENAI_API_KEY." : null),
                ListenVariantCase("GeminiRenditions", ProviderTypeEnum.Gemini, null, Env("GEMINI_API_KEY"), Env("KEYFRAME_TEST_GEMINI_MODEL"),
                    Env("GEMINI_API_KEY") == null ? "Set GEMINI_API_KEY." : null,
                    new[] { new AudioRenditionSpec { Format = ListenFormatEnum.Mp3, SampleRate = 44100, Channels = 2, BitrateKbps = 128 }, new AudioRenditionSpec { Format = ListenFormatEnum.Wav }, new AudioRenditionSpec { Format = ListenFormatEnum.Flac } }, null),
                ListenVariantCase("GeminiExcerpts", ProviderTypeEnum.Gemini, null, Env("GEMINI_API_KEY"), Env("KEYFRAME_TEST_GEMINI_MODEL"),
                    Env("GEMINI_API_KEY") == null ? "Set GEMINI_API_KEY." : null, null, 10),
                ListenVariantCase("OpenAiRenditions", ProviderTypeEnum.OpenAiCompatible, "https://api.openai.com/v1", Env("OPENAI_API_KEY"), Env("KEYFRAME_TEST_OPENAI_AUDIO_MODEL") ?? "gpt-audio-mini",
                    Env("OPENAI_API_KEY") == null ? "Set OPENAI_API_KEY." : null,
                    new[] { new AudioRenditionSpec { Format = ListenFormatEnum.Mp3, SampleRate = 44100, Channels = 2, BitrateKbps = 128 }, new AudioRenditionSpec { Format = ListenFormatEnum.Wav } }, null),
                ListenVariantCase("OpenAiExcerpts", ProviderTypeEnum.OpenAiCompatible, "https://api.openai.com/v1", Env("OPENAI_API_KEY"), Env("KEYFRAME_TEST_OPENAI_AUDIO_MODEL") ?? "gpt-audio-mini",
                    Env("OPENAI_API_KEY") == null ? "Set OPENAI_API_KEY." : null, null, 10),
                ListenCase("LlamaServerListen", ProviderTypeEnum.OpenAiCompatible, Env("KEYFRAME_TEST_LLAMA_URL"), null, Env("KEYFRAME_TEST_LLAMA_MODEL"),
                    Env("KEYFRAME_TEST_LLAMA_URL") == null ? "Set KEYFRAME_TEST_LLAMA_URL (for example http://127.0.0.1:8720/v1) and optionally KEYFRAME_TEST_LLAMA_MODEL." : null),
                ListenCase("VllmListen", ProviderTypeEnum.OpenAiCompatible, Env("KEYFRAME_TEST_VLLM_URL"), null, Env("KEYFRAME_TEST_VLLM_MODEL") ?? "mistralai/Voxtral-Mini-3B-2507",
                    Env("KEYFRAME_TEST_VLLM_URL") == null ? "Set KEYFRAME_TEST_VLLM_URL (for example http://gpu-host:8730/v1) and optionally KEYFRAME_TEST_VLLM_MODEL." : null)
            });
        }

        private static TestCaseDescriptor ListenVariantCase(string name, ProviderTypeEnum provider, string? url, string? key, string? model, string? missing, AudioRenditionSpec[]? renditions, int? maxListenSeconds)
        {
            string? skip = missing ?? TestEnvironment.FfmpegSkipReason;
            string what = renditions != null ? "accepts every offered rendition" : "hears several excerpts in one call";
            return new TestCaseDescriptor(Id, name, name.Replace("Renditions", String.Empty).Replace("Excerpts", String.Empty) + " " + what, async ct =>
            {
                KeyframeSettings settings = TestEnvironment.CreateSettings();
                settings.Inference.Endpoints.Add(new EndpointSettings
                {
                    Name = "live-listen",
                    Provider = provider,
                    Kind = EndpointKindEnum.Completion,
                    Endpoint = url,
                    ApiKey = key,
                    Model = model,
                    TimeoutMs = 600000,
                    MaxOutputTokens = 4096,
                    MaxConcurrentRequests = 1,
                    MaxListenSeconds = maxListenSeconds
                });
                settings.Inference.DefaultListening = "live-listen";
                settings.Inference.MaxRetries = 1;

                using (AnalysisServices services = new AnalysisServices(() => settings))
                {
                    string data = await TestEnvironment.Base64Async(await TestEnvironment.WavAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
                    List<string> problems = new List<string>();
                    foreach (AudioRenditionSpec? rendition in renditions ?? new AudioRenditionSpec?[] { null })
                    {
                        AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = data } };
                        request.Functions.Transcribe.Enable = false;
                        if (rendition != null) request.Listen.Rendition = rendition;
                        if (maxListenSeconds.HasValue)
                        {
                            request.Listen.SampleClips = 2;
                            request.Listen.ClipSeconds = 5;
                        }

                        AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                        string label = rendition == null ? "default" : rendition.Format + " " + rendition.SampleRate + " Hz " + rendition.Channels + " ch";
                        if (!response.Success) problems.Add(label + ": " + String.Join(" | ", response.Errors));
                        else if (!(response.Results["Summary"] is string sum) || sum.Length == 0) problems.Add(label + ": empty summary");
                        if (maxListenSeconds.HasValue && (response.Listening.Mode != ListeningModeEnum.Excerpts || response.Listening.Clips.Count != 2))
                            problems.Add("expected two excerpts in one call, got " + response.Listening.Mode + " with " + response.Listening.Clips.Count + " clips");
                    }

                    Check.True(problems.Count == 0, String.Join(" || ", problems));
                }
            }, new[] { "live" }, skip);
        }

        private static TestCaseDescriptor ListenCase(string name, ProviderTypeEnum provider, string? url, string? key, string? model, string? missing)
        {
            string? skip = missing ?? TestEnvironment.FfmpegSkipReason;
            return new TestCaseDescriptor(Id, name, name.Replace("Listen", String.Empty) + " listens to a tone recording and fills a typed field", async ct =>
            {
                KeyframeSettings settings = TestEnvironment.CreateSettings();
                settings.Inference.Endpoints.Add(new EndpointSettings
                {
                    Name = "live-listen",
                    Provider = provider,
                    Kind = EndpointKindEnum.Completion,
                    Endpoint = url,
                    ApiKey = key,
                    Model = model,
                    TimeoutMs = 600000,
                    MaxOutputTokens = 4096,
                    MaxConcurrentRequests = 1
                });
                settings.Inference.DefaultListening = "live-listen";

                using (AnalysisServices services = new AnalysisServices(() => settings))
                {
                    AudioAnalysisRequest request = new AudioAnalysisRequest { Audio = new MediaSource { Data = await TestEnvironment.Base64Async(await TestEnvironment.Mp3Async(ct).ConfigureAwait(false), ct).ConfigureAwait(false) } };
                    request.Functions.Transcribe.Enable = false;
                    request.Functions.Extract.Enable = true;
                    request.Functions.Extract.Fields.Add(new FieldSpec { Key = "hasSpeech", Type = FieldTypeEnum.Boolean, Prompt = "Does the recording contain human speech?" });
                    AudioAnalysisResponse response = await new AudioAnalyzer(services).AnalyzeAsync(request, null, null, ct).ConfigureAwait(false);
                    string detail = System.Text.Json.JsonSerializer.Serialize(response.Results) + " " + String.Join(" | ", response.Errors);
                    Check.True(response.Success, "success: " + detail);
                    Check.True(response.Listening.Listened && response.Listening.Mode == ListeningModeEnum.Full, "heard in full");
                    Check.True(response.Results["Summary"] is string s && s.Length > 0, "summary: " + detail);
                    Check.True(response.Results["hasSpeech"] is bool, "typed field: " + detail);
                    Check.True(response.Telemetry.ModelCalls.Where(c => c.Purpose != ModelCallPurposeEnum.Transcribe).All(c => c.InputAudioBytes > 0), "audio sent on every call");
                }
            }, new[] { "live" }, skip);
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
