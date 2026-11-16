# Keyframe MCP API

Version 0.1.0 (ALPHA).

`Keyframe.McpServer` exposes Keyframe to agents over the Model Context Protocol. It is a separate process built on Voltaic 2.4 and holds no analysis logic: every tool calls the Keyframe REST server through `Keyframe.Sdk`, so a tool behaves exactly like the REST route behind it, with the same validation, limits, and errors. [REST_API.md](REST_API.md) is the reference for the shapes the tools return.

## Transports

| Transport | Address (defaults) | Notes |
|---|---|---|
| Streamable HTTP | `http://127.0.0.1:8702/mcp` | What Claude Code, Codex, Gemini CLI, Cursor, and Mux connect to |
| JSON-RPC over HTTP | `http://127.0.0.1:8702/rpc` | Plain request and response, handy for scripts |
| TCP | `127.0.0.1:8703` | Newline-delimited JSON-RPC. No authentication, so keep it on loopback |
| WebSocket | `ws://127.0.0.1:8704/mcp` | |

`GET http://127.0.0.1:8702/` returns `{"status":"Ok"}` without authentication and is the container healthcheck. Clients must run the MCP `initialize` handshake before calling tools; the stateless 2026-07-28 revision is also supported on the HTTP transport.

## Authentication

With `RequireAuthentication` true (the default), HTTP and WebSocket clients authenticate with their own Keyframe key, the same access key or admin key they would use for the REST API, sent as `Authorization: Bearer <key>` (or `x-api-key`). The MCP server checks the key with the Keyframe server (`GET /v1.0/whoami`, cached for 60 seconds) and refuses a missing or unknown key with HTTP 401 before any MCP processing. Every tool call is then made to Keyframe with that caller's key, so a key has exactly the same rights over MCP as over REST: an access key can analyze, probe, cut segments, and read jobs and endpoints, and the admin key can also use `keyframe_analytics_summary` and `keyframe_analytics_list` (with an access key those two return a `403` tool error). `Keyframe.AccessKey` in the settings is only a fallback for requests with no authenticated caller: the TCP transport (which has no authentication, so keep it on loopback) and servers run with `RequireAuthentication` false. `--install` also writes it into client configuration.

## Configuration

`keyframe-mcp.json` (path from `--config=<file>` or `KEYFRAME_MCP_SETTINGS_FILE`; created with defaults when missing):

```json
{
  "Keyframe": { "Endpoint": "http://127.0.0.1:8700", "AccessKey": "default", "TimeoutMs": 600000 },
  "RequireAuthentication": true,
  "Http": { "Enable": true, "Hostname": "127.0.0.1", "Port": 8702 },
  "Tcp": { "Enable": true, "Hostname": "127.0.0.1", "Port": 8703 },
  "WebSocket": { "Enable": true, "Hostname": "127.0.0.1", "Port": 8704 },
  "Logging": { "ConsoleLogging": true, "EnableColors": true, "LogDirectory": "logs/", "LogFilename": "keyframe-mcp.log", "MinimumSeverity": 1 },
  "Telemetry": { "Enable": true, "ServiceName": "keyframe-mcp", "OtlpEnable": false, "OtlpEndpoint": "http://127.0.0.1:4717", "OtlpProtocol": "grpc", "PrometheusEnable": true, "PrometheusHostname": "127.0.0.1", "PrometheusPort": 9465 }
}
```

Environment overrides: `KEYFRAME_URL` (REST base URL), `KEYFRAME_ACCESS_KEY`, `KEYFRAME_MCP_HOSTNAME` (binds HTTP and WebSocket to this host and TCP to the matching address; containers use `*`), and `KEYFRAME_MCP_REQUIRE_AUTHENTICATION` (`false` disables bearer checks).

## Registering with agent harnesses

```bash
cd src/Keyframe.McpServer
dotnet run -c Release -- --install                 # all of: claude, codex, gemini, cursor, mux
dotnet run -c Release -- --install=claude,cursor
dotnet run -c Release -- --install=mux --endpoint=http://127.0.0.1:8702/mcp
dotnet run -c Release -- --uninstall=all
```

`--install` writes a `keyframe` entry into each tool's per-user configuration and leaves every other entry alone. When authentication is required it includes an `Authorization: Bearer <key>` header using `Keyframe.AccessKey` from the settings file, which means the key is stored in those configuration files. The entries look like this:

