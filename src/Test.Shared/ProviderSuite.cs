namespace Test.Shared
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Threading.Tasks;
    using Keyframe.Core.Analysis;
    using Keyframe.Core.Enums;
    using Keyframe.Core.Exceptions;
    using Keyframe.Core.Inference;
    using Keyframe.Core.Inference.Providers;
    using Keyframe.Core.Requests;
    using Keyframe.Core.Responses;
    using Keyframe.Core.Settings;
    using Touchstone.Core;

    /// <summary>
    /// The PolyPrompt provider integration against a local OpenAI-compatible stub server: request translation,
    /// streamed response, timing, and usage mapping, retry classification, transcription segments, factory availability, and an
    /// end-to-end image analysis.
    /// </summary>
    public static class ProviderSuite
    {
        private const string Id = "Provider";

        /// <summary>
        /// Build the suite.
        /// </summary>
        /// <returns>Suite.</returns>
        public static TestSuiteDescriptor Create()
        {
            string? skip = TestEnvironment.FfmpegSkipReason;
            return new TestSuiteDescriptor(Id, "PolyPrompt providers", new[]
            {
                new TestCaseDescriptor(Id, "Availability", "Supported providers report available; Vertex AI and Bedrock do not", ct =>
                {
                    KeyframeSettings settings = TestEnvironment.CreateSettings();
                    using (AnalysisServices services = new AnalysisServices(() => settings))
                    {
                        foreach (ProviderTypeEnum p in new[] { ProviderTypeEnum.Ollama, ProviderTypeEnum.OllamaCloud, ProviderTypeEnum.OpenAi, ProviderTypeEnum.OpenAiCompatible,
                            ProviderTypeEnum.AzureOpenAi, ProviderTypeEnum.Anthropic, ProviderTypeEnum.Gemini, ProviderTypeEnum.XAi, ProviderTypeEnum.Cohere })
                        {
                            Check.True(services.Clients.IsProviderAvailable(Endpoint(p, EndpointKindEnum.Completion, null)), p + " completion available");
                        }

                        Check.True(!services.Clients.IsProviderAvailable(Endpoint(ProviderTypeEnum.VertexAi, EndpointKindEnum.Completion, null)), "Vertex AI not available");
                        Check.True(!services.Clients.IsProviderAvailable(Endpoint(ProviderTypeEnum.Bedrock, EndpointKindEnum.Completion, null)), "Bedrock not available");
                        Check.True(services.Clients.IsProviderAvailable(Endpoint(ProviderTypeEnum.OpenAiCompatible, EndpointKindEnum.Transcription, null)), "compatible transcription available");
                        Check.True(!services.Clients.IsProviderAvailable(Endpoint(ProviderTypeEnum.Anthropic, EndpointKindEnum.Transcription, null)), "Anthropic transcription not available");

                        EndpointSettings keyless = Endpoint(ProviderTypeEnum.OpenAi, EndpointKindEnum.Completion, null);
                        keyless.ApiKey = null;
                        Check.Throws<InferenceException>(() => services.Clients.GetCompletionClient(keyless), "missing key is a misconfiguration");
                    }

                    return Task.CompletedTask;
                }, new[] { "provider" }),

                new TestCaseDescriptor(Id, "ImageRequest", "System prompt, labeled images, options, and usage map to the wire and back", async ct =>
                {
                    using (StubProviderServer stub = new StubProviderServer())
                    using (PolyPromptInferenceClient client = new PolyPromptInferenceClient(Endpoint(ProviderTypeEnum.OpenAiCompatible, EndpointKindEnum.Completion, stub.BaseUrl)))
                    {
                        InferenceRequest request = new InferenceRequest
                        {
                            SystemPrompt = "You are a media analyst.",
                            UserPrompt = "Describe the frames.",
                            MaxOutputTokens = 3210,
                            Temperature = 0.2,
                            Images = new List<InferenceImage> { new InferenceImage { Data = TinyJpeg(), MediaType = "image/jpeg", Label = "Frame at 00:01.000" } }
                        };

                        InferenceResponse response = await client.CompleteAsync(request, ct).ConfigureAwait(false);
                        Check.True(response.Success, "success: " + response.Error);
                        Check.Equal("Stub reply.", response.Text, "text");
                        Check.Equal("stop", response.FinishReason, "finish reason");
                        Check.Equal(11L, response.PromptTokens, "prompt tokens");
                        Check.Equal(3L, response.CompletionTokens, "completion tokens");
                        Check.Equal(14L, response.TotalTokens, "total tokens");
                        Check.Equal(ProviderTypeEnum.OpenAiCompatible, response.Provider, "provider");
                        Check.True(response.TimeToFirstTokenMs.HasValue && response.TimeToFirstTokenMs.Value > 0, "time to first token measured");
                        Check.True(response.GenerationMs.HasValue && response.GenerationMs.Value >= 20, "generation spans the streamed chunks: " + response.GenerationMs);
                        Check.True(response.TokensPerSecond.HasValue && response.TokensPerSecond.Value > 0, "tokens per second computed");
                        Check.True(Math.Abs(response.TokensPerSecond!.Value - 3 / (response.GenerationMs!.Value / 1000.0)) < 0.01, "tokens per second is completion tokens over generation seconds");
                        Check.True(response.TimeToFirstTokenMs!.Value + response.GenerationMs.Value <= response.DurationMs + 1, "first token plus generation fits in the duration");

                        StubRequest sent = stub.Requests.Single();
                        Check.True(sent.Path.EndsWith("/chat/completions"), "chat route: " + sent.Path);
                        using (JsonDocument doc = JsonDocument.Parse(sent.Body))
                        {
                            JsonElement root = doc.RootElement;
                            Check.True(root.GetProperty("stream").GetBoolean(), "completion calls stream");
                            Check.Equal("stub-model", root.GetProperty("model").GetString(), "model from endpoint");
                            Check.Equal(3210, root.GetProperty("max_tokens").GetInt32(), "request budget above the endpoint setting is used");
                            JsonElement messages = root.GetProperty("messages");
                            Check.Equal("system", messages[0].GetProperty("role").GetString(), "system first");
                            JsonElement parts = messages[1].GetProperty("content");
                            Check.Equal(3, parts.GetArrayLength(), "prompt, label, image");
                            Check.Equal("Frame at 00:01.000", parts[1].GetProperty("text").GetString(), "label before image");
                            Check.True((parts[2].GetProperty("image_url").GetProperty("url").GetString() ?? "").StartsWith("data:image/jpeg;base64,"), "image data URL");
                        }
                    }
                }, new[] { "provider" }),

                new TestCaseDescriptor(Id, "Failures", "429 and 503 are retryable, 400 is not, and validation errors never send", async ct =>
                {
                    using (StubProviderServer stub = new StubProviderServer())
                    using (PolyPromptInferenceClient client = new PolyPromptInferenceClient(Endpoint(ProviderTypeEnum.OpenAiCompatible, EndpointKindEnum.Completion, stub.BaseUrl)))
                    {
                        foreach (int code in new[] { 429, 503, 400 })
                        {
                            stub.StatusCode = code;
                            InferenceResponse failed = await client.CompleteAsync(new InferenceRequest { UserPrompt = "hi" }, ct).ConfigureAwait(false);
                            Check.True(!failed.Success, code + " fails");
                            Check.Equal(code, failed.StatusCode, code + " status");
                            Check.Equal(code != 400, failed.Retryable, code + " retryable");
                            Check.True(!String.IsNullOrEmpty(failed.Error), code + " error text");
                        }

                        int before = stub.Requests.Count;
                        InferenceResponse invalid = await client.CompleteAsync(new InferenceRequest
                        {
                            UserPrompt = "hi",
                            Images = new List<InferenceImage> { new InferenceImage { Data = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, MediaType = "image/jpeg" } }
                        }, ct).ConfigureAwait(false);
                        Check.True(!invalid.Success && !invalid.Retryable, "PNG bytes declared as JPEG rejected, not retryable");
                        Check.Equal(before, stub.Requests.Count, "nothing sent");
                    }
                }, new[] { "provider" }),

                new TestCaseDescriptor(Id, "Transcription", "Verbose JSON segments map to milliseconds; segments that are likely silence are dropped", async ct =>
                {
                    using (StubProviderServer stub = new StubProviderServer())
                    using (PolyPromptTranscriptionClient client = new PolyPromptTranscriptionClient(Endpoint(ProviderTypeEnum.OpenAiCompatible, EndpointKindEnum.Transcription, stub.BaseUrl)))
                    {
                        TranscriptionResponse response = await client.TranscribeAsync(new TranscriptionRequest
                        {
                            Audio = TinyFlac(),
                            MediaType = "audio/flac",
                            FileName = "chunk.flac",
                            Prompt = "Keyframe"
                        }, ct).ConfigureAwait(false);

                        Check.True(response.Success, "success: " + response.Error);
                        Check.Equal("hello world", response.Text, "text");
                        Check.Equal("english", response.Language, "language");
                        Check.Equal(2, response.Segments.Count, "segments");
                        Check.Equal(1250L, response.Segments[0].EndMs, "end ms");
                        Check.Equal("world", response.Segments[1].Text, "trimmed text");

                        StubRequest sent = stub.Requests.Single();
                        Check.True(sent.Path.EndsWith("/audio/transcriptions"), "transcription route");
                        Check.True((sent.ContentType ?? "").StartsWith("multipart/form-data"), "multipart");
                        Check.True(sent.Body.Contains("verbose_json"), "verbose json requested");
                        Check.True(sent.Body.Contains("segment"), "segment granularity requested");
                    }
                }, new[] { "provider" }),

                new TestCaseDescriptor(Id, "ImageEndToEnd", "Image analysis through an OpenAI-compatible endpoint", async ct =>
                {
                    using (StubProviderServer stub = new StubProviderServer())
                    {
                        stub.ChatReply = body => body.Contains("hasCats") ? "{\"hasCats\": true}" : "A stub summary of the image.";
                        KeyframeSettings settings = TestEnvironment.CreateSettings();
                        settings.Inference.Endpoints.Add(Endpoint(ProviderTypeEnum.OpenAiCompatible, EndpointKindEnum.Completion, stub.BaseUrl));
                        settings.Inference.DefaultVision = "stub";

                        using (AnalysisServices services = new AnalysisServices(() => settings))
                        {
                            string png = await TestEnvironment.PngAsync(ct).ConfigureAwait(false);
                            ImageAnalysisRequest request = new ImageAnalysisRequest { Image = new MediaSource { Data = await TestEnvironment.Base64Async(png, ct).ConfigureAwait(false) } };
                            request.Functions.Summarize.Enable = true;
                            request.Functions.Extract.Enable = true;
                            request.Functions.Extract.Fields = new List<FieldSpec> { new FieldSpec { Key = "hasCats", Type = FieldTypeEnum.Boolean, Prompt = "Does this image have cats?" } };

                            ImageAnalysisResponse response = await new ImageAnalyzer(services).AnalyzeAsync(request, ct).ConfigureAwait(false);
                            Check.True(response.Success, "success: " + String.Join(" ", response.Errors));
                            Check.Equal("A stub summary of the image.", response.Results["Summary"] as string, "summary");
                            Check.Equal(true, response.Results["hasCats"] as bool?, "typed field");
                            Check.True(response.Telemetry.ModelCalls.All(c => c.Provider == ProviderTypeEnum.OpenAiCompatible && c.TotalTokens == 14), "telemetry per call");
                            Check.True(response.Telemetry.ModelCalls.All(c => c.TimeToFirstTokenMs > 0 && c.GenerationMs > 0 && c.TokensPerSecond > 0), "streaming timing per call");
                            Check.True(response.Telemetry.ModelCalls.All(c => c.QueueMs >= 0 && c.AttemptDetails.Count == c.Attempts), "queue time and attempt detail per call");
                            Check.Equal(2, stub.Requests.Count, "summary and one combined extract call");
                        }
                    }
                }, new[] { "provider", "ffmpeg" }, skip)
            });
        }

        private static EndpointSettings Endpoint(ProviderTypeEnum provider, EndpointKindEnum kind, string? url)
        {
            return new EndpointSettings
            {
                Name = "stub",
                Provider = provider,
                Kind = kind,
                Endpoint = url ?? "http://127.0.0.1:9/v1",
                ApiKey = "test-key-0000",
                Model = "stub-model",
                TimeoutMs = 10000
            };
        }

        private static byte[] TinyJpeg()
        {
            return new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9 };
        }

        private static byte[] TinyFlac()
        {
            return new byte[] { 0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x00, 0x22, 0, 0, 0, 0, 0, 0, 0, 0 };
        }
    }
}
