# Keyframe Implementation Plan

Keyframe is a metadata generation and extraction platform for multimedia. A caller posts an image or a video (as a URL or as base64 data), supplies optional context, and asks for one or more functions: a summary, a dictionary of typed metadata fields answered by a model, and for video, a scene breakdown with per-scene summaries and fields. Every response carries detailed telemetry so the caller can see where the time and tokens went.

This document is the working plan. It is written so a developer can open it cold, find the current phase, and keep going. Update the status markers and the progress log at the bottom as work lands.

Status markers used below: `[x]` done, `[~]` in progress or partially done, `[ ]` not started.

---

## 1. Decisions and Constraints

These decisions were made with the product owner before planning began. They override defaults in the requirement documents where noted, and each deviation is recorded here so reviewers know it is intentional.

| # | Decision | Notes |
|---|----------|-------|
| D1 | **Model access goes through PolyPrompt.** | PolyPrompt 4.0.0 has no image, video, or audio input. The required library work is specified in `~/Code/PolyPrompt/MULTIMEDIA_SUPPORT_PLAN.md`. Keyframe codes against a small internal seam (`IInferenceClient`, `ITranscriptionClient`) so it compiles and tests today with a deterministic mock, and gains a PolyPrompt-backed implementation when that plan lands. No provider wire code lives in Keyframe. |
| D2 | **Authentication is DocumentAtom-style, not full AAA.** | This is an explicit exception to `AUTHENTICATION.md` and the multi-tenancy rules in `BACKEND_ARCHITECTURE.md`. There are no tenants, users, roles, permissions, sessions, or audit tables. Access is controlled by named access keys in the settings file plus one admin API key. See section 9. |
| D3 | **Video analysis includes speech-to-text.** | Audio is extracted with ffmpeg and transcribed through an OpenAI-compatible `/audio/transcriptions` endpoint (OpenAI, or local Whisper servers such as faster-whisper or speaches). Transcription is optional per request. Timestamped segments feed scene summaries. Reached through PolyPrompt's planned `Transcribe` capability. |
| D4 | **The per-item extraction spec is called `Fields`.** | `"Extract": { "Fields": [ { "Key": ..., "Type": ..., "Prompt": ... } ] }`. |
| D5 | **JSON contracts use PascalCase.** | Matches DocumentAtom and Partio (`Settings`, `Data`, `Chunks`). Deserialization is case-insensitive, so camelCase input is accepted, but output is always PascalCase. |
| D6 | **Initial version is `v0.1.0`, labeled ALPHA.** | Confirmed by the owner. `src/Directory.Build.props` holds `0.1.0`; the README shows `v0.1.0 - ALPHA` under `assets/logo-dark.png`. Per `VERSIONING.md`, agents do not change it without an explicit request. |
| D7 | **Ports.** | Server `8700`, dashboard `8701`, MCP server `8702` (HTTP), `8703` (TCP), `8704` (WebSocket). Observability is offset so Keyframe can run beside other stacks: Grafana `3700`, Prometheus `9700`, Tempo `3710`, OTLP collector `4717` (gRPC) and `4718` (HTTP). |
| D9 | **Branding assets.** | The README uses `assets/logo-dark.png`. The favicon everywhere (server `GET /favicon.ico` and the dashboard `index.html`) is `assets/glyph.ico`, copied into the server output and `dashboard/public/` at build time rather than duplicated by hand. `assets/glyph.png` is the NuGet package icon. |
| D8 | **MCP server on Voltaic, documented in `MCP_API.md`.** | Requested by the owner. Mirrors DocumentAtom: a separate `Keyframe.McpServer` process built on Voltaic that fronts the REST server through the C# SDK, offers HTTP, TCP, and WebSocket transports, and can register itself with Claude Code, Codex, Gemini, Cursor, and Mux (`--install`). See section 11a. The REST surface is documented in `REST_API.md`; both documents are kept in sync with their surfaces. |
| D10 | **Context is a list applied to every prompt.** | Requested by the owner. `Context` is an array of items (`Label`, `Text`) included in the evidence block of every prompt a request produces: the summary, every field (in both extract modes), every scene, and the whole-video step. For convenience it also accepts a plain string or an array of strings. |

### Requirements compliance summary

| Requirement document | How Keyframe satisfies it |
|---|---|
| `REPOSITORY_REQUIREMENTS.md` | All root files, `src/` `dashboard/` `sdk/` layout, `.yaml` compose files, healthchecks with cURL on `127.0.0.1`, `docker/update.bat`, `docker/factory/reset.bat`, `REST_API.md`, Postman collection under `assets/postman/`, GitHub Actions workflow, `AOT support` heading in README. |
| `BACKEND_ARCHITECTURE.md` | Watson 7 hosting with pre and post authentication route tables, route registrars per feature, thin `Program.cs`, typed DTOs, PrettyId identifiers, four database providers, migrations, request history at `/v1.0/api/request-history`, settings management API with restart, `UseOpenApi()`. **Exception:** no tenant or user dimension (D2). |
| `AUTHENTICATION.md` | **Exception (D2).** Retained pieces: constant-time key comparison, secrets redacted from logs and request history, auth outcome metrics, 401 vs 403 semantics, rate limiting on failed auth. |
| `BACKEND_TEST_ARCHITECTURE.md` | Touchstone `Test.Shared`, `Test.Automated`, `Test.Xunit3`, `Test.Nunit`. Full suite in CI on every push. Server is not AOT capable (Watson reflection and ffmpeg process hosting), recorded in README. |
| `CODE_STYLE.md` | Enforced throughout and repeated in `CLAUDE.md` for agents. |
| `TELEMETRY_REQUIREMENTS.md` | Watson 7 telemetry on, Radiant collector exporting OTLP, `keyframe_*` application metrics, spans per stage and per model call, Prometheus + Tempo + Loki + Grafana in compose, per-domain Grafana dashboards, External Services card on the dashboard home page. |
| `FRONTEND_ARCHITECTURE.md`, `DASHBOARD_STYLE_AND_USABILITY.md`, `I18N.md` | React 19 + Vite 6, fetch-based `ApiClient`, i18next, hand-rolled SVG charts, full route inventory (section 12). |
| `WRITING_DOCUMENTS.md` | No em-dashes anywhere. Documentation reviewed for human voice. |
| MCP (`REPOSITORY_REQUIREMENTS.md` item 14) | `Keyframe.McpServer` on Voltaic, every tool documented in `MCP_API.md` with name, purpose, input schema, result shape, and examples. |
| `BENCHMARKING.md`, `SIMULATED_USER_TESTING.md`, `INSTALLERS.md` | Deferred to Phase 11 and later. A benchmark harness is valuable here (model accuracy on field extraction), but it should follow a working v0.1. |

---

## 2. What Keyframe Does

DocumentAtom breaks documents into atoms. Keyframe applies the same idea to media: it breaks an image or a video into analyzable units, runs cheap deterministic analysis first, and spends model calls only where the caller asked for them.

For an **image**, Keyframe:

1. Resolves the input (downloads the URL with limits, or decodes base64).
2. Detects the media type from magic bytes and probes it with `ffprobe` (dimensions, format, codec, tags).
3. Normalizes it for the model (re-encodes to JPEG or PNG and caps the longest edge, configurable).
4. Runs the requested functions: `Summarize` and `Extract`.
5. Returns `Results` (a key-value dictionary using the caller's keys), per-field detail, media properties, and telemetry.

For a **video**, Keyframe:

1. Resolves and probes the input (duration, frame rate, resolution, codecs, audio presence).
2. Detects scene boundaries with ffmpeg's scene-change score, then enforces minimum and maximum scene lengths.
3. Extracts N representative keyframes per scene.
4. Optionally extracts the audio track and transcribes it with timestamps.
5. Per scene: summarizes and answers per-scene fields using that scene's keyframes plus its slice of the transcript.
6. For the whole video: summarizes from the scene summaries plus the transcript (a text-only map-reduce step), and answers whole-video fields from a sampled keyframe set plus scene summaries plus transcript.
7. Returns the same shape as images, plus a `Scenes` array with time ranges.

Video runs as an asynchronous job by default because a long video can take minutes. Callers poll the job or pass `"Wait": true` for short clips.

---

## 3. Repository Layout

```text
Keyframe/
|-- .github/workflows/test.yaml
|-- archive/                      (pre-existing; untouched)
|-- assets/
|   |-- logo-dark.png, logo-light.png (README and dashboard branding)
|   |-- glyph.ico (favicon for the server and dashboard), glyph.png (package icon)
|   |-- postman/Keyframe.postman_collection.json
|   `-- grafana/                  (dashboard JSON, source of truth)
|-- dashboard/                    (React 19 + Vite 6)
|-- docker/
|   |-- compose.yaml
|   |-- keyframe.json
|   |-- update.bat, update.sh
|   |-- prometheus.yaml, tempo.yaml, otel-collector.yaml, loki.yaml
|   |-- grafana/provisioning/{datasources,dashboards}/
|   `-- factory/
|       |-- reset.bat, reset.sh
|       |-- keyframe.json         (seeded factory settings)
|       `-- samples/              (small sample image and video for demos)
|-- sdk/
|   |-- csharp/  (Keyframe.Sdk + Test.Automated harness + README)
|   |-- js/      (keyframe-sdk + test harness + README)
|   `-- python/  (keyframe_sdk + test harness + README)
|-- src/
|   |-- Directory.Build.props
|   |-- Keyframe.sln
|   |-- Keyframe.Core/
|   |-- Keyframe.Server/
|   |-- Keyframe.McpServer/
|   |-- Test.Shared/
|   |-- Test.Automated/
|   |-- Test.Xunit3/
|   `-- Test.Nunit/
|-- .gitignore, .dockerignore
|-- CLAUDE.md
|-- README.md, DOCKERHUB_README.md, CHANGELOG.md, LICENSE.md
|-- REST_API.md, MCP_API.md, TELEMETRY.md
`-- KEYFRAME_PLAN.md              (this file)
```

### Keyframe.Core layout

```text
Keyframe.Core/
|-- Constants.cs
|-- Analysis/          ImageAnalyzer, VideoAnalyzer, SummarizeFunction, ExtractFunction,
|                      SceneFunction, PromptBuilder, FieldCoercer, AnalysisContext
|-- Database/          DatabaseDriverBase, DatabaseDriverFactory, DatabaseSettings,
|                      DatabaseTypeEnum, SchemaMigration, Interfaces/, Sqlite/, Mysql/,
|                      Postgresql/, SqlServer/ (each with Implementations/ and Queries/)
|-- Enums/             MediaTypeEnum, FieldTypeEnum, JobStatusEnum, ExtractModeEnum, ...
|-- Exceptions/        KeyframeException, MediaResolutionException, MediaProcessingException,
|                      InferenceException, FieldValidationException
|-- Helpers/           IdGenerator, MediaTypeDetector, HashHelper, UrlGuard
|-- Inference/         IInferenceClient, ITranscriptionClient, InferenceRequest,
|                      InferenceResponse, InferenceImage, TranscriptionRequest,
|                      TranscriptionResponse, TranscriptSegment, MockInferenceClient,
|                      MockTranscriptionClient, InferenceClientFactory
|-- Media/             FfmpegRunner, MediaProber, FrameExtractor, SceneDetector,
|                      AudioExtractor, ImageNormalizer, MediaResolver, MediaWorkspace
|-- Models/            MediaProperties, VideoStreamProperties, AudioStreamProperties,
|                      Scene, Keyframe (frame), FieldResult, Job, RequestHistoryEntry
|-- Observability/     KeyframeMetrics, KeyframeTelemetry (ActivitySource), StageTimer
|-- Requests/          ImageAnalysisRequest, VideoAnalysisRequest, MediaSource,
|                      FunctionsSpec, SummarizeSpec, ExtractSpec, FieldSpec, ScenesSpec,
|                      SceneDetectionSpec, TranscribeSpec, ModelSelection, RequestHistoryFilter
|-- Responses/         ImageAnalysisResponse, VideoAnalysisResponse, SceneResult,
|                      AnalysisTelemetry, StageTiming, ModelCallTelemetry, TokenTotals,
|                      JobResponse, EnumerationResult<T>, ApiErrorResponse, RequestHistorySummary
`-- Settings/          KeyframeSettings, RestSettings, AuthSettings, AccessKeySettings,
                       InferenceSettings, EndpointSettings, MediaSettings, JobSettings,
                       RequestHistorySettings, LoggingSettings, TelemetrySettings
```

### Keyframe.Server layout

```text
Keyframe.Server/
|-- Program.cs                 (thin: Bootstrapper.Run(args))
|-- Bootstrapper.cs            (settings, logging, telemetry, database, services, host)
|-- KeyframeServer.cs          (Watson host: preflight, post-routing, auth hook, registrars)
|-- SettingsManager.cs         (load, env overrides, validate, mask, persist, restart metadata)
|-- Classes/                   RequestContext, AuthResult, SettingsFieldMetadata
|-- Routes/                    HealthRoutes, ImageRoutes, VideoRoutes, JobRoutes,
|                              EndpointRoutes, SettingsRoutes, RequestHistoryRoutes, RootRoutes
|-- Services/                  AuthenticationService, AnalysisService, JobService,
|                              JobWorker, RequestHistoryCaptureService,
|                              RequestHistoryCleanupService, EndpointHealthService
|-- Dockerfile
`-- keyframe.json              (developer default settings)
```

---

## 4. API Contract

All routes are versioned under `/v1.0/`. Request history follows the requirement path `/v1.0/api/request-history`. Health, the root page, and `/openapi.json` are anonymous. Everything else requires an access key (section 9).

### 4.1 Route table

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `HEAD`/`GET` | `/` | none | Liveness and root page |
| `GET` | `/v1.0/health` | none | JSON health: version, uptime, ffmpeg availability, endpoint health |
| `GET` | `/openapi.json`, `/swagger` | none | OpenAPI document and UI |
| `GET` | `/v1.0/whoami` | key | Resolved principal (key name, admin flag) |
| `POST` | `/v1.0/image` | key | Analyze one image (synchronous) |
| `POST` | `/v1.0/video` | key | Analyze one video (job by default, `Wait` for synchronous) |
| `POST` | `/v1.0/audio` | key | Analyze audio or a video's audio track (job by default, `Wait` for synchronous) |
| `POST` | `/v1.0/segment` | key | Cut a video clip, audio clip, or frame out of a source |
| `POST` | `/v1.0/probe` | key | Deterministic media properties only, no model calls |
| `GET` | `/v1.0/jobs` | key | List jobs (paged, filter by status, type, time range) |
| `GET` | `/v1.0/jobs/{id}` | key | Job status and progress |
| `GET` | `/v1.0/jobs/{id}/result` | key | Final result (`VideoAnalysisResponse`) |
| `POST` | `/v1.0/jobs/{id}/cancel` | key | Cancel a queued or running job |
| `DELETE` | `/v1.0/jobs/{id}` | key | Delete a finished job and its result |
| `GET` | `/v1.0/endpoints` | key | Configured model endpoints (secrets masked) with health |
| `POST` | `/v1.0/endpoints/{name}/test` | admin | Connectivity probe through the inference layer |
| `GET` | `/v1.0/settings` | admin | Settings (masked) plus field metadata (live vs restart, env override) |
| `PUT` | `/v1.0/settings` | admin | Validate, clamp, persist, apply live where possible |
| `POST` | `/v1.0/restart` | admin | Clean exit so the container restarts |
| `GET` | `/v1.0/api/request-history` | key | Paged list, bodies omitted |
| `GET` | `/v1.0/api/request-history/summary` | key | Bucketed counts (gap-filled server side) |
| `GET` | `/v1.0/api/request-history/{id}` | key | Full entry with headers and bodies |
| `DELETE` | `/v1.0/api/request-history/{id}` | admin | Delete one entry |
| `DELETE` | `/v1.0/api/request-history` | admin | Bulk delete by filter, returns `DeletedCount` |
| `GET` | `/v1.0/analytics/summary` | admin | Percentiles, gap-filled buckets, by media kind, model, stage, purpose |
| `GET` | `/v1.0/analytics/analyses` | admin | Stored analyses, paged |
| `GET` | `/v1.0/analytics/analyses/{id}` | admin | One analysis with every model call attempt |
| `DELETE` | `/v1.0/analytics/analyses/{id}` | admin | Delete one analysis |
| `DELETE` | `/v1.0/analytics/analyses` | admin | Delete analyses older than a cutoff |

### 4.2 Image request

```json
POST /v1.0/image
{
  "Image": {
    "Url": "https://example.com/photos/backyard.jpg",
    "Data": null,
    "MediaType": null
  },
  "Context": [
    { "Label": "Source", "Text": "Photos submitted by pet adoption volunteers." },
    { "Label": "Location", "Text": "Shelter intake yard, Austin, Texas." },
    { "Text": "Animals wearing orange collars are already adopted." }
  ],
  "Model": {
    "Endpoint": "local-ollama",
    "Name": "qwen3-vl:8b"
  },
  "Functions": {
    "Summarize": {
      "Enable": true,
      "Key": "Summary",
      "MaxWords": 60,
      "Style": "Paragraph",
      "Instructions": "Mention the setting and any animals."
    },
    "Extract": {
      "Enable": true,
      "Mode": "Combined",
      "Fields": [
        { "Key": "hasCats", "Type": "Boolean", "Prompt": "Does this image have cats?" },
        { "Key": "dogCount", "Type": "Integer", "Prompt": "How many dogs do you see in this image?", "Minimum": 0 },
        { "Key": "setting", "Type": "Enum", "Prompt": "Where was this taken?", "Options": [ "Indoors", "Outdoors", "Unknown" ] },
        { "Key": "colors", "Type": "StringList", "Prompt": "List the dominant colors.", "MaxItems": 5 }
      ]
    }
  },
  "IncludeProperties": true
}
```

Rules:

- Exactly one of `Url` or `Data` (base64) is required. `MediaType` is optional; detection uses magic bytes and falls back to the declared type.
- `Context` is optional background shared by every question in the request. It is an array of items, each with an optional `Label` (up to 128 characters) and a required `Text`. At most 50 items and 20000 characters in total. A plain string or an array of strings is also accepted and normalized to items. Every item is included in the fenced `<context>` block of every prompt the request produces: the summary, every field in both `Combined` and `PerField` modes, and for video every scene and the whole-video step. Context is treated as data, never as instructions.
- `Model` is optional. When omitted, the endpoint marked `DefaultVision` in settings is used. `Name` overrides the endpoint's default model for this call.
- `Summarize.Key` defaults to `Summary`. `Style` is `Paragraph`, `Bullets`, or `Caption`. `MaxWords` is clamped 5 to 2000 (default 100).
- `Extract.Mode` is `Combined` (one model call that returns a JSON object holding every field, the default) or `PerField` (one call per field, run in parallel up to the endpoint's concurrency limit). `PerField` costs more but isolates failures and gives per-prompt timings for each field.
- Field `Key` must be unique, 1 to 64 characters, letters, digits, underscore, or hyphen. `Key` values may not collide with the summary key.

### 4.3 Field types

| `Type` | JSON output | Optional constraints | Coercion notes |
|---|---|---|---|
| `Boolean` | `true` / `false` | none | Accepts `yes`/`no`, `true`/`false`, `1`/`0` from the model |
| `String` | string | `MaxLength` | Trimmed; truncated to `MaxLength` with a warning |
| `Integer` | integer | `Minimum`, `Maximum` | Accepts numeric strings and words "zero" to "twenty"; clamped with a warning |
| `Number` | number | `Minimum`, `Maximum` | Invariant culture parsing |
| `Enum` | string | `Options` (required) | Case-insensitive match to an option; unknown values become `null` with an error |
| `StringList` | array of strings | `MaxItems` | Accepts a JSON array or a comma-separated string |
| `Object` | object | `Schema` (JSON Schema, optional) | Passed through when it parses; validated against `Schema` when supplied |

Each field may also set `Required` (default `false`) and `Default` (returned when the model cannot answer). When a field cannot be coerced, its `Results` value is `null` (or `Default`) and `Fields[]` carries the error.

### 4.4 Image response

```json
{
  "Id": "kfa_2XbC9...",
  "Success": true,
  "MediaType": "Image",
  "Properties": {
    "ContentType": "image/jpeg",
    "Format": "mjpeg",
    "Width": 4032,
    "Height": 3024,
    "SizeBytes": 2231840,
    "Sha256": "9f2c...",
    "Tags": { "Make": "Apple", "Model": "iPhone 15" }
  },
  "Results": {
    "Summary": "A fenced backyard lawn with two dogs playing near a red ball...",
    "hasCats": false,
    "dogCount": 2,
    "setting": "Outdoors",
    "colors": [ "green", "brown", "red" ]
  },
  "Fields": [
    { "Key": "hasCats", "Type": "Boolean", "Value": false, "Valid": true, "RawValue": "false", "Error": null, "Warnings": [] },
    { "Key": "dogCount", "Type": "Integer", "Value": 2, "Valid": true, "RawValue": "2", "Error": null, "Warnings": [] }
  ],
  "Telemetry": {
    "StartUtc": "2026-11-15T18:01:02.114Z",
    "EndUtc": "2026-11-15T18:01:05.402Z",
    "TotalMs": 3288.4,
    "Stages": [
      { "Name": "Resolve", "DurationMs": 412.0 },
      { "Name": "Probe", "DurationMs": 38.2 },
      { "Name": "Normalize", "DurationMs": 61.7 },
      { "Name": "Summarize", "DurationMs": 1720.3 },
      { "Name": "Extract", "DurationMs": 1049.1 }
    ],
    "ModelCalls": [
      {
        "Purpose": "Summarize",
        "Target": null,
        "Endpoint": "local-ollama",
        "Provider": "Ollama",
        "Model": "qwen3-vl:8b",
        "StartUtc": "2026-11-15T18:01:02.626Z",
        "DurationMs": 1720.3,
        "TimeToFirstTokenMs": null,
        "Attempts": 1,
        "Success": true,
        "Error": null,
        "InputImages": 1,
        "InputImageBytes": 184211,
        "PromptTokens": 812,
        "CompletionTokens": 71,
        "TotalTokens": 883,
        "CachedTokens": null,
        "ReasoningTokens": null,
        "FinishReason": "stop"
      }
    ],
    "Totals": {
      "ModelCalls": 2,
      "FailedModelCalls": 0,
      "ModelMs": 2769.4,
      "PromptTokens": 1611,
      "CompletionTokens": 118,
      "TotalTokens": 1729
    }
  },
  "Warnings": [],
  "Errors": []
}
```

`Success` is `true` when every enabled function produced a result. A partial failure (for example one field that would not coerce) keeps `Success = true` with details in `Fields[]` and `Warnings`. A model call that fails outright after retries sets `Success = false`, lists the error in `Errors`, and still returns whatever completed. HTTP status is 200 for a completed analysis (including partial results), 400 for an invalid request, 401/403 for auth, 422 for undecodable media, 502 when the model endpoint is unreachable, 504 on timeout.

### 4.5 Video request

```json
POST /v1.0/video
{
  "Video": { "Url": "https://example.com/talks/keynote.mp4" },
  "Context": [
    { "Label": "Event", "Text": "Conference keynote recordings." },
    { "Label": "Speakers", "Text": "The keynote host is Dana Ruiz; guests vary." }
  ],
  "Model": { "Endpoint": "local-ollama" },
  "TextModel": { "Endpoint": "local-ollama", "Name": "gpt-oss:20b" },
  "Wait": false,
  "WaitTimeoutMs": 120000,
  "Functions": {
    "Summarize": { "Enable": true, "Key": "Summary", "MaxWords": 200 },
    "Extract": {
      "Enable": true,
      "Fields": [
        { "Key": "speakerCount", "Type": "Integer", "Prompt": "How many distinct people speak on stage?" },
        { "Key": "topics", "Type": "StringList", "Prompt": "List the main topics covered.", "MaxItems": 8 }
      ]
    },
    "Scenes": {
      "Enable": true,
      "Detection": {
        "Threshold": 0.30,
        "MinSceneMs": 2000,
        "MaxSceneMs": 60000,
        "MaxScenes": 200
      },
      "FramesPerScene": 3,
      "Summarize": { "Enable": true, "MaxWords": 40 },
      "Extract": {
        "Enable": true,
        "Fields": [
          { "Key": "showsSlide", "Type": "Boolean", "Prompt": "Is a presentation slide visible?" },
          { "Key": "onScreenText", "Type": "String", "Prompt": "Transcribe any prominent on-screen text.", "MaxLength": 300 }
        ]
      }
    },
    "Transcribe": { "Enable": true, "Endpoint": "local-whisper", "Language": "en" }
  }
}
```

Rules:

- `Model` selects the vision model (frames). `TextModel` selects the model for text-only steps (video-level summary reduce, transcript-only reasoning). It defaults to the `DefaultText` endpoint, then to the vision endpoint.
- `Scenes.Detection.Threshold` is ffmpeg's scene score (0.0 to 1.0, default 0.30; lower finds more cuts). Scenes shorter than `MinSceneMs` are merged into a neighbor; scenes longer than `MaxSceneMs` are split evenly. `MaxScenes` caps cost; when exceeded, the threshold is raised and detection reruns, then remaining scenes are merged.
- `FramesPerScene` is clamped 1 to 10 (default 3). Frames are taken at evenly spaced offsets inside the scene, avoiding the first and last 5 percent where transitions live.
- When `Scenes.Enable` is false but `Summarize` or `Extract` is enabled, Keyframe still samples frames across the whole video (`SampleFrames`, default 8) to answer them.
- Transcription is skipped automatically when the video has no audio stream, with a warning.

### 4.6 Video job and response

`POST /v1.0/video` returns `202 Accepted` with a `JobResponse` unless `Wait` is true and the job finishes inside `WaitTimeoutMs`, in which case it returns `200` with the full result.

```json
{
  "Id": "kfj_2XbD1...",
  "Status": "Running",
  "MediaType": "Video",
  "CreatedUtc": "...",
  "StartedUtc": "...",
  "CompletedUtc": null,
  "Progress": { "Stage": "Scenes", "Completed": 12, "Total": 37, "Percent": 41.2 },
  "Error": null
}
```

Job statuses: `Queued`, `Running`, `Succeeded`, `Failed`, `Canceled`, `Interrupted` (the server restarted mid-job).

The final `VideoAnalysisResponse` matches the image response and adds:

```json
{
  "Properties": {
    "DurationMs": 1834210, "Width": 1920, "Height": 1080, "FrameRate": 29.97,
    "VideoCodec": "h264", "HasAudio": true, "AudioCodec": "aac", "AudioChannels": 2
  },
  "Transcript": {
    "Language": "en",
    "Text": "...",
    "Segments": [ { "StartMs": 0, "EndMs": 4200, "Text": "Good morning everyone." } ]
  },
  "Scenes": [
    {
      "Index": 0,
      "StartMs": 0,
      "EndMs": 14266,
      "DurationMs": 14266,
      "Keyframes": [ { "TimestampMs": 2140, "Sha256": "..." } ],
      "TranscriptText": "Good morning everyone...",
      "Results": { "Summary": "A speaker walks on stage...", "showsSlide": false, "onScreenText": "" },
      "Fields": [ ]
    }
  ]
}
```

Telemetry for video adds stages `Probe`, `SceneDetection`, `FrameExtraction`, `AudioExtraction`, `Transcribe`, `Scenes`, `Reduce`, and each `ModelCalls[]` entry carries `Purpose` (`SceneSummarize`, `SceneExtract`, `Summarize`, `Extract`, `Transcribe`) and `Target` (scene index or field key).

Keyframe images can optionally be returned inline (`"IncludeKeyframeData": true`, base64 JPEG thumbnails capped by `Media.ThumbnailMaxEdge`), which the dashboard uses for the scene timeline.

---

## 5. Media Pipeline

ffmpeg and ffprobe are required on the host (the Docker image installs them). Keyframe invokes them as child processes through `FfmpegRunner`, which handles argument building, timeouts, cancellation (kills the process tree), stderr capture, and exit-code mapping to `MediaProcessingException`. No ffmpeg wrapper package is used, which keeps dependencies small and behavior explicit.

| Component | Responsibility | Implementation notes |
|---|---|---|
| `MediaResolver` | URL or base64 to a file in a per-request `MediaWorkspace` | Streams downloads with `Media.MaxDownloadBytes`, `Media.DownloadTimeoutMs`. `UrlGuard` blocks loopback, link-local, and private ranges unless `Media.AllowPrivateNetworkUrls` is true (SSRF protection). Allowed schemes `http`, `https`. |
| `MediaTypeDetector` | Magic-byte detection | JPEG, PNG, GIF, WebP, BMP, TIFF, HEIC (ftyp heic/heix/mif1), MP4/MOV (ftyp), MKV/WebM (EBML), AVI (RIFF AVI), MPEG-TS. Rejects mismatches between route and detected type with 422. |
| `MediaProber` | `ffprobe -v error -print_format json -show_format -show_streams` | Deserialized into typed probe DTOs (never JSON DOM). Produces `MediaProperties`. |
| `ImageNormalizer` | Model-ready image bytes | `ffmpeg -i in -vf scale='min(iw,EDGE)':'min(ih,EDGE)':force_original_aspect_ratio=decrease -q:v 3 out.jpg`. Default edge 1568, PNG preserved when alpha matters. Animated GIF and WebP use the first frame. |
| `SceneDetector` | Scene boundaries | `ffmpeg -i in -filter:v "select='gt(scene,T)',showinfo" -f null -` and parse `pts_time` from stderr. Then apply min/max/merge rules. Videos with zero detected cuts become one scene (then split by `MaxSceneMs`). |
| `FrameExtractor` | Keyframes at timestamps | One `ffmpeg -ss t -i in -frames:v 1` per frame (fast seek), scaled like `ImageNormalizer`. Extraction runs with bounded parallelism (`Media.MaxParallelFfmpeg`). |
| `AudioExtractor` | Transcription input | `ffmpeg -i in -vn -ac 1 -ar 16000 -c:a flac out.flac` (16 kHz mono). Splits into chunks under the endpoint's upload limit (`Transcription.MaxChunkSeconds`, default 600) with timestamp offsets reapplied. |
| `MediaWorkspace` | Temp directory per request or job | `IDisposable`; deletes on dispose. Root from `Media.TempDirectory`. Orphaned workspaces older than a day are cleaned at startup. |

---

## 6. Inference Layer

### 6.1 The seam

```csharp
public interface IInferenceClient
{
    string EndpointName { get; }
    Task<InferenceResponse> CompleteAsync(InferenceRequest request, CancellationToken token = default);
}

public interface ITranscriptionClient
{
    string EndpointName { get; }
    Task<TranscriptionResponse> TranscribeAsync(TranscriptionRequest request, CancellationToken token = default);
}
```

`InferenceRequest` carries `SystemPrompt`, `UserPrompt`, `Images` (bytes plus media type), `ResponseFormat` (`Text` or `JsonObject` with an optional JSON Schema string), `MaxOutputTokens`, `Temperature`, and `Model` override. `InferenceResponse` carries `Success`, `Text`, `Error`, `StatusCode`, `FinishReason`, token counts (prompt, completion, total, cached, reasoning), `TimeToFirstTokenMs`, `DurationMs`, `Provider`, and `Model`. These map one-to-one onto PolyPrompt's planned multimodal `ChatAsync` and `TranscribeAsync` surfaces.

Implementations:

- `MockInferenceClient` and `MockTranscriptionClient` (provider `Mock`): deterministic, offline, used by tests and by the factory demo stack. They answer from a scripted table keyed by field key and type, and report synthetic token counts so telemetry paths are exercised end to end.
- `PolyPromptInferenceClient` and `PolyPromptTranscriptionClient`: built in Phase 6 once PolyPrompt ships multimodal support. They construct one PolyPrompt client per configured endpoint (`OllamaClient`, `OpenAiClient`, `OpenAiCompatibleClient`, `AnthropicClient`, `GeminiClient`, `BedrockClient`, and so on), translate `InferenceRequest` into a multimodal `ChatMessage`, and copy PolyPrompt's `TokenUsage` and call details into `InferenceResponse`.

`InferenceClientFactory` builds clients from `InferenceSettings.Endpoints`, caches them, and enforces a per-endpoint `SemaphoreSlim` sized by `MaxConcurrentRequests`.

### 6.2 Prompt design

Prompts are built by `PromptBuilder` and kept in one place so they can be tuned and tested.

- **System prompt** states the role (media analyst), the output contract, and that caller `Context` is background data.
- **Summarize** asks for a summary in the requested `Style` and `MaxWords`, grounded only in what is visible (or audible, for video transcripts), with an instruction to say "unclear" rather than guess.
- **Extract, Combined mode** sends one prompt listing each field with its key, type, constraints, and prompt, and asks for a single JSON object keyed by field key. `ResponseFormat = JsonObject` with a generated JSON Schema when the provider supports structured output; otherwise the schema is described in text and the response is parsed leniently (first JSON object in the text, code fences stripped).
- **Extract, PerField mode** sends one prompt per field asking for `{"value": ...}`.
- **Scene prompts** include the scene time range and the transcript slice for that range.
- **Video reduce** is text only: scene summaries in order with timestamps, plus the transcript (truncated to `Inference.MaxTranscriptCharacters` with head and tail kept), producing the video summary and whole-video fields.

### 6.3 Reliability

- JSON parse or coercion failure triggers one repair attempt (configurable `Inference.MaxRepairAttempts`, default 1) that sends the model its previous output and the parser error.
- Transient HTTP failures (429, 5xx, timeouts) retry with exponential backoff up to `Inference.MaxRetries` (default 2). Every attempt is counted in `ModelCallTelemetry.Attempts`.
- Per-call timeout `Endpoint.TimeoutMs` (default 120000), overall request timeout `Inference.RequestTimeoutMs` for synchronous image calls.
- Endpoint health is probed in the background (`EndpointHealthService`) and unhealthy endpoints fail fast with 502 rather than hanging.

---

## 7. Telemetry

There are two layers, and they are kept separate on purpose. Per-request telemetry goes back to the caller in the response body. Operational telemetry goes to the observability stack.

### 7.1 Response telemetry

`AnalysisTelemetry` is described in section 4.4. It is assembled by `StageTimer` and an `AnalysisContext` that collects `ModelCallTelemetry` from every inference and transcription call, including failed attempts. Totals are computed at the end. Clock values use `Stopwatch` for durations and UTC timestamps for start and end.

### 7.2 Operational telemetry

- Watson 7 built-in telemetry enabled (`Settings.Telemetry.Enable`), with the `Watson` meter and activity source subscribed by a Radiant host that exports OTLP to the collector.
- Application meter and activity source named `Keyframe`. Metric families (all low cardinality):
  - `keyframe_analysis_total{media_type, outcome}`
  - `keyframe_analysis_duration_seconds{media_type}`
  - `keyframe_stage_duration_seconds{media_type, stage}`
  - `keyframe_model_calls_total{purpose, provider, outcome}`
  - `keyframe_model_call_duration_seconds{purpose, provider}`
  - `keyframe_model_tokens_total{provider, kind}` (`kind` = prompt, completion)
  - `keyframe_ffmpeg_invocations_total{operation, outcome}` and `keyframe_ffmpeg_duration_seconds{operation}`
  - `keyframe_jobs_total{outcome}`, `keyframe_jobs_active`, `keyframe_jobs_queued`
  - `keyframe_auth_requests_total{result}`
  - `keyframe_endpoint_healthy{endpoint}` (endpoint names are operator-defined and few)
- Spans: `analysis:image`, `analysis:video`, `stage:<Name>`, `inference <purpose>`, `ffmpeg <operation>`, `job <type>`. Attributes include job id, endpoint, provider, model, token counts, and media type. Spans set error status on caught failures.
- Logs: SyslogLogging to console and file, plus Loki through the collector because video jobs are background work.
- Grafana folder `Keyframe` with dashboards: Overview, HTTP, Analysis (per-stage p95, outcomes), Models (calls, latency, tokens by provider), Jobs, Media (ffmpeg).

---

## 8. Persistence

Keyframe follows the four-provider database architecture even without tenants, because request history and durable jobs need it.

| Table | Purpose | Key columns |
|---|---|---|
| `schema_migrations` | Migration tracking | `version`, `description`, `appliedutc` |
| `requesthistory` | Required request capture | Per `BACKEND_ARCHITECTURE.md` with `principalname` (access key name) instead of tenant and user ids |
| `requesthistoryheaders` | Child table for captured headers | `entryid`, `direction` (Request/Response), `name`, `value` |
| `jobs` | Video jobs | `id`, `mediatype`, `status`, `principalname`, `sourceurl`, `sourcesha256`, `sizebytes`, `durationms`, `progressstage`, `progresscompleted`, `progresstotal`, `error`, `createdutc`, `startedutc`, `completedutc`, `lastupdateutc`, `active` |
| `jobresults` | Job result storage | `jobid`, `resultjson`, `resultbytes`, `createdutc` |

`jobresults.resultjson` is an **unmanaged JSON column, by explicit exception**: the result contains caller-defined field keys and types (`Results` is a schemaless dictionary by design), so it cannot map to fixed columns. Searchable and lifecycle fields (status, timestamps, media type, principal, sizes) live in typed columns on `jobs`. The request body that started the job is retained only in request history. Job retention is `Jobs.RetentionDays` (default 7) and is pruned with request history.

Providers: `Sqlite` (default, `SemaphoreSlim` write serialization), `Mysql`, `Postgresql`, `SqlServer`, each with `Implementations/` and `Queries/`, handwritten SQL, and idempotent migrations.

---

## 9. Authentication (DocumentAtom-style exception)

```json
"Auth": {
  "RequireAuthentication": true,
  "AdminApiKey": "keyframeadmin",
  "AccessKeys": [
    { "Name": "default", "Key": "default", "Active": true }
  ],
  "MaxFailedAttemptsPerMinute": 30
}
```

- Data-plane routes accept `Authorization: Bearer <key>` or `x-api-key: <key>`. Either an access key or the admin key works.
- Admin routes (settings, restart, endpoint test, request history deletion) require the admin key.
- Comparison is constant time. Keys are never logged or returned; settings responses show them as set or not set plus the last four characters.
- A request with no credential gets 401; a non-admin key on an admin route gets 403.
- Failed attempts are rate limited per source IP (429 when exceeded).
- `RequireAuthentication = false` is allowed for local development only and logs a startup warning.
- Environment overrides: `KEYFRAME_ADMIN_API_KEY`, `KEYFRAME_ACCESS_KEY` (adds or replaces the `default` key).

The `RequestContext` carries `IsAuthenticated`, `IsAdmin`, `PrincipalName`, `RequestId`, and timestamps. The fields that the requirement documents tie to tenants and users are omitted by design.

---

## 10. Settings File

`keyframe.json` (path override `KEYFRAME_SETTINGS_FILE`):

```json
{
  "Rest": { "Hostname": "127.0.0.1", "Port": 8700, "Ssl": false, "MaxRequestBodyBytes": 268435456 },
  "Auth": { "...": "see section 9" },
  "Database": { "Type": "Sqlite", "Filename": "data/keyframe.db", "Hostname": null, "Port": 0, "DatabaseName": null, "Username": null, "Password": null },
  "Inference": {
    "DefaultVision": "mock",
    "DefaultText": null,
    "DefaultTranscription": "mock-stt",
    "MaxRetries": 2,
    "MaxRepairAttempts": 1,
    "RequestTimeoutMs": 300000,
    "MaxTranscriptCharacters": 60000,
    "Endpoints": [
      { "Name": "mock", "Provider": "Mock", "Endpoint": null, "ApiKey": null, "Model": "mock-vision", "MaxConcurrentRequests": 4, "TimeoutMs": 120000, "Active": true },
      { "Name": "mock-stt", "Provider": "Mock", "Kind": "Transcription", "Model": "mock-whisper", "Active": true },
      { "Name": "local-ollama", "Provider": "Ollama", "Endpoint": "http://127.0.0.1:11434", "Model": "qwen3-vl:8b", "MaxConcurrentRequests": 2, "Active": false }
    ]
  },
  "Media": {
    "TempDirectory": "temp",
    "FfmpegPath": "ffmpeg",
    "FfprobePath": "ffprobe",
    "MaxDownloadBytes": 2147483648,
    "DownloadTimeoutMs": 300000,
    "AllowPrivateNetworkUrls": false,
    "ImageMaxEdge": 1568,
    "ThumbnailMaxEdge": 320,
    "MaxParallelFfmpeg": 4,
    "MaxVideoDurationMs": 14400000
  },
  "Jobs": { "MaxConcurrentJobs": 2, "MaxQueuedJobs": 100, "RetentionDays": 7 },
  "RequestHistory": { "Enabled": true, "MaxRequestBodyBytes": 65536, "MaxResponseBodyBytes": 65536, "RetentionDays": 30 },
  "Logging": { "ConsoleLogging": true, "EnableColors": true, "LogDirectory": "logs/", "LogFilename": "keyframe.log", "MinimumSeverity": 0, "Servers": [] },
  "Telemetry": { "Enable": true, "ServiceName": "keyframe-server", "OtlpEndpoint": "http://127.0.0.1:4717", "ExposePrometheus": true }
}
```

Every numeric setting is clamped in its setter, and the settings API publishes per-field metadata: `ApplyMode` (`Live` or `Restart`) and `OverriddenByEnvironment`. Restart-required groups: `Rest`, `Database`, `Telemetry`, `Logging.LogDirectory`. Live: `Inference`, `Media` (except paths), `Jobs` limits, `RequestHistory`, `Auth` keys.

---

## 11a. MCP Server

`src/Keyframe.McpServer` is a standalone process built on **Voltaic** (latest 2.x), matching DocumentAtom's `DocumentAtom.McpServer` and Partio's `partio-mcp`. It holds no analysis logic: every tool calls the REST server through `Keyframe.Sdk`, so REST and MCP behave identically and share one authorization path.

| Item | Detail |
|---|---|
| Transports | HTTP JSON-RPC at `/rpc` (port 8702), TCP (port 8703), WebSocket at `/mcp` (port 8704) |
| Configuration | `keyframe-mcp.json`: Keyframe server endpoint (`http://127.0.0.1:8700`), access key, transport ports, logging |
| Auth | Bearer token on the MCP HTTP transport, checked against the configured access key; the MCP server forwards its own key to REST |
| Install | `--install[=targets]` and `--uninstall` register the server with Claude Code, Codex, Gemini, Cursor, and Mux, as DocumentAtom does |
| Errors | Tool failures return MCP results with `isError: true`, per the current MCP specification |
| Telemetry | Voltaic JSON-RPC metrics plus `keyframe_mcp_tool_calls_total{tool, outcome}` and per-tool duration |

Tools (names use underscores, as DocumentAtom 3.3 does):

| Tool | Purpose | Key inputs |
|---|---|---|
| `keyframe_health` | Server health, version, ffmpeg and endpoint status | none |
| `keyframe_probe` | Deterministic media properties | `Url` or `Data` |
| `keyframe_image_analyze` | Summarize and extract fields from an image | `Image`, `Context[]`, `Summarize`, `Fields[]`, `Mode`, `Model` |
| `keyframe_video_analyze` | Start a video job (or wait for a short one) | `Video`, `Context[]`, `Summarize`, `Fields[]`, `Scenes`, `Transcribe`, `Wait` |
| `keyframe_job_get` | Job status and progress | `JobId` |
| `keyframe_job_result` | Completed job result | `JobId` |
| `keyframe_job_list` | Enumerate jobs | `Status`, `PageNumber`, `PageSize` |
| `keyframe_job_cancel` | Cancel a job | `JobId` |
| `keyframe_endpoints_list` | Configured model endpoints and health | none |

Agents usually pass media by URL; base64 is supported but large payloads are discouraged on MCP transports. `MCP_API.md` documents each tool's input schema (types, required flags), result shape, and examples, plus the per-harness setup snippets.

## 11. SDKs

Each SDK lives in `sdk/{language}` with its own README and a thorough test harness that runs against a server (CI starts the server with the mock provider). Base URLs default to `http://127.0.0.1:8700`.

| SDK | Package | Surface |
|---|---|---|
| C# | `Keyframe.Sdk` (net8.0, net10.0) | `KeyframeClient` with `Image.AnalyzeAsync`, `Video.AnalyzeAsync`, `Video.AnalyzeAndWaitAsync` (polls), `Jobs.*`, `Probe`, `Endpoints`, `RequestHistory`, `Settings`, `Health`. Typed models mirror Core DTOs. Uses RestWrapper. |
| JavaScript | `keyframe-sdk` (ESM, Node 20+ and browser) | Same surface with fetch, `analyzeImageFile(path)` helper for Node. |
| Python | `keyframe_sdk` (3.10+) | Same surface with `httpx`, dataclass models, `analyze_video_and_wait`. |

---

## 12. Dashboard

React 19, Vite 6, React Router 7, i18next. Shared `ApiClient` with a fetch core. Hand-rolled SVG charts. Dark and light themes. Grouped sidebar.

### Route inventory

| Route | User job | Role(s) | Backend resources | Table/filter needs | Actions and modals | Empty/error states |
|---|---|---|---|---|---|---|
| `/login` | Connect to a server | All | `GET /v1.0/whoami` | none | Server URL plus key, remembered URL | Server unreachable, bad key |
| `/dashboard/home` | See what Keyframe is doing | All | health, request history summary, jobs list, endpoints | Range selector | CTAs: Analyze image, Analyze video, Open API Explorer; External Services card | No traffic yet, endpoint unhealthy, ffmpeg missing |
| `/dashboard/analyze/image` | Run an image analysis interactively | All | `POST /v1.0/image`, endpoints | none | Upload or URL, field builder (add, reorder, type, constraints), preset save to local storage, result tabs (Results, Fields, Telemetry waterfall, Raw JSON), copy as cURL or SDK snippet | Validation errors inline, model failure with retry guidance |
| `/dashboard/analyze/video` | Run a video analysis | All | `POST /v1.0/video`, jobs | none | Same builder plus Scenes and Transcribe options, submits a job and routes to the job detail | Job rejected, queue full |
| `/dashboard/jobs` | Track video jobs | All | `GET /v1.0/jobs` | Status, time range, pagination, sort | View, cancel, delete (confirm modal), view JSON | No jobs, filter no match |
| `/dashboard/jobs/:id` | Inspect one job | All | job, job result | Scene table | Scene timeline with thumbnails, per-scene results, transcript viewer, telemetry waterfall, download JSON | Running (live progress), failed (error detail), interrupted |
| `/dashboard/endpoints` | See model endpoints and health | All (test: admin) | `GET /v1.0/endpoints`, test | Kind filter | Test connectivity, view JSON; edit through Settings | No endpoints configured, unhealthy |
| `/dashboard/request-history` | Investigate traffic | All (delete: admin) | request history list, summary, detail, delete | Method, status, path, time range, pagination | Inspector modal with headers and bodies, delete, bulk clear | No retained traffic |
| `/dashboard/api-explorer` | Execute live API calls | All | `/openapi.json` | Endpoint search | Execute, copy, destructive confirm, response tabs | OpenAPI missing |
| `/dashboard/settings` | Edit server settings | Admin | `GET/PUT /v1.0/settings`, restart | Grouped form sections | Save, restart (confirm), restart-required and env-overridden badges | Not admin (read-only notice) |
| `/dashboard/server` | Server info | All | health, whoami | none | Copy server URLs | none |

Navigation groups: **Analyze** (Image, Video), **Operate** (Jobs, Endpoints), **Observability** (Home, Request History, API Explorer), **System** (Settings, Server Info). Home sits at the top as the landing page.

---

## 13. Docker and Factory

- `src/Keyframe.Server/Dockerfile`: multi-stage .NET 10 build, runtime image installs `ffmpeg` and `curl`, runs as non-root, `HEALTHCHECK` equivalent in compose.
- `dashboard/Dockerfile`: Node build, nginx runtime, `curl` installed.
- `docker/compose.yaml`: `keyframe-server`, `keyframe-dashboard`, `otel-collector`, `prometheus`, `tempo`, `loki`, `grafana`. Healthchecks `interval: 5s`, `retries: 2`, `timeout: 3s`, probing `127.0.0.1`. `depends_on` with `condition: service_healthy`. `restart: unless-stopped` on the server. Settings file and data directory mounted as volumes. Images pinned (Tempo `2.6.1`).
- An optional `ollama` service under a compose profile (`--profile ollama`) for fully local vision models.
- `docker/update.bat` and `update.sh`: `docker compose pull`, `down`, `up -d`, `ps -a`.
- `docker/factory/`: `reset.bat`/`reset.sh` stop the stack, delete volumes and data, restore `keyframe.json` from the factory copy, and bring the stack up with the mock provider so the dashboard shows working analyses out of the box.

---

## 14. Testing

Touchstone suites in `Test.Shared` (no console output), run by `Test.Automated` (Touchstone.Cli), `Test.Xunit3`, and `Test.Nunit`.

| Suite | Covers |
|---|---|
| `Settings` | Defaults, clamping, env overrides, masking |
| `Requests` | Validation of image and video requests, field key rules, type constraints, context in all three accepted shapes |
| `FieldCoercion` | Every field type with good, borderline, and bad model output |
| `Prompts` | Prompt builder output for each function and mode |
| `MediaDetection` | Magic bytes for every supported format, mismatch handling |
| `Ffmpeg` | Probe, normalize, scene detection, frame and audio extraction against generated fixtures (ffmpeg `testsrc` and `sine` sources, so no binary fixtures in git). Skipped with a reason when ffmpeg is absent. |
| `Analysis` | Image and video pipelines end to end with the mock inference client, including partial failure, repair attempts, retries, telemetry totals |
| `Database` | Migrations, request history CRUD and summary gap fill, jobs CRUD and pruning. Runs on SQLite everywhere, and on MySQL, PostgreSQL, SQL Server in CI service containers. |
| `Server` | In-process server on `127.0.0.1` with a random port: auth (401, 403, 429), every route, job lifecycle, request history capture and redaction |

CI (`.github/workflows/test.yaml`): every push and pull request. Ubuntu job installs ffmpeg and runs all suites against all four providers using service containers. Windows and macOS jobs run the full suite on SQLite. A job builds the dashboard and runs its tests. SDK harness jobs start the server with the mock provider and run each SDK harness.

---

## 15. Phased Work Plan

Each phase ends with a green build (no warnings), passing tests, and an updated progress log.

### Phase 0: Planning and repository scaffold
- [x] Read requirement documents and reference repos (DocumentAtom, Tablix, Verbex, Pneuma, PolyPrompt)
- [x] Owner decisions captured (section 1)
- [x] `~/Code/PolyPrompt/MULTIMEDIA_SUPPORT_PLAN.md` written
- [x] `KEYFRAME_PLAN.md` written
- [x] Root files: `.gitignore`, `.dockerignore`, `LICENSE.md`, `README.md`, `CHANGELOG.md`, `CLAUDE.md`
- [x] `src/Directory.Build.props`, `Keyframe.sln`, Core, Server (placeholder entry point), Test.Shared, Test.Automated

### Phase 1: Core contracts
- [x] Enums, settings classes with clamping, request and response DTOs, field specs, context items
- [x] Request validation (`RequestValidator.Validate` overloads) returning every error found
- [x] `IdGenerator` with prefixes `kfa_` (analysis), `kfj_` (job), `req_` (request history), `rqi_` (request id)
- [x] Exceptions
- [x] Tests: Settings, Requests

### Phase 2: Media pipeline
- [x] `FfmpegRunner`, `MediaWorkspace`, `MediaResolver` with `UrlGuard` (enforced in the socket connect callback), `MediaTypeDetector`
- [x] `MediaProber`, `ImageNormalizer`, `SceneDetector`, `FrameExtractor`, `AudioExtractor`
- [x] Tests: Media suite (detection, URL guard, scene rules, probe, normalize, scenes, frames, audio, resolve)

### Phase 3: Inference seam and analysis
- [x] `IInferenceClient`, `ITranscriptionClient`, request and response types, mock clients, factory with concurrency limits and per-call timeouts
- [x] `PromptBuilder`, `ContextFormatter`, `FieldCoercer`, JSON schema generation for fields, lenient JSON parsing, repair loop, retries
- [x] `ImageAnalyzer`, `VideoAnalyzer` (scenes, transcription, whole-video step), `AnalysisContext` telemetry collection
- [x] Tests: Coercion, Prompts, Analysis

### Phase 4: Persistence
- [x] Database base, factory, settings, migrations
- [x] SQLite provider (request history with header child table, jobs, job results)
- [x] MySQL, PostgreSQL, SQL Server providers
- [x] Tests: Database (SQLite locally, all four in CI; verified locally against MySQL 8.4, PostgreSQL 17, SQL Server 2022 containers)

### Phase 5: Server
- [x] Bootstrapper, `KeyframeServer`, preflight and post-routing, auth hook, OpenAPI metadata for every route
- [x] Routes: Health, Root, Image, Video, Probe, Jobs, Endpoints, Settings, Request History
- [x] Services: Authentication (constant time, rate limit), JobService (queue, cancellation, interrupted-on-restart), request history capture, maintenance (pruning), endpoint health
- [x] Settings manager with masking, metadata, persist, restart endpoint
- [x] Radiant telemetry host, `KeyframeMetrics`, spans
- [x] Tests: Server

### Phase 6: PolyPrompt integration
- [x] `PolyPromptInferenceClient` mapping `InferenceRequest` to multimodal chat for each provider (Vertex AI and Bedrock deferred: they need credential settings)
- [x] `PolyPromptTranscriptionClient`
- [x] Live-provider smoke tests gated by environment variables (skipped in CI without keys)
- [x] Provider capability checks surfaced in `/v1.0/endpoints` (vision, structured output, transcription)

### Phase 7: Documentation and Postman
- [x] `README.md` (use cases, architecture, getting started, AOT support: No), `DOCKERHUB_README.md`, `TELEMETRY.md`
- [x] `REST_API.md`: every route with method, path, parameters, request and response bodies, status codes, auth, and examples
- [x] `MCP_API.md`: every tool with name, purpose, input schema, result shape, and examples, plus harness setup
- [x] Postman collection with folders (System, Image, Video, Jobs, Endpoints, Settings, Request History), variables for base URL, port, keys, and sample media URLs, descriptions at every level

### Phase 8: Docker and observability
- [x] Server and dashboard Dockerfiles, `docker/compose.yaml`, observability configs, Grafana dashboards in `assets/grafana/` mirrored into `docker/grafana/`
- [x] `docker/update.bat`/`.sh`, `docker/factory/` with reset scripts and seeded settings and samples

### Phase 9: SDKs
- [x] C# SDK and harness
- [x] JavaScript SDK and harness
- [x] Python SDK and harness

### Phase 9a: MCP server (Voltaic)
- [x] `Keyframe.McpServer` project on Voltaic, settings file, HTTP, TCP, and WebSocket transports
- [x] Tools in section 11a, implemented through `Keyframe.Sdk`
- [x] `--install` and `--uninstall` for Claude Code, Codex, Gemini, Cursor, and Mux
- [x] MCP service in `docker/compose.yaml` with a healthcheck, depending on the server being healthy
- [x] `Test.Mcp` (or a Test.Shared suite) exercising every tool against an in-process server with the mock provider

### Phase 10: Dashboard
- [x] Shell, auth context, ApiClient, i18n foundation, theme
- [x] Shared components (DataTable, Pagination, Modal, ConfirmModal, CopyableId, StatusBadge, JsonViewer, ActivityChart, TelemetryWaterfall, FieldBuilder)
- [x] Every route in section 12
- [x] Visual verification: desktop, tablet, mobile, dark and light

### Phase 11: CI and later work
- [x] GitHub Actions workflow (section 14; passes actionlint, not yet run on GitHub)
- [ ] Later: benchmark harness for field accuracy, simulated user testing session, EXIF extraction beyond ffprobe tags, perceptual-hash frame dedup, scene merge by caption similarity, webhook callback on job completion

---

## 16. Risks and Open Questions

- **PolyPrompt dependency.** Until Phase 6, the only working provider is the mock. The plan keeps Keyframe shippable for integration work in the meantime, but real analyses wait on PolyPrompt.
- **Model JSON reliability.** Small local vision models produce malformed JSON more often. The repair loop and `PerField` mode are the mitigations. The benchmark harness (Phase 11) should measure this per model.
- **Large uploads.** Base64 envelopes inflate payloads by a third and sit in memory. `Url` input is preferred for video. A raw-bytes or multipart upload route is a candidate for a later release.
- **Timestamp precision.** Scene boundaries come from ffmpeg, not from the model, so they are exact to the frame. Model-reported timestamps are never trusted for cutting.
- **Auth exception.** If multi-tenant use is ever needed, the full `AUTHENTICATION.md` model must be added, which touches every table. Request history and jobs carry `principalname` now so that migration has a starting point.

---

## 17. Progress Log

Add an entry for each working session: date, what landed, what is next, and anything a successor needs to know.

- **2026-11-15 (session 1, planning)**: Requirements and reference repos reviewed. Owner decisions recorded. PolyPrompt multimedia plan and this plan written. Local git repository initialized with `origin` set to `https://github.com/jchristn/Keyframe.git`. Nothing committed or pushed.
- **2026-11-15 (session 1, build)**: Phases 0 through 3 landed. `dotnet build src/Keyframe.sln -c Release` has 0 warnings and 0 errors; `dotnet run --project src/Test.Automated` passes 41 of 41 (ffmpeg 7 on macOS). Owner added mid-session: README uses `assets/logo-dark.png` with `v0.1.0 - ALPHA` (D6), favicon is `assets/glyph.ico` (D9, already copied into the server output), context is a list (D10), MCP on Voltaic plus `REST_API.md` and `MCP_API.md` (D8).

  Notes for the next session:
  - **Start Phase 4.** Verbex (`~/Code/Verbex/src/Verbex/Database`) is the reference for the driver base, factory, provider folders, and `Queries/` classes. Add `Models/Job.cs` and `Responses/JobResponse.cs` here; they were deferred from Phase 1 because their shape follows the jobs table.
  - **Then Phase 5.** `Keyframe.Server/Bootstrapper.cs` is a placeholder that exits with code 1; replace it. DocumentAtom's `ServerRuntime.cs` and `OpenApiRouteRegistrar.cs` show the Watson 7 route and OpenAPI metadata style; Tablix's `Observability/TelemetryHost.cs` shows Radiant wiring (Keyframe pins Radiant 0.1.3 and Watson 7.3.2).
  - Core targets **net10.0 only**: SqlClient pulls a vulnerable `System.Security.Cryptography.Pkcs` on net8, and the patched 10.0.15 is pinned directly. The SDKs (Phase 9) should still multi-target net8.0 and net10.0.
  - `TranscriptionChunkSeconds` has a 10 second minimum; tests rely on that.
  - Whole-video functions record their time under the `Summarize` and `Extract` stages; the `Reduce` stage enum value exists but is unused, and progress reports use "Reduce" as the stage label. Keep or remove it when the dashboard timeline is built.
  - `Test.Xunit3` and `Test.Nunit` runners are not created yet (Phase 11 CI needs them). Touchstone 1.1.0 is in use.
- **2026-11-15 (session 2, build)**: Phases 4, 5, 7, 8, 9, 9a, and the CI and runner parts of 11 landed while PolyPrompt is in progress. `dotnet build src/Keyframe.sln -c Release` has 0 warnings; Test.Automated passes 63 of 63 (Settings, Requests, Coercion, Prompts, Media, Analysis, Database-Sqlite, Server, Mcp); Test.Xunit3 and Test.Nunit run the same suites. The database suites pass against MySQL 8.4, PostgreSQL 17, and SQL Server 2022 containers (KEYFRAME_TEST_DATABASES). SDK harnesses: C# 38 on net8.0 and net10.0, JS 38, Python 40. Compose (server, MCP, collector, Prometheus, Tempo, Loki, Grafana) was brought up, checked healthy, and torn down.

  Notes for the next session:
  - **Phase 6 is the remaining blocker.** PolyPrompt (local repo) has 5.0 multimedia Phases 0 to 3 committed: `ChatMessage` content parts, `ChatRequest`, images/audio/video on the wire, `TranscribeAsync(TranscriptionRequest, TranscriptionOptions)`. File uploads are in progress. Wait for a published release, then implement `PolyPromptInferenceClient` and `PolyPromptTranscriptionClient` behind `InferenceClientFactory.CompletionProviderFactory` and `TranscriptionProviderFactory`, and make `IsProviderAvailable` true for the providers it supports.
  - Implementation decisions: provider `Implementations/` classes are thin subclasses of shared `Database/Common/*MethodsBase` because the SQL is portable; dialect differences (paging, timestamp storage, SQL Server DateTime2 parameters) live on the driver. Timestamps are truncated to microseconds everywhere. `DataTable.Load` is avoided because it merges rows that share a partial key.
  - The Voltaic version is 2.4.0 (MCP Streamable HTTP at /mcp, JSON-RPC at /rpc). MCP clients authenticate with the Keyframe access key as a bearer token.
  - Radiant 0.1.3 cannot bind Prometheus to 0.0.0.0, so the container settings use the hostname `keyframe-server` (see docker/keyframe.json).
  - Fixed in session 3: ffmpeg and ffprobe error text keeps file names but drops directories before reaching callers.
  - Phase 10 dashboard landed: `npm test` 32/32, lint clean, Playwright checks at 1280/768/390 px in both themes against a live server. The full Compose stack (server, dashboard, MCP, collector, Prometheus, Tempo, Loki, Grafana) came up healthy. `/openapi.json` CORS duplication was fixed server side; the dashboard keeps a same-origin fallback for the spec.
  - Remaining Phase 11 later work: benchmark harness, simulated user testing, EXIF beyond ffprobe tags, perceptual-hash dedup, scene merge by caption similarity, webhook on job completion.
- **2026-11-15 (session 3, Phase 6)**: PolyPrompt 5.0.0 from NuGet. `Keyframe.Core/Inference/Providers/` has `PolyPromptClientBuilder` (provider construction, support checks, retry classification, `Register(factory)`), `PolyPromptInferenceClient`, and `PolyPromptTranscriptionClient`. `AnalysisServices` registers the integration when no factory is passed. `InferenceClientFactory` gained `CompletionProviderSupported` and `TranscriptionProviderSupported` so availability is reported without building clients, and wraps construction errors (missing key or URL) in `InferenceException` (502). Test.Automated passes 68 of 68 with the 4 live cases skipped when no keys are set (69 with the Ollama case enabled); the live Ollama case passes with `qwen3.8:latest`, and a 3-scene video job ran end to end through the server against it.

  Notes for the next session:
  - Vertex AI and Bedrock are not wired: PolyPrompt needs project/region/service-account (Vertex) or region/access key/secret (Bedrock), and `EndpointSettings` has only `Endpoint` and `ApiKey`. Adding them means settings, dashboard, SDK, and docs changes.
  - Native JSON output is requested only on Responses API clients (OpenAI, Azure OpenAI, xAI) as `json_object`; `JsonSchema` is not sent as `json_schema` because generated schemas may not meet strict-mode rules. Other providers use the prompt plus lenient parsing.
  - Transcription asks for `verbose_json` with segment timestamps, except model names starting with `gpt-4o` on OpenAI and Azure (JSON only); those chunks become one segment each.
  - Provider clients are not disposed when an endpoint's settings object is replaced (the cache swaps the wrapper). Rare, but a long-running server that edits endpoints often will hold old HttpClients.
  - PolyPrompt logs to the console through its own default `LoggingModule` (warnings on failed calls). Passing Keyframe's logger through `ClientOptions.Logging` would unify logs.
- **2026-11-15 (session 4, audio, analytics, segments)**: Audio analysis (`POST /v1.0/audio`, `AudioAnalyzer`, shared `TranscriptionRunner` extracted from `VideoAnalyzer`), streaming completion calls with time to first token, generation time, tokens per second, and endpoint queue time, the analytics store (migration 2: `analyses` and `modelcalls`, one row per attempt, written by `AnalyticsService` in the background) with `/v1.0/analytics` routes, segment retrieval (`POST /v1.0/segment`, `SegmentExtractor`), new default endpoints (three Ollama models and speaches Whisper; no mock in shipped settings), a `keyframe-whisper` Compose service with a one-shot model download, and docs (REST_API.md table of contents, README API overview, TELEMETRY.md rewrite, Postman folders). `dotnet build` has 0 warnings; Test.Automated passes 89 of 94 with the 5 live cases skipped by default; the database suites pass on SQLite, MySQL 8.4, PostgreSQL 17, and SQL Server 2022. Live: Ollama gemma3:4b and qwen3-vl:8b image cases, and a `say`-generated speech clip through speaches 0.8.3 plus gpt-oss:20b, pass.

  Notes for the next session:
  - The analytics summary aggregates in memory over the filtered rows. That is exact and provider-independent, but a summary over months of heavy traffic loads every row in range; add pre-aggregated rollups if that becomes slow.
  - `modelcalls` stores attempts, so `ModelStats.Calls` and the model-call distributions count attempts; `StatBlock.ModelCalls` counts logical calls from the analyses rows.
  - `ModelSelection` accepts `Model` as an input alias of `Name` (the analytics and audio contract used `Model`).
  - speaches 0.8.3 needs `POST /v1/models/{id}` before transcription works; 0.9 (release candidate) adds `PRELOAD_MODELS`. The Compose `keyframe-whisper-init` job does the download.
  - The new metrics are not on the Grafana dashboards yet. The SDKs, MCP tools, and dashboard pages for audio, segments, and analytics are separate work items.
  - CI's SDK job now rewrites the copied `keyframe.json` with jq to use the mock endpoints.
- **2026-11-16 (session 3, round 2)**: Owner feedback round. Audio analysis (`POST /v1.0/audio`), segment retrieval (`POST /v1.0/segment`), streaming model calls with time to first token, generation time, tokens per second, and queue waits, analytics persistence (migration 2 on all four databases) with `/v1.0/analytics/*`, real-model defaults (Ollama `gemma3:4b`, `qwen3-vl:8b`, `gpt-oss:20b`, and a speaches Whisper container) replacing mock, an Analytics dashboard tab, 16 palettes with light and dark, full i18n (12 locales plus pseudo-locales), one-column analyze pages, a full-width API Explorer, SDK and MCP coverage for the new routes, MCP key pass-through, Grafana panels for the new instruments, `test-data/`, and `TESTING.md` (run against the real models). Test.Automated 91 of 96 (5 live cases opt-in); dashboard 78; SDK harnesses C# 56 on both targets, JS 55, Python 60.

  Notes for the next session:
  - The analytics summary computes percentiles in memory over every matching row. Fine for months of moderate traffic; pre-aggregate if it gets slow.
  - The new Keyframe meter instruments have no endpoint or model label; per-model views come from the analytics API.
  - speaches 0.8.3 needs its model installed explicitly; compose runs `keyframe-whisper-init` for that.