| Harness | File | Entry |
|---|---|---|
| Claude Code | `~/.claude.json` | `"mcpServers": { "keyframe": { "type": "http", "url": "http://127.0.0.1:8702/mcp", "headers": { "Authorization": "Bearer ..." } } }` |
| Gemini CLI | `~/.gemini/settings.json` | `"mcpServers": { "keyframe": { "httpUrl": "...", "headers": { ... } } }` |
| Cursor | `~/.cursor/mcp.json` | `"mcpServers": { "keyframe": { "url": "...", "headers": { ... } } }` |
| Codex | `~/.codex/config.toml` | `[mcp_servers.keyframe]` with `url` and `http_headers` |
| Mux | `$MUX_CONFIG_DIR/mcp-servers.json` (default `~/.mux`) | `{ "name": "keyframe", "transport": "http", "url": "http://127.0.0.1:8702", "mcpPath": "/mcp", "headers": { ... } }` |

## Results and errors

Every tool returns one text content item holding pretty-printed JSON in Keyframe's PascalCase wire format. A failure is a tool result with `isError: true` whose text says what went wrong, for example:

```text
Keyframe returned 409 Conflict: Job kfj_mwimq9zn_dYavERApEyL453Nkest already finished with status Succeeded.
Keyframe returned 400 BadRequest: The request is invalid. Details: Fields[0].Options is required for Enum fields.
Tool 'keyframe_job_get' arguments is missing required property 'JobId'.
```

Arguments are checked against each tool's input schema before the tool runs, then validated again by the REST server.

## Tools

Media goes in by `Url` whenever possible. Base64 `Data` works, but it inflates payloads by a third and WebSocket messages are capped at 1 MB by default. Private-network URLs are refused by the REST server unless it allows them.

### keyframe_health

Server health: version, uptime, ffmpeg availability, database, job counts, and endpoint health. No arguments. Returns `HealthResponse`. Call it first to confirm the server is reachable.

### keyframe_probe

Deterministic media properties from ffprobe. No model calls.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `Url` | string | one of | Public HTTP or HTTPS URL |
| `Data` | string | one of | Base64 bytes |

Returns `MediaProperties` (kind, content type, size, hash, dimensions, duration, frame rate, codecs, audio, tags).

### keyframe_image_analyze

Summarizes an image and extracts typed fields. Synchronous.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `Image` | object | yes | `{ "Url": "..." }` or `{ "Data": "<base64>", "MediaType": "image/jpeg" }` |
| `Context` | string or array | no | Strings or `{ "Label", "Text" }` objects, applied to every prompt. At most 50 items |
| `Summarize` | object | no | `Enable`, `Key` (default `Summary`), `MaxWords` (5 to 2000), `Style` (`Paragraph`, `Bullets`, `Caption`), `Instructions` |
| `Fields` | array | no | Each `{ "Key", "Type", "Prompt" }` plus optional `Options` (required for `Enum`), `Minimum`, `Maximum`, `MaxLength`, `MaxItems`, `Required` |
| `Mode` | string | no | `Combined` (default) or `PerField` |
| `Model` | object | no | `{ "Endpoint": "<name from keyframe_endpoints_list>", "Name": "<model override>" }` |

Field types are `Boolean`, `String`, `Integer`, `Number`, `Enum`, `StringList`, and `Object`. Supplying `Fields` turns extraction on. With neither `Summarize` nor `Fields`, the tool produces a default summary.

```json
{
  "Image": { "Url": "https://upload.wikimedia.org/wikipedia/commons/3/3a/Cat03.jpg" },
  "Context": [ "Shelter intake photo.", { "Label": "Location", "Text": "Front office" } ],
  "Summarize": { "Enable": true, "MaxWords": 40, "Style": "Caption" },
  "Fields": [
    { "Key": "hasCats", "Type": "Boolean", "Prompt": "Does this image have cats?" },
    { "Key": "catCount", "Type": "Integer", "Prompt": "How many cats are visible?", "Minimum": 0 },
    { "Key": "setting", "Type": "Enum", "Prompt": "Where was this taken?", "Options": [ "Indoors", "Outdoors" ] }
  ]
}
```

Returns `ImageAnalysisResponse`: `Results` keyed by the summary key and field keys, `Fields` with per-field validity and warnings, `Properties`, and `Telemetry` (stage timings, every model call, token totals).

### keyframe_video_analyze

Analyzes a video. By default it queues a job and returns `JobResponse`; poll `keyframe_job_get`, then read `keyframe_job_result`. With `Wait` true it returns the full `VideoAnalysisResponse` when the job finishes in time, or the job when it does not.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `Video` | object | yes | `Url` or `Data`, as above |
| `Context`, `Summarize`, `Fields`, `Mode`, `Model` | | no | Whole-video functions, as in `keyframe_image_analyze` |
| `Scenes` | object | no | `Enable`, `FramesPerScene` (1 to 10), `Summarize`, `Extract` (`{ "Enable", "Fields" }`), `Detection` (`Threshold`, `MinSceneMs`, `MaxSceneMs`, `MaxScenes`) |
| `Transcribe` | object | no | `Enable`, `Language` (ISO 639-1; omit to detect) |
| `TextModel` | object | no | Text-only endpoint for the whole-video summary and fields, computed from scene results and transcript |
| `Wait` | boolean | no | Default false |
| `WaitTimeoutMs` | integer | no | Default 120000, capped by the server |

