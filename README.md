<p align="center">
  <img src="assets/logo-dark.png" alt="Keyframe" height="192" />
</p>

<p align="center">
  <b>v0.1.0 - ALPHA</b> - API and structure may change without notice
</p>

# Keyframe

Keyframe generates summaries and extracts metadata from images and video. You post a picture or a clip, tell it what you want to know, and it hands back a summary plus a dictionary of typed answers keyed the way you asked. For video it also breaks the footage into scenes, summarizes and answers questions per scene, and can line the results up against a transcript of the audio. Every response carries its own telemetry: how long each stage took, every model call made, and the tokens each one used.

> **Status.** Keyframe is in active development. The media pipeline, analysis engine, persistence on four databases, and the REST server are built and tested. Model calls go through [PolyPrompt](https://github.com/jchristn/PolyPrompt) 5.0 (Ollama, OpenAI, Azure OpenAI, OpenAI-compatible servers, Anthropic, Gemini, xAI, Cohere), with a deterministic mock provider for tests. See [KEYFRAME_PLAN.md](KEYFRAME_PLAN.md) for the roadmap and current phase.

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

Every `Context` item is included in every prompt the request produces, so background you supply once applies to the summary and to each field. The response includes `Results` (`{ "Summary": "...", "hasCats": false, "dogCount": 2 }`), a `Fields` array with per-field detail, the media `Properties`, and `Telemetry`. Video requests (`POST /v1.0/video`) add `Scenes` and `Transcribe` functions and run as background jobs. The full contract is in [REST_API.md](REST_API.md).

## Architecture

```text
            request (URL or base64)
                     |
               MediaResolver  ---- SSRF guard, size limits
                     |
               MediaProber    ---- ffprobe: dimensions, duration, codecs, tags
                     |
     +---------------+-----------------+
     |                                 |
  image                              video
     |                                 |
ImageNormalizer             SceneDetector, FrameExtractor, AudioExtractor
     |                                 |
     +---------- IInferenceClient -----+---- ITranscriptionClient
                 (PolyPrompt or mock)
                     |
        Results + Fields + Scenes + Telemetry
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

Run the server. It creates `keyframe.json` with defaults the first time, listens on `http://127.0.0.1:8700`, stores data in SQLite under `data/`, and answers every analysis with the deterministic mock provider until real providers are configured:

```bash
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

## Model providers

Endpoints live in `Inference.Endpoints` in `keyframe.json` (or the dashboard's Settings page). `DefaultVision` names the completion endpoint used for images and frames, `DefaultText` optionally names a text-only endpoint for the whole-video reduce step, and `DefaultTranscription` names the speech-to-text endpoint.

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

Example: a local Ollama vision model as the default.

```json
"Inference": {
  "DefaultVision": "local-ollama",
  "Endpoints": [
    { "Name": "local-ollama", "Provider": "Ollama", "Kind": "Completion", "Endpoint": "http://127.0.0.1:11434", "Model": "qwen3-vl:8b", "MaxOutputTokens": 4096, "Active": true }
  ]
}
```

Reasoning models spend output tokens on thinking, so give them a generous `MaxOutputTokens`.

## Docker

`docker/compose.yaml` runs the server, dashboard, and MCP server from Docker Hub (`jchristn77/keyframe-server`, `jchristn77/keyframe-dashboard`, `jchristn77/keyframe-mcp`, tagged `v0.1.0`) with the observability stack:

```bash
cd docker
docker compose up -d
```

To build and publish multi-architecture images (linux/amd64 and linux/arm64/v8), run `build-all.sh` or `build-all.bat` from the repository root with a tag, for example `./build-all.sh v0.1.0`. Each image is tagged with that tag and `latest`, pushed, and then pulled locally. `build-server`, `build-mcp`, and `build-dashboard` build one image each.

## AOT support

Keyframe is not AOT capable. The server relies on reflection-based JSON serialization and hosts ffmpeg as a child process, so there is no `Test.Aot` project.

## License

MIT. See [LICENSE.md](LICENSE.md).
