# Keyframe REST API

Version 0.1.0 (ALPHA). The contract may change without notice until 1.0.

Keyframe listens on `http://127.0.0.1:8700` by default. Every route is versioned under `/v1.0/` except the root page, the favicon, and the OpenAPI document. The live OpenAPI 3 document is served at `GET /openapi.json` and a Swagger UI at `GET /swagger`; both are generated from the same route metadata as this file, so if the two ever disagree, the server is right and this file needs a fix.

JSON property names are PascalCase. Enum values travel as strings (`"Boolean"`, `"Succeeded"`). Property names are matched case-insensitively on input. Timestamps are ISO 8601 UTC.

## Contents

- [Authentication](#authentication)
- [Errors](#errors)
- [Common headers](#common-headers)
- [System](#system)
  - [`HEAD /`](#head-)
  - [`GET /`](#get-)
  - [`GET /favicon.ico`](#get-faviconico)
  - [`GET /v1.0/health`](#get-v10health)
  - [`GET /v1.0/whoami`](#get-v10whoami)
- [Analysis](#analysis)
  - [`POST /v1.0/image`](#post-v10image)
  - [`POST /v1.0/video`](#post-v10video)
  - [`POST /v1.0/audio`](#post-v10audio)
  - [`POST /v1.0/probe`](#post-v10probe)
- [Segments](#segments)
  - [`POST /v1.0/segment`](#post-v10segment)
- [Jobs](#jobs)
  - [`GET /v1.0/jobs`](#get-v10jobs)
  - [`GET /v1.0/jobs/{id}`](#get-v10jobsid)
  - [`GET /v1.0/jobs/{id}/result`](#get-v10jobsidresult)
  - [`POST /v1.0/jobs/{id}/cancel`](#post-v10jobsidcancel)
  - [`DELETE /v1.0/jobs/{id}`](#delete-v10jobsid)
- [Endpoints](#endpoints)
  - [Default endpoints](#default-endpoints)
  - [`GET /v1.0/endpoints`](#get-v10endpoints)
  - [`POST /v1.0/endpoints/{name}/test`](#post-v10endpointsnametest)
- [Analytics](#analytics)
  - [`GET /v1.0/analytics/summary`](#get-v10analyticssummary)
  - [`GET /v1.0/analytics/analyses`](#get-v10analyticsanalyses)
  - [`GET /v1.0/analytics/analyses/{id}`](#get-v10analyticsanalysesid)
  - [`DELETE /v1.0/analytics/analyses/{id}`](#delete-v10analyticsanalysesid)
  - [`DELETE /v1.0/analytics/analyses`](#delete-v10analyticsanalyses)
- [Settings](#settings)
  - [`GET /v1.0/settings`](#get-v10settings)
  - [`PUT /v1.0/settings`](#put-v10settings)
  - [`POST /v1.0/restart`](#post-v10restart)
  - [Listening, measurement, and probe settings](#listening-measurement-and-probe-settings)
- [Request history](#request-history)
  - [`GET /v1.0/api/request-history`](#get-v10apirequest-history)
  - [`GET /v1.0/api/request-history/summary`](#get-v10apirequest-historysummary)
  - [`GET /v1.0/api/request-history/{id}`](#get-v10apirequest-historyid)
  - [`DELETE /v1.0/api/request-history/{id}`](#delete-v10apirequest-historyid)
  - [`DELETE /v1.0/api/request-history`](#delete-v10apirequest-history)
- [Shared types](#shared-types)
  - [MediaSource](#mediasource)
  - [Context](#context)
  - [ModelSelection](#modelselection)
  - [ImageAnalysisRequest](#imageanalysisrequest)
  - [VideoAnalysisRequest](#videoanalysisrequest)
  - [AudioAnalysisRequest](#audioanalysisrequest)
  - [ListenSpec and AudioRenditionSpec](#listenspec-and-audiorenditionspec)
  - [MeasureSpec, SectionsSpec, SectionDetectionSpec](#measurespec-sectionsspec-sectiondetectionspec)
  - [SegmentRequest](#segmentrequest)
  - [SegmentResponse](#segmentresponse)
  - [SummarizeSpec](#summarizespec)
  - [ExtractSpec and FieldSpec](#extractspec-and-fieldspec)
  - [ScenesSpec, SceneDetectionSpec, SceneAudioSpec, TranscribeSpec](#scenesspec-scenedetectionspec-sceneaudiospec-transcribespec)
  - [AnalysisResponse](#analysisresponse)
  - [ListeningInfo and ListenClip](#listeninginfo-and-listenclip)
  - [AudioMeasurements](#audiomeasurements)
  - [SectionResult](#sectionresult)
  - [MediaProperties](#mediaproperties)
  - [JobResponse](#jobresponse)
  - [EndpointResponse](#endpointresponse)
  - [RequestHistoryEntry](#requesthistoryentry)
  - [AnalyticsSummary](#analyticssummary)
  - [AnalysisRecord](#analysisrecord)
  - [ModelCallRecord](#modelcallrecord)
  - [EnumerationResult](#enumerationresult)

## Authentication

Keyframe uses named access keys plus one admin key, configured in `keyframe.json` under `Auth`. Send a key in either header:

```http
Authorization: Bearer default
x-api-key: default
```

| Route class | Who may call |
|---|---|
| Anonymous | `HEAD /`, `GET /`, `GET /favicon.ico`, `GET /v1.0/health`, `GET /openapi.json`, `GET /swagger` |
| Key | Any active access key, or the admin key |
| Admin | The admin key only: settings, restart, endpoint tests, request history deletion |

A request with no key or an unknown or inactive key gets `401`. A valid non-admin key on an admin route gets `403`. Failed attempts are counted per source IP in a sliding one-minute window; once `Auth.MaxFailedAttemptsPerMinute` (default 30) is reached, every authenticated route returns `429` for that address until the window clears. Anonymous routes are never rate limited.

Keys are compared in constant time and are never logged or returned. When `Auth.RequireAuthentication` is `false` (local development only; the server logs a warning at startup), every caller is treated as the admin with principal name `anonymous`.

The principal name recorded in jobs and request history is the access key's `Name`, or `admin` for the admin key.

## Errors

Every error body has the same shape:

```json
{
  "Error": "BadRequest",
  "StatusCode": 400,
  "Message": "The request is invalid.",
  "Details": [ "Fields[1].Key 'dog count' may contain only letters, digits, underscore, hyphen, or period." ],
  "RequestId": "rqi_mwillzqc_eOygW7woAQVSghH2e16"
}
```

| Status | `Error` | When |
|---|---|---|
| 400 | `BadRequest` | Validation failed (`Details` lists every problem), unknown endpoint name, bad query parameter |
| 400 | `DeserializationError` | The body is not valid JSON for the route |
| 401 | `AuthenticationFailed` | Missing, unknown, or inactive key |
| 403 | `AuthorizationFailed` | Admin route with a non-admin key |
| 404 | `NotFound` | Route, job, endpoint, or entry not found |
| 409 | `Conflict` | Job result requested before success, deleting an active job, canceling a finished job |
| 422 | `UnprocessableMedia` | Media could not be downloaded or decoded, is the wrong kind for the route, or a private-network URL was refused |
| 429 | `TooManyRequests` | Authentication rate limit, or the job queue is full |
| 502 | `BadGateway` | The model endpoint is unreachable |
| 504 | `Timeout` | An operation timed out |
| 500 | `InternalError` | Anything else; the message names the request id to look up in the server log |

## Common headers

Every response carries `x-request-id`. Send your own `x-request-id` (up to 64 characters) to correlate a call with the server log; otherwise the server generates one with the `rqi_` prefix. CORS is open (`Access-Control-Allow-Origin: *`) and `OPTIONS` preflight requests return `204`. Exposed response headers: `x-request-id`, `Location`, `Retry-After`, `x-keyframe-job-id`.

Request bodies are limited to `Rest.MaxRequestBodyBytes` (default 256 MB). Base64 inflates media by a third, so prefer `Url` for anything large.

---

## System

### HEAD /

Liveness. Returns `200` with no body. Anonymous.

### GET /

A small HTML page with the version and links to `/v1.0/health`, `/openapi.json`, and `/swagger`. Anonymous.

### GET /favicon.ico

The Keyframe icon (`image/x-icon`). Anonymous.

### GET /v1.0/health

Anonymous. Orchestrator healthchecks probe this route.

```bash
curl http://127.0.0.1:8700/v1.0/health
```

```json
{
  "Status": "Healthy",
  "Product": "Keyframe",
  "Version": "0.1.0",
  "StartedUtc": "2026-11-16T01:41:49.7845Z",
  "UptimeSeconds": 3,
  "FfmpegAvailable": true,
  "FfmpegVersion": "ffmpeg version 9.0.2 Copyright (c) 2000-2026 the FFmpeg developers",
  "Database": "Sqlite",
  "ActiveJobs": 0,
  "QueuedJobs": 0,
  "Endpoints": { "ollama-gemma3-4b": true, "ollama-qwen3-vl-8b": true, "ollama-gpt-oss-20b": true, "whisper": true }
}
```

`Status` is `Degraded` when ffmpeg was not found at startup. `Endpoints` covers active endpoints only.

### GET /v1.0/whoami

Key. Resolves the presented key. The dashboard uses it to validate a login.

```json
{
  "IsAuthenticated": true,
  "IsAdmin": false,
  "PrincipalName": "default",
  "AuthenticationRequired": true,
  "Version": "0.1.0"
}
```

---

## Analysis

### POST /v1.0/image

Key. Summarizes an image and extracts typed fields. Synchronous.

Request body: [`ImageAnalysisRequest`](#imageanalysisrequest).

```bash
curl -X POST http://127.0.0.1:8700/v1.0/image \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{
    "Image": { "Url": "https://example.com/backyard.jpg" },
    "Context": [
      { "Label": "Source", "Text": "Photos submitted by pet adoption volunteers." },
      "Animals wearing orange collars are already adopted."
    ],
    "Functions": {
      "Summarize": { "Enable": true, "MaxWords": 60 },
      "Extract": {
        "Enable": true,
        "Fields": [
          { "Key": "hasCats", "Type": "Boolean", "Prompt": "Does this image have cats?" },
          { "Key": "dogCount", "Type": "Integer", "Prompt": "How many dogs do you see?", "Minimum": 0 },
          { "Key": "setting", "Type": "Enum", "Prompt": "Where was this taken?", "Options": [ "Indoors", "Outdoors" ] }
        ]
      }
    }
  }'
```

Response `200`: [`ImageAnalysisResponse`](#analysisresponse).

```json
{
  "Id": "kfa_mwilm0a1_3sK1QW9a7x0bQpVdL2c",
  "RequestId": "rqi_mwilm0a0_W2b8yS0TfQk1rVnXe4p",
  "Success": true,
  "MediaKind": "Image",
  "Properties": { "MediaKind": "Image", "ContentType": "image/jpeg", "Format": "image2", "SizeBytes": 2231840, "Sha256": "9f2c...", "Width": 4032, "Height": 3024, "VideoCodec": "mjpeg", "HasAudio": false, "Tags": { "Make": "Apple" } },
  "Results": { "Summary": "A fenced yard with two dogs near a red ball.", "hasCats": false, "dogCount": 2, "setting": "Outdoors" },
  "Fields": [
    { "Key": "hasCats", "Type": "Boolean", "Value": false, "Valid": true, "RawValue": "false", "Error": null, "Warnings": [] },
    { "Key": "dogCount", "Type": "Integer", "Value": 2, "Valid": true, "RawValue": "2", "Error": null, "Warnings": [] },
    { "Key": "setting", "Type": "Enum", "Value": "Outdoors", "Valid": true, "RawValue": "outdoors", "Error": null, "Warnings": [] }
  ],
  "Telemetry": {
    "StartUtc": "2026-11-15T18:01:02.114Z",
    "EndUtc": "2026-11-15T18:01:05.402Z",
    "TotalMs": 3288.4,
    "QueuedMs": null,
    "QueueWaitMs": 0,
    "Stages": [
      { "Name": "Resolve", "StartUtc": "2026-11-15T18:01:02.114Z", "DurationMs": 412.0, "Success": true },
      { "Name": "Probe", "StartUtc": "2026-11-15T18:01:02.526Z", "DurationMs": 38.2, "Success": true },
      { "Name": "Normalize", "StartUtc": "2026-11-15T18:01:02.565Z", "DurationMs": 61.7, "Success": true },
      { "Name": "Summarize", "StartUtc": "2026-11-15T18:01:02.627Z", "DurationMs": 1720.3, "Success": true },
      { "Name": "Extract", "StartUtc": "2026-11-15T18:01:02.627Z", "DurationMs": 1049.1, "Success": true }
    ],
    "ModelCalls": [
      { "Purpose": "Summarize", "Target": null, "Endpoint": "ollama-gemma3-4b", "Provider": "Ollama", "Model": "gemma3:4b", "DurationMs": 1720.3, "QueueMs": 0.0, "TimeToFirstTokenMs": 545.8, "GenerationMs": 1171.6, "TokensPerSecond": 60.6, "Attempts": 1, "RepairAttempts": 0, "Success": true, "InputImages": 1, "InputImageBytes": 184211, "PromptTokens": 812, "CompletionTokens": 71, "TotalTokens": 883, "FinishReason": "stop" }
    ],
    "Totals": { "ModelCalls": 2, "FailedModelCalls": 0, "ModelMs": 2769.4, "PromptTokens": 1611, "CompletionTokens": 118, "TotalTokens": 1729, "CachedTokens": 0, "ReasoningTokens": 0, "InputImages": 2, "InputImageBytes": 368422 }
  },
  "Warnings": [],
  "Errors": []
}
```

`200` means the analysis ran, including partial results. `Success` is `false` when a model call failed outright after retries; `Errors` explains, and whatever completed is still returned. A field that would not coerce keeps `Success = true`, sets that field's `Valid` to `false`, and returns its `Default` (or `null`). Other statuses: `400` (validation or unknown endpoint), `422` (media), `502` (model endpoint unreachable).

### POST /v1.0/video

Key. Queues a video analysis job: scene breakdown, transcript, per-scene and whole-video summaries and fields.

Request body: [`VideoAnalysisRequest`](#videoanalysisrequest).

```bash
curl -X POST http://127.0.0.1:8700/v1.0/video \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{
    "Video": { "Url": "https://example.com/talk.mp4" },
    "Context": [ "Recorded keynote at a developer conference." ],
    "Functions": {
      "Summarize": { "Enable": true, "MaxWords": 150 },
      "Extract": { "Enable": true, "Fields": [ { "Key": "speakerCount", "Type": "Integer", "Prompt": "How many distinct speakers appear?" } ] },
      "Scenes": {
        "Enable": true,
        "FramesPerScene": 3,
        "Detection": { "Threshold": 0.3, "MinSceneMs": 2000, "MaxSceneMs": 60000 },
        "Summarize": { "Enable": true, "Style": "Caption" },
        "Extract": { "Enable": true, "Fields": [ { "Key": "showsSlide", "Type": "Boolean", "Prompt": "Is a presentation slide visible?" } ] }
      },
      "Transcribe": { "Enable": true, "Language": "en" }
    }
  }'
```

Responses:

- `202 Accepted` with a [`JobResponse`](#jobresponse) and `Location: /v1.0/jobs/{id}`. Poll the job, then fetch its result.
- With `"Wait": true`, the server waits up to `WaitTimeoutMs` (capped by `Jobs.MaxWaitTimeoutMs`, default 600000). If the job succeeds in time the response is `200` with the full [`VideoAnalysisResponse`](#analysisresponse) and an `x-keyframe-job-id` header. If it finishes without succeeding (failed or canceled) the response is `200` with the `JobResponse` so you can read `Status` and `Error`. If it is still running at the deadline, the response is `202` as above.
- `400` for validation errors or an unknown endpoint. Validation and endpoint resolution happen before the job is queued, so a bad request never produces a failed job.
- `429` when `Jobs.MaxQueuedJobs` jobs are already waiting.

```json
{
  "Id": "kfj_mwillzqs_gWYGahYq6XSOKgHaVay",
  "Status": "Queued",
  "MediaKind": "Video",
  "PrincipalName": "default",
  "SourceUrl": "https://example.com/talk.mp4",
  "SizeBytes": 0,
  "DurationMs": 0,
  "Progress": { "Stage": "Queued", "Completed": 0, "Total": 0, "Percent": 0 },
  "Error": null,
  "CreatedUtc": "2026-11-16T01:42:11.201Z",
  "StartedUtc": null,
  "CompletedUtc": null,
  "ResultUrl": null
}
```

Scene boundaries come from ffmpeg scene-change detection, then are merged and split by `MinSceneMs`, `MaxSceneMs`, and `MaxScenes`. Models describe scenes; they never decide where a cut is. When scenes are disabled, whole-video functions run on `SampleFrames` frames sampled evenly across the video. When `TextModel` is set, the whole-video summary and fields are produced from the scene results and transcript with a text-only model instead of from frames.

#### Scene audio

Scene calls also hear each scene's audio by default (`Functions.Scenes.Audio.Enable`, see [SceneAudioSpec](#scenesspec-scenedetectionspec-sceneaudiospec-transcribespec)). Each scene's Summarize and Extract calls carry that scene's audio, rendered with `Scenes.Audio.Rendition` (the same `Media.Listen*` defaults as audio analysis), next to its frames and transcript slice. A scene longer than the vision endpoint's listen budget (its `MaxListenSeconds`, else `Media.MaxListenSeconds`), or whose clip and frames together would exceed the endpoint's `MaxInlineMediaBytes`, carries a centered excerpt that fits. The whole-video step is unchanged.

The vision endpoint is chosen for seeing, so scene audio never causes a `400`. When the vision endpoint cannot take audio (its provider is not `Gemini`, `OpenAiCompatible`, or `Mock`, or the rendition format is one it refuses, such as `Flac` to `OpenAiCompatible`), when it is unhealthy, or when the video has no audio track, scenes run on frames and transcript as before. `VideoAnalysisResponse.Listening` (a [ListeningInfo](#listeninginfo-and-listenclip)) is present whenever scenes are enabled and says what happened: `Mode: "Scenes"` with one `Scene` clip per scene when the calls carried audio, otherwise `Mode: "None"` with `Reason` set to `NotRequested`, `NoModelFunctions` (scenes have no Summarize or Extract), `EndpointLacksAudioInput`, `EndpointUnhealthy`, or `NoAudioTrack`. Only `EndpointUnhealthy` adds a warning (`"The vision endpoint 'ollama-gemma3-4b' is unhealthy; scenes use frames and transcript without audio."`), because the others are expected configuration. The default Ollama vision endpoints lack audio input, so on the shipped settings scene audio reports `EndpointLacksAudioInput` until `Model` names an audio-capable vision endpoint such as Gemini.

```json
"Scenes": { "Enable": true, "Summarize": { "Enable": true }, "Audio": { "Enable": true, "Rendition": { "Channels": 2 } } }
```

```json
"Listening": {
  "Listened": true, "Mode": "Scenes", "Reason": null, "Endpoint": "gemini-listen", "Model": "gemini-2.5-flash",
  "Rendition": { "Format": "Mp3", "SampleRate": 16000, "Channels": 2, "BitrateKbps": 64 },
  "DurationMs": 12000, "HeardMs": 12000,
  "Clips": [
    { "Scope": "Scene", "Index": 0, "StartMs": 0, "EndMs": 4000, "SizeBytes": 33267 },
    { "Scope": "Scene", "Index": 1, "StartMs": 4000, "EndMs": 8000, "SizeBytes": 33267 },
    { "Scope": "Scene", "Index": 2, "StartMs": 8000, "EndMs": 12000, "SizeBytes": 33267 }
  ]
}
```

### POST /v1.0/audio

Key. Queues an audio analysis job. Accepts audio files (mp3, wav, flac, m4a or aac, ogg or opus, webm audio) and video files; for a video only the audio track is used, no frames are extracted, and the response carries a warning.

By default a listening model hears the recording itself, a transcript is made alongside it as a hint, and deterministic measurements are taken before any model runs. `{ "Audio": { "Url": "..." } }` alone returns a transcript, a summary from a model that heard the audio, `Measurements`, and a `Listening` object that says whether and how the audio was heard. Without a usable listening endpoint the same request still works: the summary comes from the transcript, and `Listening` says so.

The job runs these stages in order, each only when its function is on:

```text
Resolve -> Probe -> Measure -> AudioExtraction -> Transcribe -> SectionDetection
        -> ListenExtraction -> Sections -> Summarize + Extract
```

| Function | Default | What it does |
|---|---|---|
| `Functions.Measure` | on | One ffmpeg decode pass (`ebur128`, `astats`, and `silencedetect`, plus a mid/side `astats` pass for stereo sources) fills [`Measurements`](#audiomeasurements): loudness, peak and RMS levels, clipping, silence, and stereo width. No model calls. The figures also go into every later prompt as data. |
| `Functions.Transcribe` | on | The audio is extracted (16 kHz mono FLAC, split into `Media.TranscriptionChunkSeconds` chunks) and every chunk is transcribed in parallel through the transcription endpoint. |
| `Listen` (a request member, not under `Functions`) | on | Summarize, Extract, and the section functions hear the audio through the listening endpoint (`Listen.Model`, else `Inference.DefaultListening`). See [Listening](#listening). |
| `Functions.Summarize` | on | A summary of the whole recording. |
| `Functions.Extract` | off | Typed fields for the whole recording. |
| `Functions.Sections` | off | Split the recording at silences and run Summarize and Extract per section. See [Sections](#sections). |

At least one of Transcribe, Summarize, Extract, Measure, or Sections must be enabled. Measure counts, so a request with every other function off is a measure-only job that makes no model calls. Summarize, Extract, and the section functions need evidence: `Functions.Transcribe.Enable`, `Listen.Enable`, or both. When the analysis does not listen and the transcript is empty (silence, or instrumental music), Summarize and Extract are skipped with a warning. When it listens, an empty transcript is fine and the model relies on the audio.

Request body: [`AudioAnalysisRequest`](#audioanalysisrequest).

A track from a band's archive, heard by the shipped `llama-listen` endpoint:

```bash
curl -X POST http://127.0.0.1:8700/v1.0/audio \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{
    "Audio": { "Url": "https://example.com/track07.mp3" },
    "Listen": { "Enable": true, "Model": { "Endpoint": "llama-listen" } },
    "Context": [ { "Label": "Artist", "Text": "Archive of a touring rock band, 1978 to 1994." } ],
    "Functions": {
      "Transcribe": { "Enable": true },
      "Summarize": { "Enable": true, "MaxWords": 80 },
      "Extract": { "Enable": true, "Fields": [
        { "Key": "chorusLyrics", "Type": "String", "Prompt": "Lyrics of the chorus." },
        { "Key": "recording", "Type": "Enum", "Options": [ "Live", "Studio", "Rehearsal", "Demo" ], "Prompt": "How was this recorded?" },
        { "Key": "explicit", "Type": "Boolean", "Prompt": "Does it contain profanity?" },
        { "Key": "instruments", "Type": "StringList", "Prompt": "Instruments you can hear." }
      ] }
    }
  }'
```

Measure is not listed under `Functions` and still runs, because an omitted function keeps its default. The same holds for `Listen`: `"Listen": { "Model": { ... } }` without `Enable` still listens.

A meeting recording answered from the transcript only, the way audio analysis worked before listening:

```bash
curl -X POST http://127.0.0.1:8700/v1.0/audio \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{
    "Audio": { "Url": "https://example.com/standup.mp3" },
    "Context": [ "Daily standup of the platform team." ],
    "Listen": { "Enable": false },
    "TranscriptionModel": { "Endpoint": "whisper" },
    "TextModel": { "Endpoint": "ollama-gpt-oss-20b" },
    "Functions": {
      "Transcribe": { "Enable": true, "Language": "en" },
      "Summarize": { "Enable": true, "Style": "Bullets", "MaxWords": 120 },
      "Extract": { "Enable": true, "Fields": [
        { "Key": "speakerNames", "Type": "StringList", "Prompt": "Names of the people who speak or are mentioned." },
        { "Key": "blockers", "Type": "Boolean", "Prompt": "Does anyone report a blocker?" }
      ] }
    }
  }'
```

Measurements only, with no model calls (`Listening.Reason` is `NoModelFunctions` and `Results` is empty):

```bash
curl -X POST http://127.0.0.1:8700/v1.0/audio \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{ "Audio": { "Url": "https://example.com/master.wav" }, "Wait": true,
        "Functions": { "Transcribe": { "Enable": false }, "Summarize": { "Enable": false } } }'
```

Responses:

- `202 Accepted` with a [`JobResponse`](#jobresponse) (`MediaKind: "Audio"`) and `Location`, as for `POST /v1.0/video`.
- `200` with the full [`AudioAnalysisResponse`](#analysisresponse) when `Wait` is true and the job succeeds in time, or `200` with the job when it finished without succeeding.
- `400` for validation errors or an unknown endpoint, checked before the job is queued. Four listening problems are also rejected at submission, so they never become failed jobs:
  - `Listen.Model.Endpoint` names an endpoint that does not exist, is inactive, or is not a completion endpoint: `Listening endpoint 'whisper' is not an active completion endpoint.`
  - `Listen.Model.Endpoint` names an endpoint whose provider cannot take audio: `Endpoint 'ollama-gemma3-4b' cannot take audio input. Choose an endpoint whose provider supports audio (Gemini, OpenAiCompatible), or set Listen.Enable to false.`
  - The effective rendition format is `Flac` and the listening endpoint, named or default, is `OpenAiCompatible` (OpenAI and llama-server accept only mp3 and wav): `Endpoint 'llama-listen' (OpenAiCompatible) accepts only Mp3 or Wav audio. Set Listen.Rendition.Format to Mp3 or Wav.`
  - Transcribe is off, a model function is enabled, and listening would fall back, so the job would have no evidence at all: `Nothing to analyze: no usable audio-capable endpoint and Transcribe is off.`
- `429` when the queue is full.

A named listening endpoint that is unhealthy is not rejected. It falls back with `EndpointUnhealthy`, because health is transient. Listening is resolved again when the job starts, so a rejection that only becomes true while the job waits in the queue (for example after a settings change) fails the job with the same message. A source with no audio stream, an image, or audio longer than `Media.MaxAudioDurationMs` (default 4 hours) fails the job with a `422`-class error in `Error`.

The result for the band track above. The track is 4 minutes long and `llama-listen` has a 120-second listen budget, so the model heard four 30-second excerpts:

```json
{
  "Id": "kfa_mwl4k2qd_Q7x2Vb9cTn4LsK1pW0e",
  "RequestId": "rqi_mwl4k2q8_Hc3TzQ0vYb6Nk2mPa1s",
  "Success": true,
  "MediaKind": "Audio",
  "Properties": { "MediaKind": "Audio", "ContentType": "audio/mpeg", "Format": "mp3", "SizeBytes": 5784012, "DurationMs": 241000, "BitRate": 192000, "HasAudio": true, "AudioCodec": "mp3", "AudioChannels": 2, "AudioSampleRate": 44100, "Tags": { "title": "Track 07" } },
  "Transcript": {
    "Language": "en",
    "Text": "Thank you, good night! ...",
    "Segments": [ { "StartMs": 2100, "EndMs": 4800, "Text": "Thank you, good night!" } ]
  },
  "Listening": {
    "Listened": true, "Mode": "Excerpts", "Reason": null,
    "Endpoint": "llama-listen", "Model": "voxtral-mini-3b",
    "Rendition": { "Format": "Mp3", "SampleRate": 16000, "Channels": 1, "BitrateKbps": 64 },
    "DurationMs": 241000, "HeardMs": 120000,
    "Clips": [
      { "Scope": "Recording", "Index": null, "StartMs": 0, "EndMs": 30000, "SizeBytes": 241005 },
      { "Scope": "Recording", "Index": null, "StartMs": 70333, "EndMs": 100333, "SizeBytes": 241005 },
      { "Scope": "Recording", "Index": null, "StartMs": 140666, "EndMs": 170666, "SizeBytes": 241005 },
      { "Scope": "Recording", "Index": null, "StartMs": 211000, "EndMs": 241000, "SizeBytes": 241005 }
    ]
  },
  "Measurements": {
    "IntegratedLufs": -9.4, "LoudnessRangeLu": 5.1, "TruePeakDbtp": 0.1,
    "PeakDbfs": -0.3, "RmsDbfs": -11.8, "CrestFactorDb": 11.5,
    "PeakCount": 214, "ClippingLikely": true,
    "SilenceRatio": 0.03, "SilenceCount": 2, "LeadingSilenceMs": 400, "TrailingSilenceMs": 2100,
    "Channels": 2, "StereoWidthDb": -14.2, "EffectivelyMono": false
  },
  "Sections": null,
  "Results": {
    "Summary": "A loud, crowded live rock performance: distorted guitars, bass, drums, and a shouted chorus, with cheering between lines and applause at the end.",
    "chorusLyrics": "Hold on, hold on, the night is young",
    "recording": "Live",
    "explicit": false,
    "instruments": [ "electric guitar", "bass guitar", "drums", "vocals" ]
  },
  "Fields": [
    { "Key": "chorusLyrics", "Type": "String", "Value": "Hold on, hold on, the night is young", "Valid": true, "RawValue": "Hold on, hold on, the night is young", "Error": null, "Warnings": [] },
    { "Key": "recording", "Type": "Enum", "Value": "Live", "Valid": true, "RawValue": "Live", "Error": null, "Warnings": [] },
    { "Key": "explicit", "Type": "Boolean", "Value": false, "Valid": true, "RawValue": "false", "Error": null, "Warnings": [] },
    { "Key": "instruments", "Type": "StringList", "Value": [ "electric guitar", "bass guitar", "drums", "vocals" ], "Valid": true, "RawValue": "[\"electric guitar\",\"bass guitar\",\"drums\",\"vocals\"]", "Error": null, "Warnings": [] }
  ],
  "Telemetry": {
    "TotalMs": 96214.7, "QueueWaitMs": 2.1, "QueuedMs": 2.1,
    "Stages": [
      { "Name": "Resolve", "DurationMs": 812.4, "Success": true },
      { "Name": "Probe", "DurationMs": 44.0, "Success": true },
      { "Name": "Measure", "DurationMs": 1630.2, "Success": true },
      { "Name": "AudioExtraction", "DurationMs": 410.7, "Success": true },
      { "Name": "Transcribe", "DurationMs": 9120.5, "Success": true },
      { "Name": "ListenExtraction", "DurationMs": 388.1, "Success": true },
      { "Name": "Summarize", "DurationMs": 41022.3, "Success": true },
      { "Name": "Extract", "DurationMs": 83710.9, "Success": true }
    ],
    "ModelCalls": [
      { "Purpose": "Transcribe", "Target": "chunk:0", "Endpoint": "whisper", "Provider": "OpenAiCompatible", "Model": "Systran/faster-whisper-small", "DurationMs": 9118.2, "Attempts": 1, "Success": true, "InputAudioBytes": 7712044 },
      { "Purpose": "Summarize", "Target": null, "Endpoint": "llama-listen", "Provider": "OpenAiCompatible", "Model": "voxtral-mini-3b", "DurationMs": 41020.6, "QueueMs": 0.0, "TimeToFirstTokenMs": 36410.2, "Attempts": 1, "Success": true, "InputAudioBytes": 964020, "PromptTokens": 1912, "CompletionTokens": 61 },
      { "Purpose": "Extract", "Target": null, "Endpoint": "llama-listen", "Provider": "OpenAiCompatible", "Model": "voxtral-mini-3b", "DurationMs": 83709.4, "QueueMs": 41021.0, "TimeToFirstTokenMs": 37902.8, "Attempts": 1, "Success": true, "InputAudioBytes": 964020, "PromptTokens": 2104, "CompletionTokens": 88 }
    ]
  },
  "Warnings": [ "Listened to 4 excerpts (2m00s) of a 4m01s recording." ],
  "Errors": []
}
```


Against a Gemini or vLLM endpoint with the default 1200-second budget, the same request reports `Mode: "Full"`, one clip, and no warning. When the host llama-server is stopped or still loading its model, the same request returns transcript-only results, the warning `The listening endpoint 'llama-listen' is unhealthy; results come from the transcript only.`, and:

```json
"Listening": {
  "Listened": false, "Mode": "None", "Reason": "EndpointUnhealthy",
  "Endpoint": null, "Model": null,
  "Rendition": { "Format": "Mp3", "SampleRate": 16000, "Channels": 1, "BitrateKbps": 64 },
  "DurationMs": 241000, "HeardMs": 0, "Clips": []
}
```

#### Listening

Listening is decided once, when the job starts running. A job that waited in the queue uses the endpoint health of that moment, and every function in the job hears the same evidence. The listening endpoint is `Listen.Model.Endpoint` when one is named, else `Inference.DefaultListening`; there is no further fallback to the vision or text endpoints. Its provider must accept audio input: `Gemini`, `OpenAiCompatible` (llama.cpp `llama-server`, vLLM, and OpenAI's audio models at `https://api.openai.com/v1`), or `Mock`. [`GET /v1.0/endpoints`](#get-v10endpoints) reports this as `SupportsAudioInput`.

When the model listens, the prompt says the audio is attached and that the transcript is a machine-generated hint that can be wrong, especially for sung vocals; when the two disagree, the audio wins. For an `OpenAiCompatible` endpoint, a request that carries audio has no separate system message: the system prompt leads the user message, because Voxtral served by vLLM rejects a system prompt alongside audio. The measurements go into the prompt as data, never as instructions.

What the whole-recording functions hear depends on the listen budget (the endpoint's `MaxListenSeconds`, else `Media.MaxListenSeconds`, default 1200 seconds) and the endpoint's `MaxInlineMediaBytes` (default 20 MB, counted as base64 on the wire). Keyframe measures the rendered clips against that limit before each call, so PolyPrompt never has to reject an oversized one.

| `Listening.Mode` | When | What is sent |
|---|---|---|
| `Full` | The recording fits the budget and its rendered clip fits `MaxInlineMediaBytes` | One clip of the whole recording (`Scope: "Recording"`) |
| `Excerpts` | The recording is too long or too large to send whole | `Listen.SampleClips` evenly spaced clips of `Listen.ClipSeconds` each (`Scope: "Recording"`), labeled with their offsets and sent in one call. When `SampleClips` times `ClipSeconds` exceeds the budget, or the clips would exceed `MaxInlineMediaBytes`, each clip is shortened to fit and a warning says so |
| `Sections` | The whole-recording results are reduced from section results (`TextModel` set), or only section functions ran | Only per-section clips (`Scope: "Section"`) |
| `None` | Nothing was heard | Nothing. `Reason` says why |

Section functions always hear their own section's clip, cut down to a centered excerpt when the section is longer than the budget or its clip would exceed `MaxInlineMediaBytes`; the clip's actual range is in `Clips`. `Excerpts` always adds a warning such as `Listened to 4 excerpts (2m00s) of a 4m01s recording.`; `Full` and `Sections` add none.

The audio sent to a model is its own rendition, separate from the transcription chunks: mono mp3 at 16 kHz and 64 kbps unless `Listen.Rendition` or the `Media.Listen*` settings say otherwise. That default fits about 30 minutes under the 20 MB inline limit. A larger rendition means fewer minutes per call, so asking for stereo at 44.1 kHz and 192 kbps reaches excerpts sooner, and `Listening.Mode` says so.

`Listening` is in every audio response, so a consumer never has to infer from absence whether the audio was heard. `Listened` is `true` when at least one model call carried audio. When it is `false`, `Mode` is `None` and `Reason` is a stable code (the server never sends prose there):

| `Reason` | Meaning | Warning |
|---|---|---|
| `NotRequested` | `Listen.Enable` is false | none |
| `NoModelFunctions` | No Summarize, Extract, or section function is enabled, so nothing needed the audio | none |
| `NoEndpointConfigured` | No endpoint is named, and `Inference.DefaultListening` is unset, unknown, inactive, or not a completion endpoint | `No listening endpoint is configured (Listen.Model or Inference.DefaultListening); results come from the transcript only.` |
| `EndpointLacksAudioInput` | The default listening endpoint's provider cannot take audio | `The listening endpoint 'ollama-gemma3-4b' (Ollama) cannot take audio input; results come from the transcript only.` |
| `EndpointUnhealthy` | The resolved endpoint, named or default, was unhealthy when the job started | `The listening endpoint 'llama-listen' is unhealthy; results come from the transcript only.` |
| `NoAudioTrack` | The media has no audio track. Used by video scene audio; an audio job without an audio stream fails with a `422`-class error instead | none |

A fallback runs Summarize and Extract on the transcript with the text model (`TextModel`, else `Inference.DefaultText`, else `Inference.DefaultVision`), exactly as when listening is off. An endpoint the health service has not checked yet counts as usable. The first probe runs within a second of startup. An unhealthy endpoint is probed again every `Inference.UnhealthyProbeIntervalMs` (default 15 seconds), so a listening server that comes back is used again within about that interval. A healthy endpoint is probed every `Inference.HealthProbeIntervalMs` (default five minutes), but a call that cannot reach an endpoint at all (no HTTP response, for example connection refused) marks it unhealthy at once. So when a listening server stops, the first job that tries it reports the failed call as an error, and later jobs fall back with `EndpointUnhealthy` until a probe finds it answering again. Timeouts and HTTP errors do not change health.

A listening call that fails while the job runs (a timeout, a server error, a refused payload) is a function error. It goes to `Errors`, the function's stage fails, `Success` is `false`, and `Listening` still describes what was sent, with `Listened` still `true`. Keyframe does not rerun the function on the transcript.

#### Sections

`Functions.Sections` splits the recording where ffmpeg `silencedetect` finds silence below `Detection.SilenceDb` (default -40 dBFS) lasting at least `Detection.MinSilenceMs` (default 1500). Each cut falls at the midpoint of a silence inside the recording; silences at either end are not cuts. Sections shorter than `MinSectionMs` merge into a neighbor, sections longer than `MaxSectionMs` split evenly, and beyond `MaxSections` the shortest adjacent pairs merge. Models never choose section boundaries.

Each section gets its slice of the transcript in `TranscriptText` and runs `Sections.Summarize` and `Sections.Extract` (purposes `SectionSummarize` and `SectionExtract`, `Target` `section:<index>`). When listening, each section hears its own clip. When not, each section runs on its transcript slice, and a section with no transcript text is skipped with a warning.

When `TextModel` is set and section functions run, the whole-recording Summarize and Extract are reduced from the section results with the text model, the way video reduces from scenes, and `Listening.Mode` is `Sections`. Without `TextModel`, the whole-recording functions hear the full audio or excerpts while each section hears its own clip. Any section can be cut back out with [`POST /v1.0/segment`](#post-v10segment) using its `StartMs` and `EndMs`.

```bash
curl -X POST http://127.0.0.1:8700/v1.0/audio \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{
    "Audio": { "Url": "https://example.com/live-set.mp3" },
    "TextModel": { "Endpoint": "ollama-gpt-oss-20b" },
    "Functions": {
      "Summarize": { "Enable": true, "MaxWords": 120 },
      "Sections": {
        "Enable": true,
        "Detection": { "SilenceDb": -40, "MinSilenceMs": 1500, "MinSectionMs": 20000 },
        "Summarize": { "Enable": true, "Style": "Caption" },
        "Extract": { "Enable": true, "Fields": [
          { "Key": "isSong", "Type": "Boolean", "Prompt": "Is this section a song rather than talk between songs?" }
        ] }
      }
    }
  }'
```

```json
"Sections": [
  { "Index": 0, "StartMs": 0, "EndMs": 26000, "DurationMs": 26000, "TranscriptText": "Good evening!",
    "Results": { "Summary": "Crowd noise and a short greeting before the first song.", "isSong": false },
    "Fields": [ { "Key": "isSong", "Type": "Boolean", "Value": false, "Valid": true, "RawValue": "false", "Error": null, "Warnings": [] } ],
    "Errors": [] },
  { "Index": 1, "StartMs": 26000, "EndMs": 284500, "DurationMs": 258500, "TranscriptText": "...",
    "Results": { "Summary": "A fast rock song with a sing-along chorus.", "isSong": true },
    "Fields": [ { "Key": "isSong", "Type": "Boolean", "Value": true, "Valid": true, "RawValue": "true", "Error": null, "Warnings": [] } ],
    "Errors": [] }
]
```

### POST /v1.0/probe

Key. Deterministic media properties from magic-byte detection and ffprobe. No model calls.

```bash
curl -X POST http://127.0.0.1:8700/v1.0/probe -H "x-api-key: default" \
  -d '{ "Media": { "Url": "https://example.com/talk.mp4" } }'
```

Response `200`: [`MediaProperties`](#mediaproperties). `422` when the media cannot be downloaded or decoded.

---

## Segments

### POST /v1.0/segment

Key. Cuts a time range out of a video or audio source and returns it, so you can fetch the media behind a scene (`Scenes[i].StartMs`/`EndMs`) or a transcript segment (`Transcript.Segments[i].StartMs`/`EndMs`) from an analysis result. Stateless: Keyframe does not keep uploaded media, so send the same `Source` (URL or base64) you analyzed. URL sources go through the same download limits and private-address guard as analysis.

Request body: [`SegmentRequest`](#segmentrequest).

| `Output` | `Format` (default first) | Result |
|---|---|---|
| `Video` (default) | `mp4`, `webm` | Re-encoded clip of `[StartMs, EndMs)` for frame-accurate cuts: H.264 and AAC in mp4, VP9 and Opus in webm. The audio track is kept when the source has one. |
| `Audio` | `mp3`, `wav`, `flac`, `m4a`, `ogg` | Audio clip of `[StartMs, EndMs)`. Works on video and audio sources. |
| `Frame` | `jpg` (or `jpeg`), `png` | One still frame at `StartMs`. `EndMs` is ignored. |

`MaxEdge` (16 to 4096) caps the long edge of video and frame output; smaller media is not enlarged.

Validation (`400`, every problem listed in `Details`): `Source` required with exactly one of `Url` or `Data`; `StartMs` at least 0; `EndMs` required and greater than `StartMs` for `Video` and `Audio`; `EndMs - StartMs` at most `Media.MaxSegmentMs` (default 600000, range 1000 to 14400000); `Format` valid for the `Output`; `MaxEdge` in range. Media errors (`422`): the source cannot be resolved, it is an image, `Audio` output from a source without an audio track, `Video` or `Frame` output from an audio-only source (cover art in an mp3 does not count as video), or `StartMs` at or past the end. An `EndMs` past the end of the media is clamped to the duration, and the clamped value is reported.

Binary (default) response: `200` with the media as the body, the matching `Content-Type` (`video/mp4`, `video/webm`, `audio/mpeg`, `audio/wav`, `audio/flac`, `audio/mp4`, `audio/ogg`, `image/jpeg`, `image/png`), `Content-Disposition: attachment; filename="segment-12000-18500.mp4"` (frames: `segment-12000.jpg`), and the headers `x-keyframe-segment-start-ms`, `x-keyframe-segment-end-ms`, and `x-keyframe-segment-duration-ms` (exposed to browsers through CORS).

```bash
curl -X POST http://127.0.0.1:8700/v1.0/segment \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{ "Source": { "Url": "https://example.com/talk.mp4" }, "StartMs": 12000, "EndMs": 18500, "Output": "Video", "MaxEdge": 720 }' \
  -o scene-3.mp4
```

With `"ResponseFormat": "Json"` the response is a [`SegmentResponse`](#segmentresponse):

```json
{ "MediaType": "image/jpeg", "Filename": "segment-12000.jpg", "StartMs": 12000, "EndMs": 12000, "DurationMs": 0, "SizeBytes": 48211, "Data": "/9j/4AAQSkZJRgABAQAAAQABAAD..." }
```

---

## Jobs

Video (`POST /v1.0/video`) and audio (`POST /v1.0/audio`) analyses run as jobs. Jobs are durable: their status, progress, and result survive in the database. A job that was queued or running when the server stopped is marked `Interrupted` at the next start. Finished jobs and results are pruned after `Jobs.RetentionDays` (default 7).

Statuses: `Queued`, `Running`, `Succeeded`, `Failed`, `Canceled`, `Interrupted`. A `Succeeded` job always has a result; read `Success` and `Errors` inside the result to see whether every function produced output.

### GET /v1.0/jobs

Key. Newest first.

| Query | Type | Notes |
|---|---|---|
| `status` | string | One of the statuses above; anything else is `400` |
| `mediaKind` | string | `Video` or `Audio` (jobs are never images) |
| `principalName` | string | Submitting key name |
| `fromUtc`, `toUtc` | ISO 8601 | Inclusive lower and exclusive upper bound on `CreatedUtc` |
| `pageNumber` | integer | From 1, default 1 |
| `pageSize` | integer | 1 to 1000, default 25 |

Response `200`: `EnumerationResult<JobResponse>`:

```json
{ "Items": [ { "Id": "kfj_...", "Status": "Succeeded", "...": "..." } ], "PageNumber": 1, "PageSize": 25, "TotalCount": 2, "TotalPages": 1 }
```

### GET /v1.0/jobs/{id}

Key. Response `200`: [`JobResponse`](#jobresponse). `404` when unknown. Progress is persisted at most once a second and on every stage change, so polling every second or two is plenty.

### GET /v1.0/jobs/{id}/result

Key. Response `200`: the stored [`VideoAnalysisResponse`](#analysisresponse) for a video job or [`AudioAnalysisResponse`](#analysisresponse) for an audio job. `409` while the job is not `Succeeded` (the message includes the job's error, if any). `404` when the job is unknown or its result was pruned.

### POST /v1.0/jobs/{id}/cancel

Key. Cancels a queued job immediately, or signals a running job and waits up to ten seconds for it to stop. Response `200`: the [`JobResponse`](#jobresponse) after cancellation. `409` when the job already finished. `404` when unknown.

### DELETE /v1.0/jobs/{id}

Key. Deletes a finished job and its result. Response `204`. `409` for a queued or running job (cancel it first). `404` when unknown.

---

## Endpoints

Model endpoints are configured in settings (`Inference.Endpoints`). These routes report them; edit them through `PUT /v1.0/settings`.

### Default endpoints

A settings file the server creates from scratch contains the first four endpoints below. The shipped `keyframe.json` files (`src/Keyframe.Server/keyframe.json`, `docker/keyframe.json`, and `docker/factory/keyframe.json`) also contain `llama-listen`, and the two Docker files add `vllm-voxtral`. There is no mock endpoint by default; add one with `"Provider": "Mock"` for offline demos and tests. Nothing is contacted until it is used or health-checked, and an unreachable endpoint only shows as unhealthy; the server still starts.

| Name | Provider | Kind | Endpoint (local / Docker) | Model | MaxOutputTokens | TimeoutMs |
|---|---|---|---|---|---|---|
| `ollama-gemma3-4b` | `Ollama` | Completion (vision) | `http://127.0.0.1:11434` / `http://host.docker.internal:11434` | `gemma3:4b` | 4096 | 300000 |
| `ollama-qwen3-vl-8b` | `Ollama` | Completion (vision) | same | `qwen3-vl:8b` | 4096 | 300000 |
| `ollama-gpt-oss-20b` | `Ollama` | Completion (text) | same | `gpt-oss:20b` | 4096 | 300000 |
| `whisper` | `OpenAiCompatible` | Transcription | `http://127.0.0.1:8000/v1` / `http://keyframe-whisper:8000/v1` | `Systran/faster-whisper-small` | 2048 | 120000 |
| `llama-listen` | `OpenAiCompatible` | Completion (listening) | `http://127.0.0.1:8720/v1` / `http://host.docker.internal:8720/v1` | `voxtral-mini-3b` | 4096 | 600000 |
| `vllm-voxtral` | `OpenAiCompatible` | Completion (listening), inactive | Docker only: `http://keyframe-vllm:8000/v1` | `mistralai/Voxtral-Mini-3B-2507` | 4096 | 300000 |

`Inference.DefaultVision` is `ollama-gemma3-4b`, `DefaultText` is `ollama-gpt-oss-20b`, and `DefaultTranscription` is `whisper`. `DefaultListening` is `llama-listen` in the shipped files and null in a settings file created from scratch, which makes every listening request fall back with `NoEndpointConfigured` until you set it. The Docker stack runs Whisper as the `keyframe-whisper` service (speaches) and reaches the host's Ollama and llama-server through `host.docker.internal`; pull the models with `ollama pull gemma3:4b qwen3-vl:8b gpt-oss:20b`, and start the listening server with `llama-server -hf ggml-org/Voxtral-Mini-3B-2507-GGUF:Q4_K_M --port 8720`.

`llama-listen` has `MaxConcurrentRequests` 1, a 600000 ms timeout, and `MaxListenSeconds` 120, so a small model on a CPU hears longer recordings as excerpts or per-section clips instead of one long prefill. `vllm-voxtral` points at the `keyframe-vllm` container of the `listen-gpu` compose profile (`MaxConcurrentRequests` 4). It ships inactive so a stack without a GPU does not probe a missing container every 15 seconds; activate it and set `DefaultListening` to it when you run that profile. Every endpoint has `MaxInlineMediaBytes` 20971520 and, except `llama-listen`, a null `MaxListenSeconds`.

### GET /v1.0/endpoints

Key. Response `200`: array of [`EndpointResponse`](#endpointresponse). API keys are never returned; `ApiKeySet` and `ApiKeyLast4` say whether one is configured.

```json
[
  {
    "Name": "ollama-gemma3-4b", "Provider": "Ollama", "Kind": "Completion", "Endpoint": "http://127.0.0.1:11434",
    "ApiKeySet": false, "ApiKeyLast4": null, "Model": "gemma3:4b",
    "MaxConcurrentRequests": 2, "TimeoutMs": 300000, "Active": true, "IsDefault": true,
    "ProviderAvailable": true, "SupportsImages": true, "SupportsStructuredOutput": false, "SupportsTranscription": false, "SupportsAudioInput": false,
    "Healthy": true, "HealthMessage": "OK", "LastCheckedUtc": "2026-11-16T01:41:50.002Z"
  },
  {
    "Name": "llama-listen", "Provider": "OpenAiCompatible", "Kind": "Completion", "Endpoint": "http://127.0.0.1:8720/v1",
    "ApiKeySet": false, "ApiKeyLast4": null, "Model": "voxtral-mini-3b",
    "MaxConcurrentRequests": 1, "TimeoutMs": 600000, "Active": true, "IsDefault": true,
    "ProviderAvailable": true, "SupportsImages": true, "SupportsStructuredOutput": false, "SupportsTranscription": true, "SupportsAudioInput": true,
    "Healthy": false, "HealthMessage": "Connection refused (127.0.0.1:8720)", "LastCheckedUtc": "2026-11-17T20:11:38.663Z"
  }
]
```

`ProviderAvailable` is `false` for providers this build cannot call for the endpoint's kind (in 0.1.0: `VertexAi` and `Bedrock`, and transcription on anything other than `Mock`, `OpenAi`, `AzureOpenAi`, and `OpenAiCompatible`). `SupportsImages`, `SupportsStructuredOutput` (a native JSON response format is requested for extraction), `SupportsTranscription`, and `SupportsAudioInput` describe the provider, not the specific model. `SupportsAudioInput` is `true` for completion endpoints whose provider accepts audio on chat (`Gemini`, `OpenAiCompatible`, and `Mock`), which are the endpoints that can listen; the model you name must also understand audio. It is `false` for transcription endpoints.

`IsDefault` is true for the default vision, text, transcription, and listening endpoints (`Inference.DefaultVision`, `DefaultText`, `DefaultTranscription`, `DefaultListening`). Health is probed for every active endpoint within a second of startup, then again after `Inference.HealthProbeIntervalMs` (default 300000, five minutes) for an endpoint whose last probe succeeded, or after `Inference.UnhealthyProbeIntervalMs` (default 15000) for one that failed. Endpoints are probed concurrently. A scheduled probe skips an endpoint whose concurrency gate is full of real work, because a busy endpoint is reachable, and keeps its last state, so a single-slot CPU listening server never looks unhealthy just because it is working. A real completion call that gets no HTTP response marks its endpoint unhealthy immediately, without waiting for a probe. Transcription endpoints are not checked over the network: their probe and test only confirm that a client can be built.

### POST /v1.0/endpoints/{name}/test

Admin. Sends a short prompt to a completion endpoint (transcription endpoints are checked for a client that builds) and records the outcome as the endpoint's health. Any 2xx reply counts as healthy, including an empty reply from a reasoning model that spent the small token budget thinking. A missing `ApiKey` or `Endpoint` URL is reported in `Error`.

```json
{ "Name": "ollama-gemma3-4b", "Success": true, "DurationMs": 412.7, "Model": "gemma3:4b", "Error": null }
```

`404` when no endpoint has that name.

---

## Analytics

Every finished image, video, and audio analysis (success or failure, including cancellation after it started) is stored as one analysis row plus one row per model call attempt, written in the background after the response. These routes read and delete them; all need the **admin** key. `Analytics.Enable` (default `true`) and `Analytics.RetentionDays` (default 90, 1 to 3650) apply live. How each number is computed is described in [TELEMETRY.md](TELEMETRY.md#analytics-api-and-computation).

### GET /v1.0/analytics/summary

Admin. Statistics over the analyses that completed in `[fromUtc, toUtc)`.

| Query | Type | Notes |
|---|---|---|
| `fromUtc`, `toUtc` | ISO 8601 | Default: the last 24 hours. `fromUtc` is aligned down to the bucket width |
| `bucket` | string | `minute`, `hour`, or `day`. Default by range: minute up to 2 hours, hour up to 7 days, otherwise day. More than 10000 buckets is `400` |
| `mediaKind` | string | `Image`, `Video`, or `Audio` |
| `endpoint` | string | Analyses that used this endpoint (vision, text, or transcription; an audio analysis records its listening endpoint as its vision endpoint); model call statistics only for calls to it |
| `model` | string | The same, by model name |

Response `200`: [`AnalyticsSummary`](#analyticssummary). Every bucket in the range is present, gap-filled with zero counts and `null` distributions.

```bash
curl -H "x-api-key: keyframeadmin" "http://127.0.0.1:8700/v1.0/analytics/summary?bucket=hour&mediaKind=Audio"
```

```json
{
  "FromUtc": "2026-11-15T04:00:00Z", "ToUtc": "2026-11-16T04:32:58.309Z", "Bucket": "hour", "MediaKind": null, "Endpoint": null, "Model": null,
  "Totals": {
    "Count": 2, "Succeeded": 2, "Failed": 0, "SuccessRate": 1,
    "AnalysisMs": { "Count": 2, "Min": 1910.2, "Max": 5112.4, "Avg": 3511.3, "P50": 3511.3, "P90": 4792.18, "P95": 4952.29, "P99": 5080.38, "StdDev": 1601.1, "Sum": 7022.6 },
    "QueueWaitMs": { "Count": 2, "...": "..." },
    "MediaDurationMs": { "Count": 1, "...": "..." },
    "RealtimeFactor": { "Count": 1, "Min": 0.795, "Max": 0.795, "Avg": 0.795, "P50": 0.795, "P90": 0.795, "P95": 0.795, "P99": 0.795, "StdDev": 0, "Sum": 0.795 },
    "MediaBytes": { "Count": 2, "...": "..." },
    "ModelCalls": 5, "FailedModelCalls": 0, "Retries": 0, "RepairAttempts": 0,
    "ModelCallMs": { "Count": 5, "...": "..." },
    "TimeToFirstTokenMs": { "Count": 4, "Min": 162.584, "Max": 1997.08, "Avg": 1097.935, "P50": 1116.038, "P90": 1903.834, "P95": 1950.457, "P99": 1987.755, "StdDev": 763.922, "Sum": 4391.74 },
    "GenerationMs": { "Count": 4, "...": "..." },
    "TokensPerSecond": { "Count": 4, "Min": 117.171, "Max": 159.062, "Avg": 136.114, "P50": 134.111, "P90": 156.466, "P95": 157.764, "P99": 158.802, "StdDev": 18.873, "Sum": 544.455 },
    "PromptTokens": 1322, "CompletionTokens": 622, "TotalTokens": 1944, "CachedTokens": 0, "ReasoningTokens": 0,
    "TokensPerAnalysis": { "Count": 2, "...": "..." },
    "InputImages": 2, "InputImageBytes": 61830, "InputAudioBytes": 98412, "Scenes": 0, "TranscriptCharacters": 106
  },
  "ByMediaKind": { "Image": { "Count": 1, "...": "..." }, "Video": { "Count": 0, "...": "..." }, "Audio": { "Count": 1, "...": "..." } },
  "Buckets": [ { "StartUtc": "2026-11-15T04:00:00Z", "Stats": { "Count": 0, "AnalysisMs": null, "...": "..." } } ],
  "ByModel": [ { "Endpoint": "ollama-gemma3-4b", "Provider": "Ollama", "Model": "gemma3:4b", "Calls": 2, "Failed": 0, "DurationMs": { "...": "..." }, "TimeToFirstTokenMs": { "...": "..." }, "GenerationMs": { "...": "..." }, "TokensPerSecond": { "...": "..." }, "PromptTokens": 1084, "CompletionTokens": 222 } ],
  "ByStage": [ { "Stage": "Resolve", "DurationMs": { "...": "..." } }, { "Stage": "Transcribe", "DurationMs": { "...": "..." } } ],
  "ByPurpose": [ { "Purpose": "Summarize", "Calls": 2, "Failed": 0, "DurationMs": { "...": "..." }, "TimeToFirstTokenMs": { "...": "..." } } ]
}
```

### GET /v1.0/analytics/analyses

Admin. Stored analyses, newest first by completion time, without their model calls (`Calls` is `null`).

| Query | Type | Notes |
|---|---|---|
| `mediaKind` | string | `Image`, `Video`, or `Audio` |
| `success` | boolean | `true` or `false` |
| `fromUtc`, `toUtc` | ISO 8601 | Inclusive lower and exclusive upper bound on completion time |
| `maxResults` | integer | 1 to 1000, default 50 (`pageSize` is accepted too) |
| `skip` | integer | Rows to skip, default 0 (or `pageNumber`, from 1) |

Response `200`: `EnumerationResult<AnalysisRecord>` (the same envelope as jobs and request history; `PageNumber` is derived from `skip`).

### GET /v1.0/analytics/analyses/{id}

Admin. One [`AnalysisRecord`](#analysisrecord) with `Calls`: every model call attempt as a [`ModelCallRecord`](#modelcallrecord). The id is the analysis `Id` (`kfa_`) from the analysis response. `404` when unknown or pruned.

### DELETE /v1.0/analytics/analyses/{id}

Admin. Deletes one analysis and its model calls. Response `200`: `{ "DeletedCount": 1 }`. `404` when unknown.

### DELETE /v1.0/analytics/analyses

Admin. Deletes analyses (and their model calls) that completed before `olderThanUtc` (ISO 8601). Without `olderThanUtc`, deletes all of them. Response `200`: `{ "DeletedCount": 42 }`.

---

## Settings

### GET /v1.0/settings

Admin. Response `200`:

```json
{
  "Settings": { "Rest": { "Hostname": "127.0.0.1", "Port": 8700, "...": "..." }, "Auth": { "AdminApiKey": "****dmin", "AccessKeys": [ { "Name": "default", "Key": "****ault", "Active": true } ] }, "...": "..." },
  "Fields": [
    { "Path": "Rest.Port", "ApplyMode": "Restart", "OverriddenByEnvironment": false, "EnvironmentVariable": null, "Secret": false },
    { "Path": "Auth.AdminApiKey", "ApplyMode": "Live", "OverriddenByEnvironment": true, "EnvironmentVariable": "KEYFRAME_ADMIN_API_KEY", "Secret": true }
  ],
  "RestartRequired": false,
  "SettingsFile": "/app/data/keyframe.json"
}
```

Secrets (`Auth.AdminApiKey`, `Auth.AccessKeys[].Key`, `Database.Password`, `Inference.Endpoints[].ApiKey`) come back as `****` plus their last four characters, or `null` when unset. `ApplyMode` is `Restart` for `Rest`, `Database`, `Telemetry`, most of `Logging`, and the media tool paths; everything else applies live.

### PUT /v1.0/settings

Admin. Body: a complete settings object, normally the `Settings` value from `GET` with your edits. Send masked secrets back unchanged to keep the stored values; send a new value to replace one. The server validates (duplicate endpoint or key names, numeric ranges, masked values that match nothing), clamps numeric fields to their documented ranges, writes the file atomically, and applies live fields at once. Environment overrides (`KEYFRAME_ADMIN_API_KEY`, `KEYFRAME_ACCESS_KEY`) stay in effect and are never written to the file.

Response `200`: the saved `SettingsResponse`. `RestartRequired` becomes `true` when a restart-only field changed. `400` for invalid settings. Request bodies for this route are stored as `[redacted]` in request history.

### POST /v1.0/restart

Admin. Responds `202`, then the process exits cleanly. Under Docker Compose (`restart: unless-stopped`) the container relaunches and reloads settings from disk. Outside a supervisor, the server simply stops.

```json
{ "Accepted": true, "Message": "Restarting. The container runtime relaunches the server and settings are reloaded from disk." }
```

### Listening, measurement, and probe settings

Listening, measurements, and the health probe schedule added the settings below. Every one applies live (`ApplyMode: "Live"`): a saved change takes effect on the next request or probe without a restart. Numeric values outside the range are clamped on save. `Endpoints[].MaxInlineMediaBytes` also applies live, because a saved settings change replaces the endpoint object and the client cache rebuilds its client.

| Setting | Default | Range | Purpose |
|---|---|---|---|
| `Inference.DefaultListening` | null (`llama-listen` in the shipped `keyframe.json` files) | an endpoint name (not checked on save; an unknown, inactive, or non-audio name makes requests fall back) | Listening endpoint when a request does not name one in `Listen.Model`. Its provider must accept audio input (`Gemini`, `OpenAiCompatible`, `Mock`). Null means requests fall back with `NoEndpointConfigured` |
| `Inference.HealthProbeIntervalMs` | 300000 | 10000 to 3600000 | Re-probe interval for an endpoint whose last probe succeeded |
| `Inference.UnhealthyProbeIntervalMs` | 15000 | 1000 to 600000 | Re-probe interval for an endpoint that is unhealthy or not yet checked |
| `Inference.Endpoints[].MaxInlineMediaBytes` | 20971520 (20 MB) | 1048576 to 1073741824 | Largest inline media one call may carry, counted as encoded on the wire (base64 plus the data URL prefix). Passed to PolyPrompt; Keyframe shortens listening clips to fit |
| `Inference.Endpoints[].MaxListenSeconds` | null (uses `Media.MaxListenSeconds`; 120 on the shipped `llama-listen`) | 10 to 7200 | Listen budget for this endpoint: the longest audio sent as one clip and the most audio sent in one call |
| `Media.ListenFormat` | `Mp3` | `Mp3`, `Wav`, `Flac` | Default for `Listen.Rendition.Format` and `Scenes.Audio.Rendition.Format`. `OpenAiCompatible` endpoints accept only `Mp3` and `Wav` |
| `Media.ListenSampleRate` | 16000 | 8000 to 48000 | Default sample rate in Hz |
| `Media.ListenChannels` | 1 | 1 to 2 | Default channel count |
| `Media.ListenBitrateKbps` | 64 | 16 to 320 | Default mp3 bitrate; ignored for `Wav` and `Flac` |
| `Media.MaxListenSeconds` | 1200 | 10 to 7200 | Default listen budget, unless the endpoint sets its own `MaxListenSeconds` |
| `Media.MeasureSilenceDb` | -50 | -90 to -10 | Level in dBFS below which audio counts as silence in `Measurements` (independent of section detection) |
| `Media.MeasureMinSilenceMs` | 500 | 100 to 30000 | Shortest silence counted in `Measurements` |
| `Media.ClippingTruePeakDbtp` | -0.1 | -6 to 3 | `ClippingLikely` needs `TruePeakDbtp` at or above this |
| `Media.ClippingMinPeaksPerMinute` | 10 | 0 to 100000 | `ClippingLikely` also needs at least this many peak-level samples per minute of audio |
| `Media.MonoMaxSideDb` | -40 | -120 to 0 | A stereo source whose `StereoWidthDb` is at or below this is `EffectivelyMono` |

`Analytics.Enable` and `RequestHistory.Enable` are named `Enable`, like every other on/off member. The earlier `Enabled` spelling is not read, so a settings file that still uses it gets the default of `true` for those two.

---

## Request history

Every request except `OPTIONS` and the favicon is captured after the response is sent, written in the background so capture never slows a response. Headers named `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, or containing `api-key`, `token`, `secret`, or `password` are stored as `[redacted]`. Bodies are truncated to `RequestHistory.MaxRequestBodyBytes` and `MaxResponseBodyBytes` (default 64 KB each) with the original length kept. Settings request bodies are redacted, and response bodies of settings and request history routes, and binary responses (video, audio, and image media such as segments), are not stored; their byte counts are. Entries are pruned after `RequestHistory.RetentionDays` (default 30). With `RequestHistory.Enable` set to `false`, nothing is captured and the read routes return empty results.

Shared filter parameters:

| Query | Type | Notes |
|---|---|---|
| `method` | string | Case-insensitive |
| `statusCode` | integer | Exact match |
| `pathContains` | string | Substring of the path; `%` and `_` are literal |
| `principalName` | string | Access key name, or `admin` |
| `fromUtc`, `toUtc` | ISO 8601 | Inclusive lower and exclusive upper bound |

### GET /v1.0/api/request-history

Key. Filters plus `pageNumber` and `pageSize`. Newest first. Headers and bodies are omitted (`null`). Response `200`: `EnumerationResult<RequestHistoryEntry>`.

### GET /v1.0/api/request-history/summary

Key. Filters plus `bucketMinutes` (1 to 10080, default 60). Defaults to the last 24 hours. Buckets align to multiples of the bucket width, and every bucket in range is present, including empty ones.

```json
{
  "TotalCount": 15, "TotalSuccess": 13, "TotalFailure": 2, "AverageDurationMs": 41.7, "BucketMinutes": 60,
  "Buckets": [
    { "BucketStartUtc": "2026-11-15T02:00:00Z", "BucketEndUtc": "2026-11-15T03:00:00Z", "SuccessCount": 0, "FailureCount": 0, "AverageDurationMs": 0 }
  ]
}
```

### GET /v1.0/api/request-history/{id}

Key. Response `200`: one [`RequestHistoryEntry`](#requesthistoryentry) with headers and bodies. `404` when unknown.

### DELETE /v1.0/api/request-history/{id}

Admin. Response `204`. `404` when unknown.

### DELETE /v1.0/api/request-history

Admin. Deletes every entry matching the filters; with no filters, deletes everything. Response `200`: `{ "DeletedCount": 42 }`.

---

## Shared types

### MediaSource

| Property | Type | Notes |
|---|---|---|
| `Url` | string | HTTP or HTTPS. URLs that resolve to private, loopback, or link-local addresses are refused unless `Media.AllowPrivateNetworkUrls` is `true`; the check runs at connect time, so redirects cannot bypass it. |
| `Data` | string | Base64 media bytes |
| `MediaType` | string | Optional declared MIME type; magic-byte detection wins |

Exactly one of `Url` or `Data` is required. Downloads are capped by `Media.MaxDownloadBytes` (default 2 GB) and `Media.DownloadTimeoutMs`.

### Context

`Context` is optional background applied to every prompt the request produces: the summary, every field in both extraction modes, and for video every scene and the whole-video step. It is treated as data, never as instructions. It accepts any of these shapes:

```json
"Context": "A single string."
"Context": [ "One string", "Another string" ]
"Context": [ { "Label": "Location", "Text": "Shelter intake yard." }, "A plain string item" ]
```

Each item has an optional `Label` (up to 128 characters) and a required `Text`. At most 50 items and 20000 characters in total.

### ModelSelection

| Property | Type | Notes |
|---|---|---|
| `Endpoint` | string | Endpoint name from settings. Null uses the default for the call type (`Inference.DefaultVision`, `DefaultText`, `DefaultTranscription`, or `DefaultListening` for `Listen.Model`). |
| `Name` | string | Model override; null uses the endpoint's configured model |
| `Model` | string | Input-only alias of `Name`, so `{ "Endpoint": "whisper", "Model": "..." }` also works. Never returned |

### ImageAnalysisRequest

| Property | Type | Default | Notes |
|---|---|---|---|
| `Image` | MediaSource | | Required |
| `Context` | Context | `[]` | See above |
| `Model` | ModelSelection | null | Vision endpoint |
| `Functions` | FunctionsSpec | | `Summarize` and `Extract`; at least one must be enabled |
| `IncludeProperties` | boolean | true | Return `Properties` |

### VideoAnalysisRequest

| Property | Type | Default | Notes |
|---|---|---|---|
| `Video` | MediaSource | | Required |
| `Context` | Context | `[]` | |
| `Model` | ModelSelection | null | Vision endpoint for keyframe calls |
| `TextModel` | ModelSelection | null | When set, whole-video functions run text-only over scene results and transcript |
| `Functions` | VideoFunctionsSpec | | `Summarize`, `Extract`, `Scenes`, `Transcribe`; at least one must be enabled |
| `Wait` | boolean | false | See `POST /v1.0/video` |
| `WaitTimeoutMs` | integer | 120000 | 1000 to 3600000, also capped by `Jobs.MaxWaitTimeoutMs` |
| `SampleFrames` | integer | 8 | 1 to 64. Frames for whole-video functions when scenes are disabled |
| `IncludeKeyframeData` | boolean | false | Return base64 JPEG thumbnails (long edge `Media.ThumbnailMaxEdge`) in `Scenes[].Keyframes[].Data` |
| `IncludeProperties` | boolean | true | |

### AudioAnalysisRequest

| Property | Type | Default | Notes |
|---|---|---|---|
| `Audio` | MediaSource | | Required. Audio file, or a video file (audio track only) |
| `Context` | Context | `[]` | Included in the summary and every field prompt |
| `TranscriptionModel` | ModelSelection | null | Transcription endpoint and model; takes precedence over `Functions.Transcribe.Endpoint` and `Model`. Null uses `Inference.DefaultTranscription` |
| `Listen` | [ListenSpec](#listenspec-and-audiorenditionspec) | `Enable: true` | Whether the listening model hears the audio, which model, the rendition, and the excerpt sampling. Never null |
| `TextModel` | ModelSelection | null | Endpoint for Summarize and Extract on the transcript when the analysis does not listen, and for reducing section results to whole-recording results. Null uses `Inference.DefaultText`, then `DefaultVision` |
| `Functions` | AudioFunctionsSpec | | `Transcribe` (a [TranscribeSpec](#scenesspec-scenedetectionspec-sceneaudiospec-transcribespec), on by default), `Summarize` (on by default), `Extract` (off by default), `Measure` (a [MeasureSpec](#measurespec-sectionsspec-sectiondetectionspec), on by default), and `Sections` (a [SectionsSpec](#measurespec-sectionsspec-sectiondetectionspec), off by default). At least one must be enabled; Measure counts. Summarize, Extract, and section functions need Transcribe or `Listen.Enable` |
| `Wait` | boolean | false | See `POST /v1.0/audio` |
| `WaitTimeoutMs` | integer | 120000 | 1000 to 3600000, also capped by `Jobs.MaxWaitTimeoutMs` |
| `IncludeProperties` | boolean | true | |

When you send a `Functions` object, the sub-objects you include replace the defaults, so send `"Enable": true` on each function you want. A function you leave out keeps its default, so `Measure` stays on unless you send `"Measure": { "Enable": false }`.

Validation (`400`, every problem listed in `Details`) adds these rules for audio: section functions (`Functions.Sections.Summarize` or `Extract`) require `Functions.Sections.Enable`; `Sections.Detection.MaxSectionMs` must be at least `MinSectionMs`; section fields follow the same rules as top-level fields; and `Listen.Model` is checked like any other `ModelSelection`. The listening rejections are listed under [`POST /v1.0/audio`](#post-v10audio).

### ListenSpec and AudioRenditionSpec

`AudioAnalysisRequest.Listen`:

| Property | Type | Default | Notes |
|---|---|---|---|
| `Enable` | boolean | true | Send the audio itself to the listening model for Summarize, Extract, and per-section functions. Falls back to transcript-only, with `Listening.Listened: false`, when no audio-capable endpoint is usable |
| `Model` | ModelSelection | null | Listening endpoint and model. Null uses `Inference.DefaultListening`. Naming an endpoint without audio input is a `400`. Ignored unless `Enable` is true |
| `Rendition` | AudioRenditionSpec | all members null | Quality of the audio sent to the model. Never null |
| `SampleClips` | integer | 4 | 1 to 32. Excerpts sent when the recording is too long to send whole and the whole-recording results are not reduced from sections |
| `ClipSeconds` | integer | 30 | 5 to 300. Length of each excerpt; shortened when `SampleClips` times `ClipSeconds` exceeds the listen budget |

`AudioRenditionSpec`, used by `Listen.Rendition` on audio and `Scenes.Audio.Rendition` on video. Each null member uses its server default. In a response (`Listening.Rendition`) every member is filled with the effective value.

| Property | Type | Default when null | Notes |
|---|---|---|---|
| `Format` | string | `Media.ListenFormat` (`Mp3`) | `Mp3` (constant bitrate), `Wav` (16-bit PCM), or `Flac`. `OpenAiCompatible` endpoints accept only `Mp3` and `Wav` |
| `SampleRate` | integer | `Media.ListenSampleRate` (16000) | 8000 to 48000 Hz |
| `Channels` | integer | `Media.ListenChannels` (1) | 1 or 2 |
| `BitrateKbps` | integer | `Media.ListenBitrateKbps` (64) | 16 to 320. `Mp3` only; ignored for `Wav` and `Flac` |

Out-of-range numbers are clamped, not rejected.

```json
"Listen": {
  "Enable": true,
  "Model": { "Endpoint": "gemini-listen", "Name": "gemini-2.5-flash" },
  "Rendition": { "Format": "Mp3", "SampleRate": 44100, "Channels": 2, "BitrateKbps": 128 },
  "SampleClips": 6,
  "ClipSeconds": 20
}
```

### MeasureSpec, SectionsSpec, SectionDetectionSpec

`Functions.Measure` (MeasureSpec) has one member, `Enable` (default true). Measurements make no model calls.

`Functions.Sections` (SectionsSpec) is the audio counterpart of `ScenesSpec`:

| SectionsSpec | Default | Notes |
|---|---|---|
| `Enable` | false | Detect sections |
| `Detection.SilenceDb` | -40 | -90 to -10. Level in dBFS below which audio counts as silence |
| `Detection.MinSilenceMs` | 1500 | 100 to 30000. Shortest silence that can separate sections |
| `Detection.MinSectionMs` | 20000 | 0 to 600000. Shorter sections merge into a neighbor |
| `Detection.MaxSectionMs` | 600000 | 10000 to 3600000. Longer sections split evenly; must be at least `MinSectionMs` |
| `Detection.MaxSections` | 100 | 1 to 1000. Beyond this, the shortest adjacent pairs merge |
| `Summarize`, `Extract` | disabled | Per-section functions, same shape as everywhere else |

`Detection` is a SectionDetectionSpec. Out-of-range numbers are clamped.

### SegmentRequest

| Property | Type | Default | Notes |
|---|---|---|---|
| `Source` | MediaSource | | Required. The video or audio to cut |
| `StartMs` | integer | 0 | Start of the range, or the frame time for `Frame` |
| `EndMs` | integer | | Exclusive end. Required for `Video` and `Audio`; clamped to the media duration |
| `Output` | string | `Video` | `Video`, `Audio`, or `Frame` |
| `Format` | string | per output | `mp4`/`webm`, `mp3`/`wav`/`flac`/`m4a`/`ogg`, `jpg`/`png` |
| `MaxEdge` | integer | null | 16 to 4096; long-edge cap for video and frames |
| `ResponseFormat` | string | `Binary` | `Binary` or `Json` |

### SegmentResponse

`MediaType`, `Filename`, `StartMs`, `EndMs` (after clamping; equal to `StartMs` for frames), `DurationMs` (0 for frames), `SizeBytes`, and `Data` (base64).

### SummarizeSpec

| Property | Type | Default | Notes |
|---|---|---|---|
| `Enable` | boolean | false | |
| `Key` | string | `Summary` | Key in `Results`; must not collide with a field key |
| `MaxWords` | integer | 100 | 5 to 2000 |
| `Style` | string | `Paragraph` | `Paragraph`, `Bullets`, or `Caption` |
| `Instructions` | string | null | Up to 4000 characters |

### ExtractSpec and FieldSpec

`ExtractSpec` has `Enable` (default false), `Mode` (`Combined`, the default, asks for every field in one JSON object; `PerField` makes one call per field in parallel, costs more, and isolates failures), and `Fields`.

| FieldSpec property | Type | Notes |
|---|---|---|
| `Key` | string | Required, unique, 1 to 64 characters: letters, digits, underscore, hyphen, or period |
| `Type` | string | `Boolean`, `String`, `Integer`, `Number`, `Enum`, `StringList`, `Object` (default `String`) |
| `Prompt` | string | Required, up to 4000 characters |
| `Required` | boolean | When true, a missing or invalid value marks the analysis failed |
| `Default` | any | Returned when the model cannot answer; its JSON type follows `Type` |
| `Options` | string[] | Required for `Enum` |
| `Minimum`, `Maximum` | number | `Integer` and `Number` |
| `MaxLength` | integer | `String` |
| `MaxItems` | integer | `StringList` |
| `Schema` | string | Optional JSON Schema text for `Object` |

Coercion: `Boolean` accepts true/false, yes/no, 1/0. `Integer` accepts numeric strings and the words zero to twenty, and clamps to `Minimum`/`Maximum` with a warning. `Number` parses with the invariant culture. `Enum` matches options case-insensitively; anything else is invalid. `StringList` accepts a JSON array or a comma-separated string and is truncated to `MaxItems` with a warning. `String` is trimmed and truncated to `MaxLength`. `Object` passes through when it parses.

### ScenesSpec, SceneDetectionSpec, SceneAudioSpec, TranscribeSpec

| ScenesSpec | Default | Notes |
|---|---|---|
| `Enable` | false | |
| `Detection.Threshold` | 0.30 | 0.01 to 1.0; lower finds more cuts |
| `Detection.MinSceneMs` | 2000 | 0 to 600000; shorter scenes merge into a neighbor |
| `Detection.MaxSceneMs` | 60000 | 1000 to 3600000; longer scenes split evenly |
| `Detection.MaxScenes` | 200 | 1 to 2000; excess scenes merge |
| `FramesPerScene` | 3 | 1 to 10 |
| `Summarize`, `Extract` | disabled | Per-scene functions, same shape as above |
| `Audio.Enable` | true | Attach each scene's audio to that scene's calls when the vision endpoint accepts audio. Never a `400`; see [Scene audio](#scene-audio) |
| `Audio.Rendition` | all members null | An [AudioRenditionSpec](#listenspec-and-audiorenditionspec) with the same `Media.Listen*` defaults as audio |

`Audio` is a SceneAudioSpec and is never null.

| TranscribeSpec | Notes |
|---|---|
| `Enable` | Default false for video, true for audio. Audio is extracted, chunked, and sent to the transcription endpoint |
| `Endpoint`, `Model` | Null uses `Inference.DefaultTranscription` and its model |
| `Language` | ISO 639-1 code; null lets the model detect |
| `Prompt` | Spelling hints for names and terms |

A video without an audio track skips transcription with a warning rather than failing.

### AnalysisResponse

`ImageAnalysisResponse`, `VideoAnalysisResponse`, and `AudioAnalysisResponse` share these properties:

| Property | Type | Notes |
|---|---|---|
| `Id` | string | `kfa_` prefix. Also the id of the stored analytics record |
| `RequestId` | string | Id of the HTTP request that submitted the analysis (the `x-request-id` response header) |
| `Success` | boolean | See the image route |
| `MediaKind` | string | `Image`, `Video`, or `Audio` |
| `Properties` | MediaProperties | When `IncludeProperties` |
| `Results` | object | Caller-keyed map: the summary key plus every field key. The one schemaless value in the contract |
| `Fields` | FieldResult[] | `Key`, `Type`, `Value`, `Valid`, `RawValue`, `Error`, `Warnings[]` |
| `Telemetry` | AnalysisTelemetry | `StartUtc`, `EndUtc`, `TotalMs`, `QueueWaitMs` (job queued to started; 0 for images), `QueuedMs` (the same for jobs, null for images), `Stages[]` (`Name`, `StartUtc`, `DurationMs`, `Success`), `ModelCalls[]`, `Totals` |
| `Warnings`, `Errors` | string[] | |

`AudioAnalysisResponse` adds `Transcript` (same shape as video), `Listening` (a [ListeningInfo](#listeninginfo-and-listenclip), always present, including when `Listen.Enable` is false), `Measurements` (an [AudioMeasurements](#audiomeasurements), present when `Functions.Measure.Enable` is true and the measurement succeeded; a measurement failure adds a `Measure: ...` error and leaves it null), and `Sections` (a [SectionResult](#sectionresult) array, present when `Functions.Sections.Enable` is true). `VideoAnalysisResponse` adds `Transcript` (`Language`, `Text`, `Segments[]` with `StartMs`, `EndMs`, `Text`), `Scenes[]` (`Index`, `StartMs`, `EndMs`, `DurationMs`, `Keyframes[]` with `TimestampMs`, `Width`, `Height`, `Data`, then `TranscriptText`, `Results`, `Fields`, `Errors`), and `Listening` (a ListeningInfo, present whenever scenes are enabled, null otherwise).

Every `ModelCalls[]` entry records `Purpose` (`Summarize`, `Extract`, `SceneSummarize`, `SceneExtract`, `SectionSummarize`, `SectionExtract`, `Transcribe`; repair attempts are counted in `RepairAttempts` on the call they repaired), `Target` (field key, `scene:<index>`, `section:<index>`, or `chunk:<index>`), `Endpoint`, `Provider`, `Model`, `StartUtc`, `DurationMs` (request sent to last token, across every attempt including backoff and the endpoint gate), `QueueMs` (time waiting on the endpoint's concurrency gate, summed over attempts), `TimeToFirstTokenMs` (request sent to the first content or reasoning token; completion calls stream), `GenerationMs` (first token to last token), `TokensPerSecond` (`CompletionTokens / (GenerationMs / 1000)`, null when either is unknown), `Attempts`, `RepairAttempts`, `Success`, `Error`, `StatusCode`, `InputImages`, `InputImageBytes`, `InputAudioBytes` (the audio file for a transcription call, or the clips attached to a completion call, before base64), and token counts (`PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`), plus `FinishReason`. The timing fields come from the last attempt that reported them. Failed attempts are included. Audio stages are `Resolve`, `Probe`, `Measure`, `AudioExtraction`, `Transcribe`, `SectionDetection`, `ListenExtraction` (rendering the whole-recording clips), `Sections` (per-section clips and functions), `Summarize`, and `Extract`, each present only when it ran. Video stages are `Resolve`, `Probe`, `SceneDetection`, `FrameExtraction`, `AudioExtraction`, `Transcribe`, `Scenes` (including scene audio clips), `Summarize`, and `Extract`.

### ListeningInfo and ListenClip

`ListeningInfo` says whether, how, and by which model the audio was heard.

| Property | Type | Notes |
|---|---|---|
| `Listened` | boolean | True when at least one model call carried audio, including a call that then failed |
| `Mode` | string | What the whole-recording functions heard: `Full`, `Sections`, `Excerpts`, `Scenes` (video only), or `None`. `None` whenever `Listened` is false |
| `Reason` | string | Null when listening happened, otherwise `NotRequested`, `NoModelFunctions`, `NoEndpointConfigured`, `EndpointLacksAudioInput`, `EndpointUnhealthy`, or `NoAudioTrack`. A stable code; clients localize it |
| `Endpoint`, `Model` | string | The listening endpoint (on video, the vision endpoint) and model; null when not listened |
| `Rendition` | AudioRenditionSpec | The effective rendition, every member filled, even when nothing was heard |
| `DurationMs` | integer | Length of the media |
| `HeardMs` | integer | Length of the distinct audio the models heard; overlapping clips are counted once |
| `Clips` | ListenClip[] | Every clip rendered and sent, in order (recording clips, then section or scene clips by index). Empty when not listened |

`ListenClip`: `Scope` (`Recording` for the whole-recording Summarize and Extract, `Section`, or `Scene`), `Index` (the section or scene index; null for `Recording`), `StartMs` and `EndMs` (the clip's range in the source), and `SizeBytes` (the rendered clip before base64).

### AudioMeasurements

Deterministic measurements from ffmpeg. Levels are in dB relative to full scale. A level member is null when ffmpeg could not measure it (for example on digital silence).

| Property | Type | Notes |
|---|---|---|
| `IntegratedLufs` | number | Integrated loudness (EBU R128), LUFS |
| `LoudnessRangeLu` | number | Loudness range (EBU R128), LU |
| `TruePeakDbtp` | number | True peak, dBTP |
| `PeakDbfs`, `RmsDbfs` | number | Sample peak and RMS level, dBFS |
| `CrestFactorDb` | number | Peak level minus RMS level, dB |
| `PeakCount` | integer | Samples at the peak level, across channels |
| `ClippingLikely` | boolean | `TruePeakDbtp` at or above `Media.ClippingTruePeakDbtp` and `PeakCount` per minute at or above `Media.ClippingMinPeaksPerMinute`. The raw inputs are reported so you can apply your own threshold |
| `SilenceRatio` | number | Fraction of the recording that is silence, 0 to 1, using `Media.MeasureSilenceDb` and `Media.MeasureMinSilenceMs` |
| `SilenceCount` | integer | Silent stretches |
| `LeadingSilenceMs`, `TrailingSilenceMs` | integer | Silence at the start and end |
| `Channels` | integer | Channel count of the source |
| `StereoWidthDb` | number | Side-to-mid RMS level difference in dB for stereo sources; -120 means the channels are identical. Null for sources that are not stereo |
| `EffectivelyMono` | boolean | True for a mono source, or a stereo source whose `StereoWidthDb` is at or below `Media.MonoMaxSideDb` |

Tempo and key are not measured.

### SectionResult

`Index` (zero-based), `StartMs`, `EndMs`, `DurationMs`, `TranscriptText` (the transcript spoken in the section; null when transcription did not run), `Results` (keyed like the top-level `Results`), `Fields`, and `Errors`. The shape of a scene without `Keyframes`.

### MediaProperties

`MediaKind`, `ContentType`, `Format`, `SizeBytes`, `Sha256`, `Width`, `Height`, `DurationMs`, `FrameRate`, `BitRate`, `VideoCodec`, `HasAudio`, `AudioCodec`, `AudioChannels`, `AudioSampleRate`, and `Tags` (container and stream tags from ffprobe, such as camera make and creation time).

### JobResponse

`Id` (`kfj_` prefix), `Status`, `MediaKind`, `PrincipalName`, `SourceUrl` (null for inline media), `SizeBytes` and `DurationMs` (filled once the media is resolved and probed), `Progress` (`Stage`, `Completed`, `Total`, `Percent` from 0 to 100), `Error`, `CreatedUtc`, `StartedUtc`, `CompletedUtc`, and `ResultUrl` (set once `Succeeded`).

### EndpointResponse

`Name`, `Provider`, `Kind` (`Completion` or `Transcription`), `Endpoint`, `ApiKeySet`, `ApiKeyLast4`, `Model`, `MaxConcurrentRequests`, `TimeoutMs`, `Active`, `IsDefault`, `ProviderAvailable`, `SupportsImages`, `SupportsStructuredOutput`, `SupportsTranscription`, `SupportsAudioInput` (the provider accepts audio on chat, so the endpoint can listen: `Gemini`, `OpenAiCompatible`, `Mock`), `Healthy`, `HealthMessage`, `LastCheckedUtc`.

### RequestHistoryEntry

`Id` (`req_` prefix), `RequestId`, `PrincipalName`, `Method`, `Path`, `Url`, `StatusCode`, `Success` (status below 400), `DurationMs`, `SourceIp`, `RequestContentType`, `ResponseContentType`, `RequestHeaders`, `RequestBody`, `RequestBodyBytes`, `RequestBodyTruncated`, `ResponseHeaders`, `ResponseBody`, `ResponseBodyBytes`, `ResponseBodyTruncated`, `CreatedUtc`, `CompletedUtc`.

### AnalyticsSummary

`FromUtc`, `ToUtc`, `Bucket` (`minute`, `hour`, or `day`), `MediaKind`, `Endpoint`, `Model` (the filters, or null), `Totals` (StatBlock), `ByMediaKind` (map with `Image`, `Video`, and `Audio`, each a StatBlock), `Buckets[]` (`StartUtc`, `Stats`), `ByModel[]` (ModelStats), `ByStage[]` (`Stage`, `DurationMs`), `ByPurpose[]` (`Purpose`, `Calls`, `Failed`, `DurationMs`, `TimeToFirstTokenMs`).

**StatBlock**: `Count`, `Succeeded`, `Failed`, `SuccessRate` (0 to 1), distributions `AnalysisMs`, `QueueWaitMs`, `MediaDurationMs`, `RealtimeFactor` (AnalysisMs divided by MediaDurationMs, video and audio), `MediaBytes` (over analyses), counts `ModelCalls`, `FailedModelCalls`, `Retries`, `RepairAttempts`, distributions `ModelCallMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond` (over model call attempts), token sums `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`, distribution `TokensPerAnalysis`, and sums `InputImages`, `InputImageBytes`, `InputAudioBytes` (transcription audio plus audio clips attached to completion calls), `Scenes`, `TranscriptCharacters`.

**Distribution** (null when there are no samples): `Count`, `Min`, `Max`, `Avg`, `P50`, `P90`, `P95`, `P99`, `StdDev`, `Sum`.

**ModelStats**: `Endpoint`, `Provider`, `Model`, `Calls` (attempts), `Failed`, distributions `DurationMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond`, and sums `PromptTokens`, `CompletionTokens`.

### AnalysisRecord

`Id` (`kfa_`), `RequestId`, `JobId` (null for images), `MediaKind`, `PrincipalName`, `Success`, `Error`, `StartedUtc`, `CompletedUtc`, `AnalysisMs`, `QueueWaitMs`, `MediaDurationMs`, `MediaBytes`, `RealtimeFactor`, `VisionEndpoint`, `VisionModel` (for an audio analysis that listened, the listening endpoint and model), `TextEndpoint`, `TextModel`, `TranscriptionEndpoint`, `TranscriptionModel`, `ModelCalls`, `FailedModelCalls`, `Retries`, `RepairAttempts`, `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`, `InputImages`, `InputImageBytes`, `InputAudioBytes`, `Scenes`, `TranscriptCharacters`, `FieldCount`, `InvalidFields`, `Stages[]` (StageTiming), and `Calls` (ModelCallRecord[] when reading one analysis, null in listings).

### ModelCallRecord

One row per attempt: `Id` (`kfc_`), `AnalysisId`, `Purpose`, `Target`, `Endpoint`, `Provider`, `Model`, `Attempt` (from 1), `StartUtc`, `Success`, `Error`, `StatusCode`, `DurationMs`, `QueueMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond`, `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`, `InputImages`, `InputImageBytes`, `InputAudioBytes`, `FinishReason`.

### EnumerationResult

`Items`, `PageNumber`, `PageSize`, `TotalCount`, `TotalPages`.