When `Scenes` and `Transcribe` are both off and neither `Summarize` nor `Fields` is given, the tool produces a default whole-video summary.

```json
{
  "Video": { "Url": "https://download.samplelib.com/mp4/sample-5s.mp4" },
  "Scenes": { "Enable": true, "Summarize": { "Enable": true, "Style": "Caption" } },
  "Transcribe": { "Enable": true },
  "Summarize": { "Enable": true, "MaxWords": 120 }
}
```

### keyframe_audio_analyze

Analyzes an audio file (mp3, wav, flac, m4a or aac, ogg or opus, webm audio) or the audio track of a video file. The recording is transcribed, then the summary and fields run on the transcript with the text model. By default it queues a job and returns `JobResponse` (`MediaKind` `Audio`); poll `keyframe_job_get`, then read `keyframe_job_result`. With `Wait` true it returns the full `AudioAnalysisResponse` when the job finishes in time, or the job when it does not.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `Audio` | object | yes | `Url` or `Data`, as above |
| `Context`, `Summarize`, `Fields`, `Mode` | | no | As in `keyframe_image_analyze`, applied to the transcript |
| `Transcribe` | object | no | `Enable` (default true; required for `Summarize` and `Fields`), `Language` (ISO 639-1; omit to detect), `Prompt` (vocabulary hints) |
| `TranscriptionModel` | object | no | `{ "Endpoint", "Name" }` of a transcription endpoint. Default `Inference.DefaultTranscription` |
| `TextModel` | object | no | Text endpoint for the summary and fields. Default `Inference.DefaultText` |
| `Wait` | boolean | no | Default false |
| `WaitTimeoutMs` | integer | no | Default 120000, capped by the server |

Defaults match the REST route: transcription and the summary are on, extraction is off until `Fields` is supplied.

```json
{
  "Audio": { "Url": "https://example.com/meeting.mp3" },
  "Transcribe": { "Language": "en" },
  "Summarize": { "Enable": true, "Style": "Bullets", "MaxWords": 120 },
  "Fields": [ { "Key": "speakerCount", "Type": "Integer", "Prompt": "How many distinct speakers are there?" } ],
  "Wait": true
}
```

