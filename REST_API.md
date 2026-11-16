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
  - [SegmentRequest](#segmentrequest)
  - [SegmentResponse](#segmentresponse)
  - [SummarizeSpec](#summarizespec)
  - [ExtractSpec and FieldSpec](#extractspec-and-fieldspec)
  - [ScenesSpec, SceneDetectionSpec, TranscribeSpec](#scenesspec-scenedetectionspec-transcribespec)
  - [AnalysisResponse](#analysisresponse)
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

### POST /v1.0/audio

Key. Queues an audio analysis job: the audio track is extracted with ffmpeg (16 kHz mono FLAC, split into `Media.TranscriptionChunkSeconds` chunks), every chunk is transcribed in parallel through the transcription endpoint, and the transcript is then summarized and fields are extracted from it with the text model (`TextModel`, else `Inference.DefaultText`, else `Inference.DefaultVision`). Accepts audio files (mp3, wav, flac, m4a or aac, ogg or opus, webm audio) and video files; for a video only the audio track is used, no frames are extracted, and the response carries a warning.

Defaults: `Transcribe` and `Summarize` are on and `Extract` is off, so `{ "Audio": { "Url": "..." } }` alone returns a transcript and a summary. `Summarize` and `Extract` need `Transcribe`, because they run on the transcript. When the transcript is empty (silence), they are skipped with a warning.

Request body: [`AudioAnalysisRequest`](#audioanalysisrequest).

```bash
curl -X POST http://127.0.0.1:8700/v1.0/audio \
  -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{
    "Audio": { "Url": "https://example.com/standup.mp3" },
    "Context": [ "Daily standup of the platform team." ],
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

Responses are the same as `POST /v1.0/video`: `202` with a [`JobResponse`](#jobresponse) (`MediaKind: "Audio"`) and `Location`; `200` with the full [`AudioAnalysisResponse`](#analysisresponse) when `Wait` is true and the job succeeds in time, or `200` with the job when it finished without succeeding; `400` for validation or an unknown endpoint (checked before queueing); `429` when the queue is full. A source with no audio stream, an image, or audio longer than `Media.MaxAudioDurationMs` (default 4 hours) fails the job with a `422`-class error in `Error`.

```json
{
  "Id": "kfa_mwirp8ky_8pV0Qe1yHn3cTt5KJwq",
  "RequestId": "rqi_mwirp8kt_Yd1vT0aQ2ZdE8wq2b0h",
  "Success": true,
  "MediaKind": "Audio",
  "Properties": { "MediaKind": "Audio", "ContentType": "audio/mpeg", "Format": "mp3", "SizeBytes": 52284, "DurationMs": 6432, "BitRate": 65028, "HasAudio": true, "AudioCodec": "mp3", "AudioChannels": 1, "AudioSampleRate": 22050, "Tags": {} },
  "Transcript": {
    "Language": "en",
    "Text": "Welcome to the quarterly planning meeting. Today, Alice and Bob will review the budget and the hiring plan.",
    "Segments": [ { "StartMs": 0, "EndMs": 6200, "Text": "Welcome to the quarterly planning meeting. Today, Alice and Bob will review the budget and the hiring plan." } ]
  },
  "Results": { "Summary": "The speaker opens a quarterly planning meeting where Alice and Bob will review the budget and hiring plan.", "speakerNames": [ "Alice", "Bob" ] },
  "Fields": [ { "Key": "speakerNames", "Type": "StringList", "Value": [ "Alice", "Bob" ], "Valid": true, "RawValue": "[\"Alice\",\"Bob\"]", "Error": null, "Warnings": [] } ],
  "Telemetry": {
    "TotalMs": 5112.4, "QueueWaitMs": 1.82, "QueuedMs": 1.82,
    "Stages": [ { "Name": "Resolve", "DurationMs": 3.1, "Success": true }, { "Name": "Probe", "DurationMs": 41.0, "Success": true }, { "Name": "AudioExtraction", "DurationMs": 60.2, "Success": true }, { "Name": "Transcribe", "DurationMs": 1330.9, "Success": true }, { "Name": "Summarize", "DurationMs": 1905.0, "Success": true }, { "Name": "Extract", "DurationMs": 3681.2, "Success": true } ],
    "ModelCalls": [
      { "Purpose": "Transcribe", "Target": "chunk:0", "Endpoint": "whisper", "Provider": "OpenAiCompatible", "Model": "Systran/faster-whisper-small", "DurationMs": 1328.63, "QueueMs": 0.01, "TimeToFirstTokenMs": null, "GenerationMs": null, "TokensPerSecond": null, "Attempts": 1, "Success": true, "InputAudioBytes": 98412 },
      { "Purpose": "Summarize", "Endpoint": "ollama-gpt-oss-20b", "Provider": "Ollama", "Model": "gpt-oss:20b", "DurationMs": 1903.36, "QueueMs": 0.0, "TimeToFirstTokenMs": 162.58, "GenerationMs": 1731.55, "TokensPerSecond": 117.81, "Attempts": 1, "Success": true, "PromptTokens": 238, "CompletionTokens": 204 }
    ],
    "Totals": { "ModelCalls": 3, "FailedModelCalls": 0, "ModelMs": 6911.4, "PromptTokens": 486, "CompletionTokens": 400, "TotalTokens": 886 }
  },
  "Warnings": [],
  "Errors": []
}
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

A new settings file (and the shipped `keyframe.json` files) contain these endpoints. There is no mock endpoint by default; add one with `"Provider": "Mock"` for offline demos and tests. Nothing is contacted until it is used or health-checked, and an unreachable endpoint only shows as unhealthy; the server still starts.

| Name | Provider | Kind | Endpoint (local / Docker) | Model | MaxOutputTokens | TimeoutMs |
|---|---|---|---|---|---|---|
| `ollama-gemma3-4b` | `Ollama` | Completion (vision) | `http://127.0.0.1:11434` / `http://host.docker.internal:11434` | `gemma3:4b` | 4096 | 300000 |
| `ollama-qwen3-vl-8b` | `Ollama` | Completion (vision) | same | `qwen3-vl:8b` | 4096 | 300000 |
| `ollama-gpt-oss-20b` | `Ollama` | Completion (text) | same | `gpt-oss:20b` | 4096 | 300000 |
| `whisper` | `OpenAiCompatible` | Transcription | `http://127.0.0.1:8000/v1` / `http://keyframe-whisper:8000/v1` | `Systran/faster-whisper-small` | 2048 | 120000 |

`Inference.DefaultVision` is `ollama-gemma3-4b`, `DefaultText` is `ollama-gpt-oss-20b`, and `DefaultTranscription` is `whisper`. The Docker stack runs Whisper as the `keyframe-whisper` service (speaches) and reaches the host's Ollama through `host.docker.internal`; pull the models with `ollama pull gemma3:4b qwen3-vl:8b gpt-oss:20b`.

### GET /v1.0/endpoints

Key. Response `200`: array of [`EndpointResponse`](#endpointresponse). API keys are never returned; `ApiKeySet` and `ApiKeyLast4` say whether one is configured.

```json
[
  {
    "Name": "ollama-gemma3-4b", "Provider": "Ollama", "Kind": "Completion", "Endpoint": "http://127.0.0.1:11434",
    "ApiKeySet": false, "ApiKeyLast4": null, "Model": "gemma3:4b",
    "MaxConcurrentRequests": 2, "TimeoutMs": 300000, "Active": true, "IsDefault": true,
    "ProviderAvailable": true, "SupportsImages": true, "SupportsStructuredOutput": false, "SupportsTranscription": false, "Healthy": true, "HealthMessage": "OK", "LastCheckedUtc": "2026-11-16T01:41:50.002Z"
  }
]
```

`ProviderAvailable` is `false` for providers this build cannot call for the endpoint's kind (in 0.1.0: `VertexAi` and `Bedrock`, and transcription on anything other than `Mock`, `OpenAi`, `AzureOpenAi`, and `OpenAiCompatible`). `SupportsImages`, `SupportsStructuredOutput` (a native JSON response format is requested for extraction), and `SupportsTranscription` describe the provider, not the specific model. Health is probed at startup and every five minutes.

### POST /v1.0/endpoints/{name}/test

Admin. Sends a short prompt to a completion endpoint (transcription endpoints are checked for a client that builds) and records the outcome as the endpoint's health. Any 2xx reply counts as healthy, including an empty reply from a reasoning model that spent the small token budget thinking. A missing `ApiKey` or `Endpoint` URL is reported in `Error`.

```json
{ "Name": "ollama-gemma3-4b", "Success": true, "DurationMs": 412.7, "Model": "gemma3:4b", "Error": null }
```

`404` when no endpoint has that name.

---

## Analytics

Every finished image, video, and audio analysis (success or failure, including cancellation after it started) is stored as one analysis row plus one row per model call attempt, written in the background after the response. These routes read and delete them; all need the **admin** key. `Analytics.Enabled` (default `true`) and `Analytics.RetentionDays` (default 90, 1 to 3650) apply live. How each number is computed is described in [TELEMETRY.md](TELEMETRY.md#analytics-api-and-computation).

### GET /v1.0/analytics/summary

Admin. Statistics over the analyses that completed in `[fromUtc, toUtc)`.

| Query | Type | Notes |
|---|---|---|
| `fromUtc`, `toUtc` | ISO 8601 | Default: the last 24 hours. `fromUtc` is aligned down to the bucket width |
| `bucket` | string | `minute`, `hour`, or `day`. Default by range: minute up to 2 hours, hour up to 7 days, otherwise day. More than 10000 buckets is `400` |
| `mediaKind` | string | `Image`, `Video`, or `Audio` |
| `endpoint` | string | Analyses that used this endpoint (vision, text, or transcription); model call statistics only for calls to it |
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

---

## Request history

Every request except `OPTIONS` and the favicon is captured after the response is sent, written in the background so capture never slows a response. Headers named `Authorization`, `Proxy-Authorization`, `Cookie`, `Set-Cookie`, or containing `api-key`, `token`, `secret`, or `password` are stored as `[redacted]`. Bodies are truncated to `RequestHistory.MaxRequestBodyBytes` and `MaxResponseBodyBytes` (default 64 KB each) with the original length kept. Settings request bodies are redacted, and response bodies of settings and request history routes, and binary responses (video, audio, and image media such as segments), are not stored; their byte counts are. Entries are pruned after `RequestHistory.RetentionDays` (default 30). With `RequestHistory.Enabled` set to `false`, nothing is captured and the read routes return empty results.

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
| `Endpoint` | string | Endpoint name from settings. Null uses the default for the call type (`Inference.DefaultVision`, `DefaultText`, `DefaultTranscription`). |
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
| `TextModel` | ModelSelection | null | Endpoint for Summarize and Extract on the transcript. Null uses `Inference.DefaultText`, then `DefaultVision` |
| `Functions` | AudioFunctionsSpec | | `Transcribe` (a [TranscribeSpec](#scenesspec-scenedetectionspec-transcribespec), on by default), `Summarize` (on by default), `Extract` (off by default). At least one must be enabled, and Summarize or Extract need Transcribe |
| `Wait` | boolean | false | See `POST /v1.0/audio` |
| `WaitTimeoutMs` | integer | 120000 | 1000 to 3600000, also capped by `Jobs.MaxWaitTimeoutMs` |
| `IncludeProperties` | boolean | true | |

When you send a `Functions` object, the sub-objects you include replace the defaults, so send `"Enable": true` on each function you want.

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

### ScenesSpec, SceneDetectionSpec, TranscribeSpec

| ScenesSpec | Default | Notes |
|---|---|---|
| `Enable` | false | |
| `Detection.Threshold` | 0.30 | 0.01 to 1.0; lower finds more cuts |
| `Detection.MinSceneMs` | 2000 | 0 to 600000; shorter scenes merge into a neighbor |
| `Detection.MaxSceneMs` | 60000 | 1000 to 3600000; longer scenes split evenly |
| `Detection.MaxScenes` | 200 | 1 to 2000; excess scenes merge |
| `FramesPerScene` | 3 | 1 to 10 |
| `Summarize`, `Extract` | disabled | Per-scene functions, same shape as above |

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

`AudioAnalysisResponse` adds `Transcript` (same shape as video). `VideoAnalysisResponse` adds `Transcript` (`Language`, `Text`, `Segments[]` with `StartMs`, `EndMs`, `Text`) and `Scenes[]` (`Index`, `StartMs`, `EndMs`, `DurationMs`, `Keyframes[]` with `TimestampMs`, `Width`, `Height`, `Data`, then `TranscriptText`, `Results`, `Fields`, `Errors`).

Every `ModelCalls[]` entry records `Purpose` (`Summarize`, `Extract`, `SceneSummarize`, `SceneExtract`, `Transcribe`; repair attempts are counted in `RepairAttempts` on the call they repaired), `Target` (field key or scene index), `Endpoint`, `Provider`, `Model`, `StartUtc`, `DurationMs` (request sent to last token, across every attempt including backoff and the endpoint gate), `QueueMs` (time waiting on the endpoint's concurrency gate, summed over attempts), `TimeToFirstTokenMs` (request sent to the first content or reasoning token; completion calls stream), `GenerationMs` (first token to last token), `TokensPerSecond` (`CompletionTokens / (GenerationMs / 1000)`, null when either is unknown), `Attempts`, `RepairAttempts`, `Success`, `Error`, `StatusCode`, `InputImages`, `InputImageBytes`, `InputAudioBytes`, and token counts (`PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`), plus `FinishReason`. The timing fields come from the last attempt that reported them. Failed attempts are included. Audio stages are `Resolve`, `Probe`, `AudioExtraction`, `Transcribe`, `Summarize`, and `Extract`. Video stages are `Resolve`, `Probe`, `SceneDetection`, `FrameExtraction`, `AudioExtraction`, `Transcribe`, `Scenes`, `Summarize`, and `Extract`.

### MediaProperties

`MediaKind`, `ContentType`, `Format`, `SizeBytes`, `Sha256`, `Width`, `Height`, `DurationMs`, `FrameRate`, `BitRate`, `VideoCodec`, `HasAudio`, `AudioCodec`, `AudioChannels`, `AudioSampleRate`, and `Tags` (container and stream tags from ffprobe, such as camera make and creation time).

### JobResponse

`Id` (`kfj_` prefix), `Status`, `MediaKind`, `PrincipalName`, `SourceUrl` (null for inline media), `SizeBytes` and `DurationMs` (filled once the media is resolved and probed), `Progress` (`Stage`, `Completed`, `Total`, `Percent` from 0 to 100), `Error`, `CreatedUtc`, `StartedUtc`, `CompletedUtc`, and `ResultUrl` (set once `Succeeded`).

### EndpointResponse

`Name`, `Provider`, `Kind` (`Completion` or `Transcription`), `Endpoint`, `ApiKeySet`, `ApiKeyLast4`, `Model`, `MaxConcurrentRequests`, `TimeoutMs`, `Active`, `IsDefault`, `ProviderAvailable`, `SupportsImages`, `SupportsStructuredOutput`, `SupportsTranscription`, `Healthy`, `HealthMessage`, `LastCheckedUtc`.

### RequestHistoryEntry

`Id` (`req_` prefix), `RequestId`, `PrincipalName`, `Method`, `Path`, `Url`, `StatusCode`, `Success` (status below 400), `DurationMs`, `SourceIp`, `RequestContentType`, `ResponseContentType`, `RequestHeaders`, `RequestBody`, `RequestBodyBytes`, `RequestBodyTruncated`, `ResponseHeaders`, `ResponseBody`, `ResponseBodyBytes`, `ResponseBodyTruncated`, `CreatedUtc`, `CompletedUtc`.

### AnalyticsSummary

`FromUtc`, `ToUtc`, `Bucket` (`minute`, `hour`, or `day`), `MediaKind`, `Endpoint`, `Model` (the filters, or null), `Totals` (StatBlock), `ByMediaKind` (map with `Image`, `Video`, and `Audio`, each a StatBlock), `Buckets[]` (`StartUtc`, `Stats`), `ByModel[]` (ModelStats), `ByStage[]` (`Stage`, `DurationMs`), `ByPurpose[]` (`Purpose`, `Calls`, `Failed`, `DurationMs`, `TimeToFirstTokenMs`).

**StatBlock**: `Count`, `Succeeded`, `Failed`, `SuccessRate` (0 to 1), distributions `AnalysisMs`, `QueueWaitMs`, `MediaDurationMs`, `RealtimeFactor` (AnalysisMs divided by MediaDurationMs, video and audio), `MediaBytes` (over analyses), counts `ModelCalls`, `FailedModelCalls`, `Retries`, `RepairAttempts`, distributions `ModelCallMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond` (over model call attempts), token sums `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`, distribution `TokensPerAnalysis`, and sums `InputImages`, `InputImageBytes`, `InputAudioBytes`, `Scenes`, `TranscriptCharacters`.

**Distribution** (null when there are no samples): `Count`, `Min`, `Max`, `Avg`, `P50`, `P90`, `P95`, `P99`, `StdDev`, `Sum`.

**ModelStats**: `Endpoint`, `Provider`, `Model`, `Calls` (attempts), `Failed`, distributions `DurationMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond`, and sums `PromptTokens`, `CompletionTokens`.

### AnalysisRecord

`Id` (`kfa_`), `RequestId`, `JobId` (null for images), `MediaKind`, `PrincipalName`, `Success`, `Error`, `StartedUtc`, `CompletedUtc`, `AnalysisMs`, `QueueWaitMs`, `MediaDurationMs`, `MediaBytes`, `RealtimeFactor`, `VisionEndpoint`, `VisionModel`, `TextEndpoint`, `TextModel`, `TranscriptionEndpoint`, `TranscriptionModel`, `ModelCalls`, `FailedModelCalls`, `Retries`, `RepairAttempts`, `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`, `InputImages`, `InputImageBytes`, `InputAudioBytes`, `Scenes`, `TranscriptCharacters`, `FieldCount`, `InvalidFields`, `Stages[]` (StageTiming), and `Calls` (ModelCallRecord[] when reading one analysis, null in listings).

### ModelCallRecord

One row per attempt: `Id` (`kfc_`), `AnalysisId`, `Purpose`, `Target`, `Endpoint`, `Provider`, `Model`, `Attempt` (from 1), `StartUtc`, `Success`, `Error`, `StatusCode`, `DurationMs`, `QueueMs`, `TimeToFirstTokenMs`, `GenerationMs`, `TokensPerSecond`, `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens`, `InputImages`, `InputImageBytes`, `InputAudioBytes`, `FinishReason`.

### EnumerationResult

`Items`, `PageNumber`, `PageSize`, `TotalCount`, `TotalPages`.
