namespace Keyframe.Core.Inference
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.Core.Enums;

    /// <summary>
    /// Deterministic offline completion client for tests and demos. Text requests return a synthetic summary.
    /// JSON requests return values generated from the request's JSON Schema, seeded by the field name and the
    /// image hashes so the same input always gives the same output. Failure injection counters support tests.
    /// Thread safety: safe for concurrent use.
    /// </summary>
    public class MockInferenceClient : IInferenceClient
    {
        #region Public-Members

        /// <inheritdoc />
        public string EndpointName { get; }

        /// <inheritdoc />
        public ProviderTypeEnum Provider
        {
            get
            {
                return ProviderTypeEnum.Mock;
            }
        }

        /// <inheritdoc />
        public string? Model { get; }

        /// <summary>
        /// Simulated latency per call in milliseconds. Default 0. Minimum 0, maximum 600000.
        /// </summary>
        public int DelayMs
        {
            get
            {
                return _DelayMs;
            }
            set
            {
                _DelayMs = Math.Clamp(value, 0, 600000);
            }
        }

        /// <summary>
        /// Calls made so far.
        /// </summary>
        public int CallCount
        {
            get
            {
                return Volatile.Read(ref _CallCount);
            }
        }

        #endregion

        #region Private-Members

        private int _DelayMs = 0;
        private int _CallCount = 0;
        private int _TransientFailures = 0;
        private int _PermanentFailures = 0;
        private int _MalformedResponses = 0;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate the mock client.
        /// </summary>
        /// <param name="endpointName">Endpoint name.</param>
        /// <param name="model">Model name reported in responses.</param>
        /// <exception cref="ArgumentNullException">Thrown when endpointName is null or empty.</exception>
        public MockInferenceClient(string endpointName, string? model = "mock-vision")
        {
            if (String.IsNullOrWhiteSpace(endpointName)) throw new ArgumentNullException(nameof(endpointName));
            EndpointName = endpointName;
            Model = model;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Make the next calls fail with a retryable 503.
        /// </summary>
        /// <param name="count">Number of calls.</param>
        public void FailNextTransient(int count)
        {
            Interlocked.Add(ref _TransientFailures, Math.Max(0, count));
        }

        /// <summary>
        /// Make the next calls fail with a non-retryable 400.
        /// </summary>
        /// <param name="count">Number of calls.</param>
        public void FailNextPermanent(int count)
        {
            Interlocked.Add(ref _PermanentFailures, Math.Max(0, count));
        }

        /// <summary>
        /// Make the next JSON calls return malformed output.
        /// </summary>
        /// <param name="count">Number of calls.</param>
        public void MalformNext(int count)
        {
            Interlocked.Add(ref _MalformedResponses, Math.Max(0, count));
        }

        /// <inheritdoc />
        public async Task<InferenceResponse> CompleteAsync(InferenceRequest request, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            Stopwatch sw = Stopwatch.StartNew();
            Interlocked.Increment(ref _CallCount);

            if (_DelayMs > 0) await Task.Delay(_DelayMs, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            InferenceResponse response = new InferenceResponse
            {
                Provider = ProviderTypeEnum.Mock,
                Model = request.Model ?? Model
            };

            if (TryConsume(ref _TransientFailures))
            {
                response.Success = false;
                response.Retryable = true;
                response.StatusCode = 503;
                response.Error = "Mock transient failure.";
                response.DurationMs = sw.Elapsed.TotalMilliseconds;
                return response;
            }

            if (TryConsume(ref _PermanentFailures))
            {
                response.Success = false;
                response.Retryable = false;
                response.StatusCode = 400;
                response.Error = "Mock permanent failure.";
                response.DurationMs = sw.Elapsed.TotalMilliseconds;
                return response;
            }

            string seed = String.Join("|", request.Images.Select(i => i.Sha256 ?? i.Data.Length.ToString()));
            string text;
            if (request.ResponseFormat == ResponseFormatEnum.JsonObject)
            {
                text = TryConsume(ref _MalformedResponses)
                    ? "Here is the answer: {\"unterminated\": "
                    : "```json\n" + BuildJson(request.JsonSchema, seed) + "\n```";
            }
            else
            {
                text = BuildSummary(request, seed);
            }

            response.Success = true;
            response.Text = text;
            response.StatusCode = 200;
            response.FinishReason = "stop";
            response.PromptTokens = (request.SystemPrompt.Length + request.UserPrompt.Length) / 4 + 85L * request.Images.Count;
            response.CompletionTokens = Math.Max(1, text.Length / 4);
            response.TotalTokens = response.PromptTokens + response.CompletionTokens;
            response.DurationMs = sw.Elapsed.TotalMilliseconds;
            return response;
        }

        #endregion

        #region Private-Methods

        private static bool TryConsume(ref int counter)
        {
            while (true)
            {
                int current = Volatile.Read(ref counter);
                if (current <= 0) return false;
                if (Interlocked.CompareExchange(ref counter, current - 1, current) == current) return true;
            }
        }

        private static string BuildSummary(InferenceRequest request, string seed)
        {
            string id = Hash(seed).ToString("x8");
            int images = request.Images.Count;
            string subject = images == 1 ? "1 image" : images + " images";
            return "Mock summary of " + subject + " (" + id + "): a deterministic description generated by the Keyframe mock provider for "
                + request.Purpose.ToString() + (request.Target != null ? " " + request.Target : string.Empty) + ".";
        }

        private static string BuildJson(string? schema, string seed)
        {
            Dictionary<string, object?> output = new Dictionary<string, object?>();
            if (!String.IsNullOrWhiteSpace(schema))
            {
                using (JsonDocument doc = JsonDocument.Parse(schema))
                {
                    if (doc.RootElement.TryGetProperty("properties", out JsonElement props) && props.ValueKind == JsonValueKind.Object)
                    {
                        foreach (JsonProperty prop in props.EnumerateObject())
                            output[prop.Name] = ValueFor(prop.Name, prop.Value, seed);
                    }
                }
            }

            return JsonSerializer.Serialize(output);
        }

        private static object? ValueFor(string name, JsonElement schema, string seed)
        {
            uint h = Hash(name + "|" + seed);
            string type = schema.TryGetProperty("type", out JsonElement t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "string" : "string";

            if (schema.TryGetProperty("enum", out JsonElement en) && en.ValueKind == JsonValueKind.Array && en.GetArrayLength() > 0)
            {
                List<string> options = en.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString() ?? string.Empty).ToList();
                if (options.Count > 0) return options[(int)(h % (uint)options.Count)];
            }

            switch (type)
            {
                case "boolean":
                    return h % 2 == 0;
                case "integer":
                    {
                        long min = schema.TryGetProperty("minimum", out JsonElement mn) && mn.TryGetInt64(out long a) ? a : 0;
                        long max = schema.TryGetProperty("maximum", out JsonElement mx) && mx.TryGetInt64(out long b) ? b : min + 5;
                        if (max < min) max = min;
                        return min + (long)(h % (uint)(max - min + 1));
                    }
                case "number":
                    return Math.Round((h % 1000) / 10.0, 1);
                case "array":
                    return new List<string> { "mock-" + (h % 7), "mock-" + (h % 11) };
                case "object":
                    return new Dictionary<string, object?> { { "mock", true } };
                default:
                    return "mock " + name + " " + (h % 1000).ToString("D3");
            }
        }

        private static uint Hash(string text)
        {
            byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToUInt32(digest, 0);
        }

        #endregion
    }
}
