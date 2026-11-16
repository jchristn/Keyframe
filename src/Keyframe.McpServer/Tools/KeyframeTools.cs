namespace Keyframe.McpServer.Tools
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Diagnostics.Metrics;
    using System.Threading;
    using System.Threading.Tasks;
    using Keyframe.McpServer.Tools.Inputs;
    using Keyframe.Sdk;
    using Keyframe.Sdk.Enums;
    using Keyframe.Sdk.Requests;
    using Keyframe.Sdk.Responses;
    using Keyframe.Sdk.Serialization;
    using Voltaic.Core;
    using Voltaic.Mcp;

    /// <summary>
    /// The Keyframe MCP tools. Every tool calls the Keyframe REST server through Keyframe.Sdk, so MCP and REST
    /// behave identically. Results are returned as pretty JSON text. Failures surface as tool errors
    /// (isError: true) whose text names the HTTP status and Keyframe error.
    /// </summary>
    public class KeyframeTools
    {
        #region Public-Members

        /// <summary>
        /// Meter name for MCP tool metrics.
        /// </summary>
        public const string MeterName = "Keyframe.Mcp";

        /// <summary>
        /// Tool names, in registration order.
        /// </summary>
        public static readonly string[] ToolNames = new string[]
        {
            "keyframe_health", "keyframe_probe", "keyframe_image_analyze", "keyframe_video_analyze",
            "keyframe_job_get", "keyframe_job_result", "keyframe_job_list", "keyframe_job_cancel", "keyframe_endpoints_list"
        };

        #endregion

        #region Private-Members

        private static readonly Meter _Meter = new Meter(MeterName);
        private static readonly Counter<long> _Calls = _Meter.CreateCounter<long>("keyframe.mcp.tool.calls", "{call}", "MCP tool calls.");
        private static readonly Histogram<double> _Duration = _Meter.CreateHistogram<double>("keyframe.mcp.tool.duration", "s", "MCP tool call duration.");

        private readonly KeyframeClient _Client;
        private readonly Action<string>? _Log;

        #endregion

        #region Constructors-and-Factories

        /// <summary>
        /// Instantiate.
        /// </summary>
        /// <param name="client">Keyframe SDK client.</param>
        /// <param name="log">Optional debug logger.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public KeyframeTools(KeyframeClient client, Action<string>? log = null)
        {
            _Client = client ?? throw new ArgumentNullException(nameof(client));
            _Log = log;
        }

        #endregion

        #region Public-Methods

        /// <summary>
        /// Register every tool.
        /// </summary>
        /// <param name="register">Registration delegate for one transport.</param>
        /// <exception cref="ArgumentNullException">Thrown when register is null.</exception>
        public void RegisterAll(RegisterToolDelegate register)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));

            register("keyframe_health",
                "Keyframe server health: version, uptime, ffmpeg availability, database, job counts, and model endpoint health. Call first to confirm the server is reachable.",
                new { type = "object", properties = new { } },
                (args, token) => Run("keyframe_health", async () => (object)await _Client.Health.GetAsync(token).ConfigureAwait(false)));

            register("keyframe_probe",
                "Deterministic media properties from ffprobe (kind, dimensions, duration, frame rate, codecs, audio, tags). No model calls. Supply Url (preferred) or base64 Data.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        Url = new { type = "string", description = "Public HTTP or HTTPS URL of the image or video." },
                        Data = new { type = "string", description = "Base64 media bytes. Avoid large payloads on MCP transports." }
                    }
                },
                (args, token) => Run("keyframe_probe", async () =>
                {
                    ProbeInput input = Parse<ProbeInput>(args);
                    if (String.IsNullOrWhiteSpace(input.Url) && String.IsNullOrWhiteSpace(input.Data)) throw new McpToolException("Supply Url or Data.");
                    return await _Client.Probe.ProbeAsync(new MediaSource { Url = input.Url, Data = input.Data }, token).ConfigureAwait(false);
                }));

            register("keyframe_image_analyze",
                "Summarize an image and extract typed fields. Fields have a Key, a Type (Boolean, String, Integer, Number, Enum, StringList, Object), and a Prompt; "
                + "Enum fields need Options. Results come back keyed by the summary key and field keys, with per-field validity, media properties, and telemetry (timings, model calls, tokens). "
                + "Context items (strings or {Label, Text}) are applied to every question. With neither Summarize nor Fields, a default summary is produced.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        Image = MediaSchema("The image."),
                        Context = ContextSchema(),
                        Summarize = SummarizeSchema(),
                        Fields = FieldsSchema(),
                        Mode = new { type = "string", @enum = new[] { "Combined", "PerField" }, description = "Combined (one call, default) or PerField (one call per field)." },
                        Model = ModelSchema()
                    },
                    required = new[] { "Image" }
                },
                (args, token) => Run("keyframe_image_analyze", async () =>
                {
                    ImageAnalyzeInput input = Parse<ImageAnalyzeInput>(args);
                    ImageAnalysisRequest request = new ImageAnalysisRequest
                    {
                        Image = input.Image,
                        Context = input.Context,
                        Model = input.Model
                    };

                    ApplyFunctions(request.Functions.Summarize, request.Functions.Extract, input.Summarize, input.Fields, input.Mode, true);
                    return await _Client.Image.AnalyzeAsync(request, token).ConfigureAwait(false);
                }));

            register("keyframe_video_analyze",
                "Analyze a video: scene breakdown with per-scene summaries and fields, a transcript, and whole-video summary and fields. Starts a background job and returns it "
                + "(poll keyframe_job_get, then keyframe_job_result). Set Wait to true for short videos to get the result directly. Context and Fields work as in keyframe_image_analyze.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        Video = MediaSchema("The video."),
                        Context = ContextSchema(),
                        Summarize = SummarizeSchema(),
                        Fields = FieldsSchema(),
                        Mode = new { type = "string", @enum = new[] { "Combined", "PerField" } },
                        Scenes = new
                        {
                            type = "object",
                            description = "Scene breakdown. Boundaries come from ffmpeg; models describe scenes.",
                            properties = new
                            {
                                Enable = new { type = "boolean" },
                                FramesPerScene = new { type = "integer", description = "1 to 10, default 3." },
                                Summarize = SummarizeSchema(),
                                Extract = new { type = "object", properties = new { Enable = new { type = "boolean" }, Fields = FieldsSchema() } }
                            }
                        },
                        Transcribe = new
                        {
                            type = "object",
                            properties = new
                            {
                                Enable = new { type = "boolean" },
                                Language = new { type = "string", description = "ISO 639-1 code; omit to detect." }
                            }
                        },
                        Model = ModelSchema(),
                        TextModel = ModelSchema(),
                        Wait = new { type = "boolean", description = "Wait for the result. Default false." },
                        WaitTimeoutMs = new { type = "integer", description = "Wait timeout, default 120000." }
                    },
                    required = new[] { "Video" }
                },
                (args, token) => Run("keyframe_video_analyze", async () =>
                {
                    VideoAnalyzeInput input = Parse<VideoAnalyzeInput>(args);
                    VideoAnalysisRequest request = new VideoAnalysisRequest
                    {
                        Video = input.Video,
                        Context = input.Context,
                        Model = input.Model,
                        TextModel = input.TextModel,
                        Wait = input.Wait
                    };

                    if (input.WaitTimeoutMs != null) request.WaitTimeoutMs = input.WaitTimeoutMs.Value;
                    if (input.Scenes != null) request.Functions.Scenes = input.Scenes;
                    if (input.Transcribe != null) request.Functions.Transcribe = input.Transcribe;
                    bool otherWork = request.Functions.Scenes.Enable || request.Functions.Transcribe.Enable;
                    ApplyFunctions(request.Functions.Summarize, request.Functions.Extract, input.Summarize, input.Fields, input.Mode, !otherWork);

                    VideoSubmitResult submitted = await _Client.Video.AnalyzeAsync(request, token).ConfigureAwait(false);
                    if (submitted.Result != null) return submitted.Result;
                    return (object?)submitted.Job ?? new { submitted.JobId, submitted.StatusCode };
                }));

            register("keyframe_job_get",
                "Status and progress of a video job: Status (Queued, Running, Succeeded, Failed, Canceled, Interrupted), Progress {Stage, Completed, Total, Percent}, and Error.",
                JobIdSchema(),
                (args, token) => Run("keyframe_job_get", async () => (object)await _Client.Jobs.GetAsync(RequireJobId(args), token).ConfigureAwait(false)));

            register("keyframe_job_result",
                "The full result of a succeeded video job: Results, Fields, Scenes, Transcript, Properties, and Telemetry. Fails with 409 until the job has succeeded.",
                JobIdSchema(),
                (args, token) => Run("keyframe_job_result", async () => (object)await _Client.Jobs.GetResultAsync(RequireJobId(args), token).ConfigureAwait(false)));

            register("keyframe_job_list",
                "List jobs, newest first.",
                new
                {
                    type = "object",
                    properties = new
                    {
                        Status = new { type = "string", @enum = Enum.GetNames(typeof(JobStatusEnum)) },
                        PageNumber = new { type = "integer", description = "From 1, default 1." },
                        PageSize = new { type = "integer", description = "1 to 100, default 25." }
                    }
                },
                (args, token) => Run("keyframe_job_list", async () =>
                {
                    JobListInput input = Parse<JobListInput>(args);
                    JobFilter filter = new JobFilter
                    {
                        PageNumber = Math.Max(1, input.PageNumber ?? 1),
                        PageSize = Math.Clamp(input.PageSize ?? 25, 1, 100)
                    };

                    if (!String.IsNullOrWhiteSpace(input.Status))
                    {
                        JobStatusEnum status;
                        if (!Enum.TryParse<JobStatusEnum>(input.Status, true, out status)) throw new McpToolException("Unknown Status '" + input.Status + "'.");
                        filter.Status = status;
                    }

                    return await _Client.Jobs.ListAsync(filter, token).ConfigureAwait(false);
                }));

            register("keyframe_job_cancel",
                "Cancel a queued or running video job. Fails with 409 when the job already finished.",
                JobIdSchema(),
                (args, token) => Run("keyframe_job_cancel", async () => (object)await _Client.Jobs.CancelAsync(RequireJobId(args), token).ConfigureAwait(false)));

            register("keyframe_endpoints_list",
                "Configured model endpoints (API keys masked) with provider availability and health. Use an endpoint Name in Model.Endpoint to choose it.",
                new { type = "object", properties = new { } },
                (args, token) => Run("keyframe_endpoints_list", async () => (object)await _Client.Endpoints.ListAsync(token).ConfigureAwait(false)));
        }

        #endregion

        #region Private-Methods

        private async Task<object> Run(string tool, Func<Task<object>> action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            string outcome = "success";
            try
            {
                object result = await action().ConfigureAwait(false);
                return KeyframeSerializer.Serialize(result, true);
            }
            catch (KeyframeApiException ex)
            {
                outcome = "api_error";
                string details = ex.ApiError != null && ex.ApiError.Details.Count > 0 ? " Details: " + String.Join(" ", ex.ApiError.Details) : String.Empty;
                throw new McpToolException("Keyframe returned " + ex.StatusCode + " " + (ex.Error?.ToString() ?? String.Empty) + ": " + (ex.ApiError?.Message ?? ex.Message) + details);
            }
            catch (McpToolException)
            {
                outcome = "invalid_arguments";
                throw;
            }
            catch (System.Text.Json.JsonException ex)
            {
                outcome = "invalid_arguments";
                throw new McpToolException("The arguments do not match the tool schema: " + ex.Message);
            }
            catch (ArgumentException ex)
            {
                outcome = "invalid_arguments";
                throw new McpToolException(ex.Message);
            }
            catch (OperationCanceledException)
            {
                outcome = "canceled";
                throw;
            }
            catch (Exception ex)
            {
                outcome = "error";
                _Log?.Invoke("[KeyframeTools] " + tool + " failed: " + ex.Message);
                throw new McpToolException("Keyframe could not be reached or failed: " + ex.Message);
            }
            finally
            {
                KeyValuePair<string, object?>[] tags = new KeyValuePair<string, object?>[]
                {
                    new KeyValuePair<string, object?>("tool", tool),
                    new KeyValuePair<string, object?>("outcome", outcome)
                };
                _Calls.Add(1, tags);
                _Duration.Record(sw.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("tool", tool));
                _Log?.Invoke("[KeyframeTools] " + tool + " " + outcome + " in " + sw.ElapsedMilliseconds + "ms");
            }
        }

        private static T Parse<T>(RpcParameters? args) where T : class, new()
        {
            if (args == null || !args.HasValue || String.IsNullOrWhiteSpace(args.RawJson)) return new T();
            return KeyframeSerializer.Deserialize<T>(args.RawJson) ?? new T();
        }

        private static string RequireJobId(RpcParameters? args)
        {
            JobIdInput input = Parse<JobIdInput>(args);
            if (String.IsNullOrWhiteSpace(input.JobId)) throw new McpToolException("JobId is required.");
            return input.JobId;
        }

        private static void ApplyFunctions(SummarizeSpec target, ExtractSpec extract, SummarizeSpec? summarize, List<FieldSpec>? fields, ExtractModeEnum? mode, bool defaultSummary)
        {
            bool hasFields = fields != null && fields.Count > 0;
            if (summarize != null)
            {
                target.Enable = summarize.Enable;
                target.Key = summarize.Key;
                target.MaxWords = summarize.MaxWords;
                target.Style = summarize.Style;
                target.Instructions = summarize.Instructions;
            }
            else if (!hasFields && defaultSummary)
            {
                target.Enable = true;
            }

            if (hasFields)
            {
                extract.Enable = true;
                extract.Fields = fields!;
                if (mode != null) extract.Mode = mode.Value;
            }
        }

        private static object MediaSchema(string description)
        {
            return new
            {
                type = "object",
                description = description + " Supply Url (preferred) or base64 Data.",
                properties = new
                {
                    Url = new { type = "string", description = "Public HTTP or HTTPS URL. Private-network URLs are refused by default." },
                    Data = new { type = "string", description = "Base64 bytes." },
                    MediaType = new { type = "string", description = "Optional MIME type." }
                }
            };
        }

        private static object ContextSchema()
        {
            return new
            {
                description = "Background applied to every prompt: a string, an array of strings, or an array of {Label, Text} objects. At most 50 items.",
                oneOf = new object[]
                {
                    new { type = "string" },
                    new
                    {
                        type = "array",
                        items = new
                        {
                            oneOf = new object[]
                            {
                                new { type = "string" },
                                new { type = "object", properties = new { Label = new { type = "string" }, Text = new { type = "string" } }, required = new[] { "Text" } }
                            }
                        }
                    }
                }
            };
        }

        private static object SummarizeSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    Enable = new { type = "boolean" },
                    Key = new { type = "string", description = "Result key, default Summary." },
                    MaxWords = new { type = "integer", description = "5 to 2000, default 100." },
                    Style = new { type = "string", @enum = new[] { "Paragraph", "Bullets", "Caption" } },
                    Instructions = new { type = "string" }
                }
            };
        }

        private static object FieldsSchema()
        {
            return new
            {
                type = "array",
                description = "Fields to extract.",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        Key = new { type = "string", description = "1 to 64 characters: letters, digits, underscore, hyphen, period." },
                        Type = new { type = "string", @enum = new[] { "Boolean", "String", "Integer", "Number", "Enum", "StringList", "Object" } },
                        Prompt = new { type = "string" },
                        Options = new { type = "array", items = new { type = "string" }, description = "Required for Enum." },
                        Minimum = new { type = "number" },
                        Maximum = new { type = "number" },
                        MaxLength = new { type = "integer" },
                        MaxItems = new { type = "integer" },
                        Required = new { type = "boolean" }
                    },
                    required = new[] { "Key", "Type", "Prompt" }
                }
            };
        }

        private static object ModelSchema()
        {
            return new
            {
                type = "object",
                description = "Endpoint (a Name from keyframe_endpoints_list) and optional model Name override.",
                properties = new
                {
                    Endpoint = new { type = "string" },
                    Name = new { type = "string" }
                }
            };
        }

        private static object JobIdSchema()
        {
            return new
            {
                type = "object",
                properties = new { JobId = new { type = "string", description = "Job identifier (kfj_ prefix)." } },
                required = new[] { "JobId" }
            };
        }

        #endregion
    }
}
