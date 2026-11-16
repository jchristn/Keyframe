# Changelog

## v0.1.0 (unreleased, alpha)

First development release. Not yet published.

### Added

- Request and response contracts for image and video analysis: `Summarize` and `Extract` functions with typed `Fields`, plus video `Scenes` and `Transcribe` functions.
- Media pipeline on ffmpeg and ffprobe: magic-byte type detection, SSRF-guarded URL download, probing, image normalization, scene detection with minimum, maximum, and count rules, keyframe extraction, and chunked audio extraction.
- Inference seam (`IInferenceClient`, `ITranscriptionClient`) with deterministic mock providers, per-endpoint concurrency limits, and per-call timeouts.
- `Keyframe` meter and activity source with metrics for analyses, stages, model calls, tokens, ffmpeg, jobs, and authentication.
- Analysis engine: prompt builder with a fenced, data-only context block shared by every prompt, `Combined` and `PerField` extraction, lenient JSON parsing with a repair loop, retries with backoff, type coercion with per-field warnings, and per-request telemetry (stage timings, every model call and attempt, token totals).
- Persistence on SQLite, MySQL, PostgreSQL, and SQL Server with versioned, idempotent migrations: request history (with a header child table), jobs, and job results. The database suites pass against all four providers.
- REST server on Watson 7 with OpenAPI and Swagger UI: image analysis, video jobs (queued, cancelable, durable, marked `Interrupted` after a restart, optional synchronous `Wait`), probe, jobs, endpoints and endpoint tests, settings read and update with masked secrets and live-versus-restart metadata, restart, and request history with gap-filled summaries.
- DocumentAtom-style authentication: named access keys plus an admin key, accepted as a bearer token or `x-api-key`, compared in constant time, with per-address rate limiting of failed attempts.
- Request capture for every call with secret headers redacted and bodies truncated, written in the background.
- Radiant telemetry host exporting the Keyframe and Watson meters and sources over OTLP and Prometheus.
- `REST_API.md` and a Postman collection under `assets/postman/`.
- MCP server on Voltaic 2.4 (Streamable HTTP, JSON-RPC, TCP, WebSocket) with nine tools that call the REST server through the C# SDK, bearer authentication, and `--install`/`--uninstall` for Claude Code, Codex, Gemini CLI, Cursor, and Mux. `MCP_API.md` documents every tool.
- SDKs for C# (`Keyframe.Sdk`, net8.0 and net10.0), JavaScript (`keyframe-sdk`), and Python (`keyframe_sdk`), each with a harness that runs against a live server.
- Docker images for the server and MCP server, a Compose stack with an OpenTelemetry collector, Prometheus, Tempo, Loki, and Grafana with six dashboards, update scripts, and a factory reset with sample media. `TELEMETRY.md` and `DOCKERHUB_README.md`.
- xUnit and NUnit runners for the shared suites and a GitHub Actions workflow covering all four database providers, three operating systems, the dashboard, and the SDK harnesses.
- React dashboard (React 19, Vite 6): analyze image and video, jobs with scene timeline and transcript, endpoints, request history, API Explorer, settings with restart, server info; dark and light themes, i18n.
- Docker Hub images `jchristn77/keyframe-server`, `jchristn77/keyframe-dashboard`, and `jchristn77/keyframe-mcp` (tag `v0.1.0`) used by `docker/compose.yaml`, and `build-*.sh`/`build-*.bat` scripts that build for linux/amd64 and linux/arm64/v8, tag the given version and `latest`, push, and pull.
- Real model providers through PolyPrompt 5.0: Ollama, Ollama Cloud, OpenAI, Azure OpenAI, OpenAI-compatible servers, Anthropic, Gemini, xAI, and Cohere for vision completions, and OpenAI, Azure OpenAI, and OpenAI-compatible servers for transcription with timed segments. JSON object output is requested natively on OpenAI, Azure OpenAI, and xAI. `GET /v1.0/endpoints` reports `SupportsImages`, `SupportsStructuredOutput`, and `SupportsTranscription`. A misconfigured endpoint (missing key or URL) fails with 502.
- Provider suite against a local OpenAI-compatible stub, and opt-in live provider tests (`KEYFRAME_TEST_OLLAMA_URL` and `KEYFRAME_TEST_OLLAMA_MODEL`, `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, `GEMINI_API_KEY`).