The result has `Transcript` (`Language`, `Text`, `Segments` with `StartMs` and `EndMs`), `Results`, `Fields`, `Properties`, and `Telemetry` (including `QueueWaitMs` and, per model call, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond`, and `QueueMs`).

### keyframe_segment

Cuts a time range out of a video or audio source and returns it as a `SegmentResponse` with the media in base64 `Data` (the tool always uses the REST route's `Json` response form). Use `StartMs` and `EndMs` from `Scenes` or `Transcript.Segments` of an analysis result. The source is resolved again from `Url` or `Data`; Keyframe does not keep uploaded media.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `Source` | object | yes | `Url` or `Data`, as above |
| `StartMs` | integer | yes | 0 or more. For `Frame` output, the frame time |
| `EndMs` | integer | for Video and Audio | Must exceed `StartMs`; values past the end are clamped. Ignored for `Frame` |
| `Output` | string | no | `Video` (default), `Audio`, or `Frame` |
| `Format` | string | no | `Video`: `mp4` (default) or `webm`. `Audio`: `mp3` (default), `wav`, `flac`, `m4a`, or `ogg`. `Frame`: `jpg` (default) or `png` |
| `MaxEdge` | integer | no | Long-edge cap in pixels for video and frame output, 16 to 4096 |

```json
{ "Source": { "Url": "https://example.com/talk.mp4" }, "StartMs": 12000, "EndMs": 18500, "Output": "Audio" }
```

Returns `MediaType`, `Filename` (for example `segment-12000-18500.mp3`, or `segment-12000.jpg` for a frame), `StartMs`, `EndMs` (after clamping), `DurationMs` (0 for frames), `SizeBytes`, and `Data`. Segments longer than `Media.MaxSegmentMs` (default 10 minutes) and an invalid range or format fail with `400`; a source without the needed stream (audio for `Audio`, video for `Video` and `Frame`) or a `StartMs` past the end fails with `422`. The base64 payload grows with the clip, so keep ranges short and set `MaxEdge` on MCP transports.

### keyframe_job_get

| Argument | Type | Required |
|---|---|---|
| `JobId` | string | yes |

Returns `JobResponse`: `Status` (`Queued`, `Running`, `Succeeded`, `Failed`, `Canceled`, `Interrupted`), `Progress` (`Stage`, `Completed`, `Total`, `Percent`), `Error`, and timestamps. Polling every second or two is enough.

### keyframe_job_result

| Argument | Type | Required |
|---|---|---|
| `JobId` | string | yes |

Returns the result of a succeeded job. For a video job, the `VideoAnalysisResponse`: `Results`, `Fields`, `Scenes` (with `StartMs`, `EndMs`, keyframes, per-scene results), `Transcript`, `Properties`, and `Telemetry`. For an audio job, the `AudioAnalysisResponse`: the same members without `Scenes`. Fails with `409` until the job has succeeded.

### keyframe_job_list

| Argument | Type | Required | Notes |
|---|---|---|---|
| `Status` | string | no | One of the job statuses |
| `PageNumber` | integer | no | From 1 |
| `PageSize` | integer | no | 1 to 100, default 25 |

Returns `EnumerationResult<JobResponse>`, newest first.

### keyframe_job_cancel

| Argument | Type | Required |
|---|---|---|
| `JobId` | string | yes |

Cancels a queued or running job and returns it. Fails with `409` when the job already finished.

### keyframe_endpoints_list

No arguments. Returns the configured model endpoints with API keys masked, whether each provider is available in this build, its capabilities (`SupportsImages`, `SupportsStructuredOutput`, `SupportsTranscription`), and last observed health. Use a `Name` from this list in `Model.Endpoint`.

### keyframe_analytics_summary

Requires connecting with the admin key (see Authentication). Summarizes stored analyses: `Totals`, `ByMediaKind` (`Image`, `Video`, and `Audio` are always present), gap-filled `Buckets`, `ByModel`, `ByStage`, and `ByPurpose`. Every timing (`AnalysisMs`, `QueueWaitMs`, `ModelCallMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond`, `RealtimeFactor`, and more) is a distribution with `Count`, `Min`, `Max`, `Avg`, `P50`, `P90`, `P95`, `P99`, `StdDev`, and `Sum`, or null without samples.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `FromUtc` | string | no | ISO 8601, inclusive. Default 24 hours before `ToUtc` |
| `ToUtc` | string | no | ISO 8601, exclusive. Default now |
| `Bucket` | string | no | `Minute`, `Hour`, or `Day`. Default `Minute` up to 2 hours, `Hour` up to 7 days, otherwise `Day` |
| `MediaKind` | string | no | `Image`, `Video`, or `Audio` |
| `Endpoint` | string | no | Endpoint name |
| `Model` | string | no | Model name |

Returns `AnalyticsSummary` (the `Bucket` member is lowercase). A range with too many buckets fails with `400`.

### keyframe_analytics_list

Requires connecting with the admin key (see Authentication). Lists stored analyses, newest first.

| Argument | Type | Required | Notes |
|---|---|---|---|
| `MediaKind` | string | no | `Image`, `Video`, or `Audio` |
| `Success` | boolean | no | |
| `FromUtc`, `ToUtc` | string | no | ISO 8601 bounds on completion time |
| `MaxResults` | integer | no | 1 to 1000, default 50 |
| `Skip` | integer | no | Default 0 |

Returns `EnumerationResult<AnalysisRecord>`. Each record (`Id` prefix `kfa_`, the same as the analysis response `Id`) carries `AnalysisMs`, `QueueWaitMs`, `MediaDurationMs`, `RealtimeFactor`, the endpoints and models used, model call, retry, repair, and token counts, and `Stages`. `Calls` is null in listings; the REST route `GET /v1.0/analytics/analyses/{id}` returns the model call attempts.

## Telemetry

The MCP server exports the `Keyframe.Mcp` meter (`keyframe_mcp_tool_calls_total{tool, outcome}` and `keyframe_mcp_tool_duration_seconds{tool}`, where `outcome` is `success`, `api_error`, `invalid_arguments`, `canceled`, or `error`) and Voltaic's own meter and activity source (per-request and per-tool durations, sessions, authentication denials). Prometheus scrapes `http://127.0.0.1:9465/metrics` by default; set `Telemetry.OtlpEnable` to export to a collector.

## Calling a tool by hand

```bash
curl -s http://127.0.0.1:8702/mcp \
  -H "Authorization: Bearer default" -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" -H "MCP-Protocol-Version: 2025-11-25" \
  -D headers.txt \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"curl","version":"1"}}}'
# Send the Mcp-Session-Id from headers.txt on every later request:
#   notifications/initialized, then tools/list, then tools/call
#   {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"keyframe_health","arguments":{}}}
```
