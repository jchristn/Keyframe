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

With `RequireAuthentication` true (the default), HTTP and WebSocket clients send the Keyframe access key configured in `Keyframe.AccessKey` as `Authorization: Bearer <key>` (or `x-api-key`). The key is compared in constant time. A missing or wrong key gets HTTP 401 before any MCP processing. The MCP server presents the same key to the REST server, so what an agent can do over MCP is exactly what that key can do over REST. No tool needs the admin key.

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

### keyframe_job_get

| Argument | Type | Required |
|---|---|---|
| `JobId` | string | yes |

Returns `JobResponse`: `Status` (`Queued`, `Running`, `Succeeded`, `Failed`, `Canceled`, `Interrupted`), `Progress` (`Stage`, `Completed`, `Total`, `Percent`), `Error`, and timestamps. Polling every second or two is enough.

### keyframe_job_result

| Argument | Type | Required |
|---|---|---|
| `JobId` | string | yes |

Returns the `VideoAnalysisResponse` of a succeeded job: `Results`, `Fields`, `Scenes` (with `StartMs`, `EndMs`, keyframes, per-scene results), `Transcript`, `Properties`, and `Telemetry`. Fails with `409` until the job has succeeded.

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

No arguments. Returns the configured model endpoints with API keys masked, whether each provider is available in this build, and last observed health. Use a `Name` from this list in `Model.Endpoint`.

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
