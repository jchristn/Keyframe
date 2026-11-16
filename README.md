<p align="center">
  <img src="assets/logo-dark.png" alt="Keyframe" height="192" />
</p>

<p align="center">
  <b>v0.1.0 - ALPHA</b> - API and structure may change without notice
</p>

# Keyframe

Keyframe generates summaries and extracts metadata from images, video, and audio. You post a picture, a clip, or a recording, tell it what you want to know, and it hands back a summary plus a dictionary of typed answers keyed the way you asked. For video it also breaks the footage into scenes, summarizes and answers questions per scene, and can line the results up against a transcript of the audio. For audio it transcribes the recording with timestamps and answers your questions from the transcript. Any scene or transcript segment can be cut back out of the source as a clip, an audio file, or a still. Every response carries its own telemetry (how long each stage took, every model call made, time to first token, generation speed, and tokens), and every analysis is kept for an analytics API with percentiles over time.

> **Status.** Keyframe is in active development. The media pipeline, analysis engine, persistence on four databases, and the REST server are built and tested. Model calls go through [PolyPrompt](https://github.com/jchristn/PolyPrompt) 5.0 (Ollama, OpenAI, Azure OpenAI, OpenAI-compatible servers, Anthropic, Gemini, xAI, Cohere), with a deterministic mock provider for tests. The shipped defaults use local Ollama models and a local Whisper server. See [KEYFRAME_PLAN.md](KEYFRAME_PLAN.md) for the roadmap and current phase.

## Why Keyframe

Most media metadata work ends up as a pile of one-off scripts: something to pull frames, something to call a vision model, something to coax the answer into JSON, and nothing that tells you what it cost. Keyframe puts that behind one API with one request shape.

- **Ask for exactly what you need.** Define fields with a key, a type (`Boolean`, `String`, `Integer`, `Number`, `Enum`, `StringList`, `Object`), and a prompt. Keyframe returns values coerced to those types, with per-field validity and warnings when it had to clamp or truncate.
- **Video without the plumbing.** Scene boundaries come from ffmpeg, so they are exact to the frame. Models describe what is in each scene; they are never trusted to decide where a cut is.
- **Cheap work first.** Media properties, scene detection, and frame extraction are deterministic and run before any model is called. Model calls happen only for the functions you enable.
- **See where the time and tokens went.** Stage timings, per-call latency, attempts, and token counts come back in every response, and the same signals flow to Prometheus, Tempo, and Grafana.

## Example

```json
POST /v1.0/image
{
  "Image": { "Url": "https://example.com/backyard.jpg" },
  "Context": [
    { "Label": "Source", "Text": "Photos submitted by pet adoption volunteers." },
    { "Label": "Location", "Text": "Shelter intake yard." }
  ],
  "Functions": {
    "Summarize": { "Enable": true, "Key": "Summary", "MaxWords": 60 },
    "Extract": {
      "Enable": true,
      "Fields": [
        { "Key": "hasCats", "Type": "Boolean", "Prompt": "Does this image have cats?" },
        { "Key": "dogCount", "Type": "Integer", "Prompt": "How many dogs do you see in this image?" }
      ]
    }
  }
}
```

Every `Context` item is included in every prompt the request produces, so background you supply once applies to the summary and to each field. The response includes `Results` (`{ "Summary": "...", "hasCats": false, "dogCount": 2 }`), a `Fields` array with per-field detail, the media `Properties`, and `Telemetry`. Video requests (`POST /v1.0/video`) add `Scenes` and `Transcribe` functions, and audio requests (`POST /v1.0/audio`) transcribe and then summarize and extract from the transcript; both run as background jobs. The full contract is in [REST_API.md](REST_API.md).

## Architecture

```text
            request (URL or base64)
                     |
               MediaResolver  ---- SSRF guard, size limits
                     |
               MediaProber    ---- ffprobe: dimensions, duration, codecs, tags
                     |
     +---------------+-----------------+------------------+
     |                                 |                  |
  image                              video              audio
     |                                 |                  |
ImageNormalizer             SceneDetector,          AudioExtractor
     |                      FrameExtractor,               |
     |                      AudioExtractor                |
     |                                 |                  |
     +---------- IInferenceClient -----+---- ITranscriptionClient
           (PolyPrompt, streamed; or mock)
                     |
   Results + Fields + Scenes + Transcript + Telemetry
                     |
        analytics rows (analyses, modelcalls)
```

| Project | Purpose |
|---|---|
| `src/Keyframe.Core` | Contracts, media pipeline, inference seam, analysis engine, persistence, metrics and spans |
| `src/Keyframe.Server` | Watson 7 REST server, authentication, jobs, request history, settings API |
| `src/Keyframe.McpServer` | MCP server on Voltaic that exposes Keyframe to agents ([MCP_API.md](MCP_API.md)) |
| `src/Test.*` | Touchstone suites shared by the automated, xUnit, and NUnit runners |
| `dashboard/` | React dashboard on port 8701 ([dashboard/README.md](dashboard/README.md)) |
| `sdk/` | C#, JavaScript, and Python SDKs, each with a test harness |
| `docker/` | Compose stack with Prometheus, Tempo, Loki, and Grafana ([TELEMETRY.md](TELEMETRY.md)) |

## Getting started

Keyframe needs the .NET 10 SDK and ffmpeg (with ffprobe) on the PATH.

```bash
git clone https://github.com/jchristn/Keyframe.git
cd Keyframe
dotnet build src/Keyframe.sln -c Release
dotnet run --project src/Test.Automated -c Release
```

Run the server. It creates `keyframe.json` with defaults the first time, listens on `http://127.0.0.1:8700`, and stores data in SQLite under `data/`. The default endpoints (see [Default settings](#default-settings)) expect Ollama on `127.0.0.1:11434` and a Whisper server on `127.0.0.1:8000`; the server starts either way and reports unreachable endpoints as unhealthy.

```bash
ollama pull gemma3:4b && ollama pull qwen3-vl:8b && ollama pull gpt-oss:20b

# Speech to text (speaches, OpenAI-compatible). It does not download models on its own:
docker run -d --name keyframe-whisper -p 127.0.0.1:8000:8000 -e ENABLE_UI=false \
  -v whisper-models:/home/ubuntu/.cache/huggingface/hub ghcr.io/speaches-ai/speaches:0.8.3-cpu
curl -X POST http://127.0.0.1:8000/v1/models/Systran/faster-whisper-small

cd src/Keyframe.Server
dotnet run -c Release
```

Then try it:

```bash
curl http://127.0.0.1:8700/v1.0/health
curl -H "x-api-key: default" -H "Content-Type: application/json" \
  -d '{"Image":{"Url":"https://upload.wikimedia.org/wikipedia/commons/3/3a/Cat03.jpg"},"Functions":{"Summarize":{"Enable":true}}}' \
  http://127.0.0.1:8700/v1.0/image
```

The default keys are `default` (access) and `keyframeadmin` (admin). Change both before the server is reachable by anyone else, or set `KEYFRAME_ACCESS_KEY` and `KEYFRAME_ADMIN_API_KEY`. The full API is in [REST_API.md](REST_API.md), the live OpenAPI document is at `/openapi.json`, and a Postman collection is in [assets/postman](assets/postman/Keyframe.postman_collection.json).

## API overview

Every route, grouped as in [REST_API.md](REST_API.md#contents). Auth is `none` (anonymous), `key` (any access key or the admin key), or `admin` (the admin key only).

| Group | Method | Path | Auth | Purpose |
|---|---|---|---|---|
| [System](REST_API.md#system) | `HEAD`, `GET` | [`/`](REST_API.md#get-) | none | Liveness and the root page |
| | `GET` | [`/favicon.ico`](REST_API.md#get-faviconico) | none | Favicon |
| | `GET` | [`/v1.0/health`](REST_API.md#get-v10health) | none | Version, uptime, ffmpeg, database, endpoint health |
| | `GET` | [`/v1.0/whoami`](REST_API.md#get-v10whoami) | key | The key you are using and whether it is admin |
| [Analysis](REST_API.md#analysis) | `POST` | [`/v1.0/image`](REST_API.md#post-v10image) | key | Summarize an image and extract typed fields (synchronous) |
| | `POST` | [`/v1.0/video`](REST_API.md#post-v10video) | key | Queue a video job: scenes, transcript, summaries, fields |
| | `POST` | [`/v1.0/audio`](REST_API.md#post-v10audio) | key | Queue an audio job: transcript, then summary and fields from it |
| | `POST` | [`/v1.0/probe`](REST_API.md#post-v10probe) | key | Media properties from ffprobe, no model calls |
| [Segments](REST_API.md#segments) | `POST` | [`/v1.0/segment`](REST_API.md#post-v10segment) | key | Cut a video clip, audio clip, or frame out of a source |
| [Jobs](REST_API.md#jobs) | `GET` | [`/v1.0/jobs`](REST_API.md#get-v10jobs) | key | List jobs with filters and paging |
| | `GET` | [`/v1.0/jobs/{id}`](REST_API.md#get-v10jobsid) | key | Job status and progress |
| | `GET` | [`/v1.0/jobs/{id}/result`](REST_API.md#get-v10jobsidresult) | key | The finished video or audio result |
| | `POST` | [`/v1.0/jobs/{id}/cancel`](REST_API.md#post-v10jobsidcancel) | key | Cancel a queued or running job |
| | `DELETE` | [`/v1.0/jobs/{id}`](REST_API.md#delete-v10jobsid) | key | Delete a finished job and its result |
| [Endpoints](REST_API.md#endpoints) | `GET` | [`/v1.0/endpoints`](REST_API.md#get-v10endpoints) | key | Configured model endpoints with capabilities and health |
| | `POST` | [`/v1.0/endpoints/{name}/test`](REST_API.md#post-v10endpointsnametest) | admin | Probe one endpoint now |
| [Analytics](REST_API.md#analytics) | `GET` | [`/v1.0/analytics/summary`](REST_API.md#get-v10analyticssummary) | admin | Percentiles, gap-filled buckets, by media kind, model, stage, purpose |
| | `GET` | [`/v1.0/analytics/analyses`](REST_API.md#get-v10analyticsanalyses) | admin | Stored analyses, newest first |
| | `GET` | [`/v1.0/analytics/analyses/{id}`](REST_API.md#get-v10analyticsanalysesid) | admin | One analysis with every model call attempt |
| | `DELETE` | [`/v1.0/analytics/analyses/{id}`](REST_API.md#delete-v10analyticsanalysesid) | admin | Delete one analysis |
| | `DELETE` | [`/v1.0/analytics/analyses`](REST_API.md#delete-v10analyticsanalyses) | admin | Delete analyses older than a cutoff |
| [Settings](REST_API.md#settings) | `GET` | [`/v1.0/settings`](REST_API.md#get-v10settings) | admin | Settings with secrets masked, plus field metadata |
| | `PUT` | [`/v1.0/settings`](REST_API.md#put-v10settings) | admin | Validate, save, and apply settings |
| | `POST` | [`/v1.0/restart`](REST_API.md#post-v10restart) | admin | Exit cleanly so the container restarts |
| [Request history](REST_API.md#request-history) | `GET` | [`/v1.0/api/request-history`](REST_API.md#get-v10apirequest-history) | key | Captured requests, newest first |
| | `GET` | [`/v1.0/api/request-history/summary`](REST_API.md#get-v10apirequest-historysummary) | key | Bucketed success and failure counts |
| | `GET` | [`/v1.0/api/request-history/{id}`](REST_API.md#get-v10apirequest-historyid) | key | One request with headers and bodies |
| | `DELETE` | [`/v1.0/api/request-history/{id}`](REST_API.md#delete-v10apirequest-historyid) | admin | Delete one entry |
| | `DELETE` | [`/v1.0/api/request-history`](REST_API.md#delete-v10apirequest-history) | admin | Delete entries matching a filter |

The request and response types are listed under [Shared types](REST_API.md#shared-types). The OpenAPI document at `/openapi.json` is generated from the same route metadata.

### Retrieving segments of audio and video

Analysis results carry exact time ranges: `Scenes[i].StartMs` and `EndMs` for video, and `Transcript.Segments[i].StartMs` and `EndMs` for video and audio. `POST /v1.0/segment` cuts that range back out of the source as a video clip (`Video`), its audio (`Audio`), or a still frame at `StartMs` (`Frame`). Keyframe does not keep uploaded media, so send the same `Source` again: the same `Url`, or the same base64 `Data`.

1. Analyze the media and wait for the result.

   ```bash
   curl -s -X POST http://127.0.0.1:8700/v1.0/video \
     -H "x-api-key: default" -H "Content-Type: application/json" \
     -d '{ "Video": { "Url": "https://example.com/talk.mp4" }, "Wait": true,
           "Functions": { "Scenes": { "Enable": true, "Summarize": { "Enable": true } }, "Transcribe": { "Enable": true } } }' \
     -o result.json
   ```

   For a recording, use `POST /v1.0/audio` with `{ "Audio": { "Url": "https://example.com/standup.mp3" }, "Wait": true }`.

2. Read a range from the result, for example with jq.

   ```bash
   jq '.Scenes[2] | { StartMs, EndMs }' result.json               # { "StartMs": 12000, "EndMs": 18500 }
   jq '.Transcript.Segments[5] | { StartMs, EndMs, Text }' result.json
   ```

3. Fetch the segment. The binary form writes the media straight to a file; the response also carries `Content-Disposition` and `x-keyframe-segment-start-ms`, `-end-ms`, and `-duration-ms` headers (an `EndMs` past the end is clamped).

   ```bash
   # The scene as an mp4 clip, scaled to 720 pixels on the long edge
   curl -s -X POST http://127.0.0.1:8700/v1.0/segment \
     -H "x-api-key: default" -H "Content-Type: application/json" \
     -d '{ "Source": { "Url": "https://example.com/talk.mp4" }, "StartMs": 12000, "EndMs": 18500, "Output": "Video", "MaxEdge": 720 }' \
     -o scene-3.mp4

   # What was said in that transcript segment, as mp3
   curl -s -X POST http://127.0.0.1:8700/v1.0/segment \
     -H "x-api-key: default" -H "Content-Type: application/json" \
     -d '{ "Source": { "Url": "https://example.com/talk.mp4" }, "StartMs": 41200, "EndMs": 47900, "Output": "Audio", "Format": "mp3" }' \
     -o segment-5.mp3

   # A still from the start of the scene, returned as JSON with base64 data
   curl -s -X POST http://127.0.0.1:8700/v1.0/segment \
     -H "x-api-key: default" -H "Content-Type: application/json" \
     -d '{ "Source": { "Url": "https://example.com/talk.mp4" }, "StartMs": 12000, "Output": "Frame", "ResponseFormat": "Json" }' \
     | jq '{ MediaType, Filename, SizeBytes }'
   ```

   With base64 input, put the same `Data` in `Source` (`{ "Source": { "Data": "AAAAIGZ0eXBpc29t..." }, ... }`). Audio files support `Output: "Audio"` only; `Video` and `Frame` need a video source. Segments are limited to `Media.MaxSegmentMs` (10 minutes by default).

## Model providers

Endpoints live in `Inference.Endpoints` in `keyframe.json` (or the dashboard's Settings page). `DefaultVision` names the completion endpoint used for images and frames, `DefaultText` names the text-only endpoint for audio summaries and fields (and the optional text-only whole-video step), and `DefaultTranscription` names the speech-to-text endpoint. Completion calls stream, so every call reports time to first token, generation time, and tokens per second.

### Default settings

New settings files and the shipped `keyframe.json` files (`src/Keyframe.Server/keyframe.json`, `docker/keyframe.json`, `docker/factory/keyframe.json`) contain these endpoints. There is no mock endpoint by default; tests and offline demos add one with `"Provider": "Mock"`.

| Name | Provider | Kind | Endpoint (local / Docker) | Model |
|---|---|---|---|---|
| `ollama-gemma3-4b` | `Ollama` | Completion, vision (`DefaultVision`) | `http://127.0.0.1:11434` / `http://host.docker.internal:11434` | `gemma3:4b` |
| `ollama-qwen3-vl-8b` | `Ollama` | Completion, vision | same | `qwen3-vl:8b` |
| `ollama-gpt-oss-20b` | `Ollama` | Completion, text (`DefaultText`) | same | `gpt-oss:20b` |
| `whisper` | `OpenAiCompatible` | Transcription (`DefaultTranscription`) | `http://127.0.0.1:8000/v1` / `http://keyframe-whisper:8000/v1` | `Systran/faster-whisper-small` |

The Ollama endpoints use `MaxOutputTokens` 4096 and `TimeoutMs` 300000, because reasoning models such as gpt-oss spend output tokens thinking. Analytics are on (`Analytics.Enabled`, 90 days of `Analytics.RetentionDays`), and segments are limited to 10 minutes (`Media.MaxSegmentMs`).

### Providers

| Provider | `Endpoint` | `ApiKey` | Images | Native JSON output | Transcription |
|---|---|---|---|---|---|
| `Ollama` | Optional (default `http://127.0.0.1:11434`) | Optional | Yes | No | No |
| `OllamaCloud` | Optional | Required | Yes | No | No |
| `OpenAi` | Optional | Required | Yes | Yes | Yes (`whisper-1` returns timed segments) |
| `AzureOpenAi` | Required | Required | Yes | Yes | Yes |
| `OpenAiCompatible` | Required (for example `http://127.0.0.1:8000/v1`) | Optional | Yes | No | Yes, if the server has `/v1/audio/transcriptions` |
| `Anthropic` | Optional | Required | Yes | No | No |
| `Gemini` | Optional | Required | Yes | No | No |
| `XAi` | Optional | Required | Yes | Yes | No |
| `Cohere` | Optional | Required | Yes | No | No |
| `VertexAi`, `Bedrock` | Not yet supported: they need credential settings Keyframe does not have yet | | | | |

"Images: Yes" means the provider accepts images; the model you name must be a vision model. Where native JSON output is not available, extraction describes the schema in the prompt and parses the reply leniently, with one repair attempt. `GET /v1.0/endpoints` reports `ProviderAvailable`, `SupportsImages`, `SupportsStructuredOutput`, and `SupportsTranscription` for each endpoint.

Example: switch the default vision model to the larger Qwen model and add a hosted text model.

```json
"Inference": {
  "DefaultVision": "ollama-qwen3-vl-8b",
  "DefaultText": "openai-text",
  "Endpoints": [
    { "Name": "ollama-qwen3-vl-8b", "Provider": "Ollama", "Kind": "Completion", "Endpoint": "http://127.0.0.1:11434", "Model": "qwen3-vl:8b", "MaxOutputTokens": 4096, "TimeoutMs": 300000, "Active": true },
    { "Name": "openai-text", "Provider": "OpenAi", "Kind": "Completion", "ApiKey": "sk-...", "Model": "gpt-4.1-mini", "Active": true }
  ]
}
```

Reasoning models spend output tokens on thinking, so give them a generous `MaxOutputTokens`.

## Docker

`docker/compose.yaml` runs the server, dashboard, and MCP server from Docker Hub (`jchristn77/keyframe-server`, `jchristn77/keyframe-dashboard`, `jchristn77/keyframe-mcp`, tagged `v0.1.0`) with the observability stack and a `keyframe-whisper` speech-to-text service (speaches `0.8.3-cpu`, port 8000 on host loopback, models cached in the `whisper-models` volume). A one-shot `keyframe-whisper-init` service downloads `Systran/faster-whisper-small` on first start, because speaches 0.8.3 does not fetch models on demand. Vision and text models come from Ollama on the host, reached through `host.docker.internal`:

```bash
ollama pull gemma3:4b && ollama pull qwen3-vl:8b && ollama pull gpt-oss:20b
cd docker
docker compose up -d
```

To build and publish multi-architecture images (linux/amd64 and linux/arm64/v8), run `build-all.sh` or `build-all.bat` from the repository root with a tag, for example `./build-all.sh v0.1.0`. Each image is tagged with that tag and `latest`, pushed, and then pulled locally. `build-server`, `build-mcp`, and `build-dashboard` build one image each.

## AOT support

Keyframe is not AOT capable. The server relies on reflection-based JSON serialization and hosts ffmpeg as a child process, so there is no `Test.Aot` project.

## License

MIT. See [LICENSE.md](LICENSE.md).
