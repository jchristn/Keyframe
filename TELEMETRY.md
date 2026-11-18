# Keyframe Telemetry

Keyframe reports what it does on three layers:

1. **Response telemetry.** Every analysis response carries a `Telemetry` block: stage timings, every model call with its timing and tokens, and totals. The caller gets it with the result.
2. **Analytics store.** Every finished analysis also writes one row to the `analyses` table and one row per model call attempt to `modelcalls`. The admin-only `/v1.0/analytics` routes turn those rows into percentiles, time buckets, and per-model breakdowns.
3. **Operational telemetry.** Metrics, traces, and logs for the observability stack (Prometheus, Tempo, Loki, Grafana), with the Grafana dashboards built on them.

This document describes all three: every statistic, where it is stored, and how it is computed. Request and response shapes are in [REST_API.md](REST_API.md).

- [Response telemetry](#response-telemetry)
- [Analytics store](#analytics-store)
- [Analytics API and computation](#analytics-api-and-computation)
- [How it fits together](#how-it-fits-together)
- [Sources](#sources)
- [Configuration](#configuration)
- [Metrics](#metrics)
- [Built-in metrics from Watson and .NET](#built-in-metrics-from-watson-and-net)
- [Spans](#spans)
- [Logs](#logs)
- [Dashboards](#dashboards)
- [Viewing telemetry](#viewing-telemetry)
- [Production notes](#production-notes)

## Response telemetry

`AnalysisResponse.Telemetry` (images, videos, and audio):

| Field | How it is measured |
| --- | --- |
| `StartUtc`, `EndUtc` | Wall clock when the analyzer starts work and when it completes the response. |
| `TotalMs` | Stopwatch from analyzer start to response completion. Excludes queue time. |
| `QueueWaitMs` | Job queued to job started (`Job.StartedUtc - Job.CreatedUtc`). Always `0` for synchronous image calls. |
| `QueuedMs` | The same value as `QueueWaitMs` for video and audio jobs, `null` for images. Kept for older clients. |
| `Stages[]` | One entry per pipeline stage: `Name`, `StartUtc`, `DurationMs` (stopwatch around the stage), `Success` (false when the stage recorded an error). Stages that run in parallel (Summarize and Extract) overlap. Audio stages, in order: `Resolve`, `Probe`, `Measure`, `AudioExtraction`, `Transcribe`, `SectionDetection`, `ListenExtraction`, `Sections`, `Summarize`, `Extract`, each present only when it ran. `ListenExtraction` renders the whole-recording listening clips; per-section clips are rendered inside `Sections`, and scene audio clips inside the video `Scenes` stage. |
| `ModelCalls[]` | One entry per logical model call. A call that was retried or repaired is still one entry; see below. |
| `Totals` | Sums over `ModelCalls`: `ModelCalls`, `FailedModelCalls`, `ModelMs` (sum of call durations, so parallel calls can exceed `TotalMs`), token counts, `InputImages`, `InputImageBytes`. |

Each `ModelCalls[]` entry:

| Field | How it is measured |
| --- | --- |
| `Purpose`, `Target` | Why the call was made (`Summarize`, `Extract`, `SceneSummarize`, `SceneExtract`, `SectionSummarize`, `SectionExtract`, `Transcribe`) and the field key, scene (`scene:<index>`), audio section (`section:<index>`), or transcription chunk (`chunk:<index>`) it served. |
| `Endpoint`, `Provider`, `Model` | The configured endpoint, its provider, and the model the provider reported (or the requested one). |
| `StartUtc` | Start of the first attempt. |
| `DurationMs` | Stopwatch around every attempt of the call, including retry backoff and the wait on the endpoint's concurrency gate. |
| `QueueMs` | Time spent waiting for a slot on the endpoint (`EndpointSettings.MaxConcurrentRequests`), summed over attempts. Measured by the gated client wrapper. |
| `TimeToFirstTokenMs` | Completion calls stream. Request sent to the first content or reasoning token of the last attempt that produced one. Reasoning models (gpt-oss) count their first reasoning token. `null` for transcription. |
| `GenerationMs` | First token to last token of that attempt. |
| `TokensPerSecond` | `CompletionTokens / (GenerationMs / 1000)` for that attempt; `null` when either is unknown or zero. |
| `Attempts`, `RepairAttempts` | Attempts made (1 means no retry), and how many were repairs of unusable output. Retries are `Attempts - 1 - RepairAttempts`. |
| `Success`, `Error`, `StatusCode`, `FinishReason` | Outcome of the call and details of the last attempt. |
| `InputImages`, `InputImageBytes`, `InputAudioBytes` | What was sent. `InputAudioBytes` is the audio file for a transcription call, or the audio clips attached to a completion call (a listening call, or a scene call that heard its audio), counted before base64. |
| `PromptTokens`, `CompletionTokens`, `TotalTokens`, `CachedTokens`, `ReasoningTokens` | As reported by the provider, summed over attempts. `null` when the provider reports nothing. |

The mock provider reports simulated, deterministic timing so the analytics pipeline can be tested offline: time to first token is 2 ms plus 0.02 ms per prompt token, and generation runs at 500 tokens per second (with `DelayMs` set, a quarter of the delay is first-token time and the rest generation).

## Analytics store

`AnalysisServices` raises a completion notification at the end of every analysis that started: success, failure (including errors thrown by media handling), and cancellation after start. Requests rejected by validation or endpoint resolution, and jobs canceled while queued, never start an analysis and are not recorded. The server's `AnalyticsService` builds the rows from the completed response and queues them; one background writer inserts each analysis and its calls in a single transaction. The response path never waits for the database. A failed write is logged (`[Analytics] could not write analysis ...`) and counted in `keyframe.analytics.writes`; it never fails the analysis.

| Setting | Default | Range | Applies |
| --- | --- | --- | --- |
| `Analytics.Enable` | `true` | | Live. When false, nothing new is written; stored rows stay readable. |
| `Analytics.RetentionDays` | `90` | 1 to 3650 | Live. The hourly maintenance pass deletes analyses (and their calls) that completed before the cutoff. |

Schema migration 2 creates both tables on SQLite, MySQL, PostgreSQL, and SQL Server.

`analyses`, one row per analysis (indexes on `completedutc`, `(mediakind, completedutc)`, and `jobid`):

| Column | Meaning |
| --- | --- |
| `id` | Analysis id (`kfa_`), the response `Id`. |
| `requestid`, `jobid`, `principalname` | HTTP request id, job id (null for images), and access key name. |
| `mediakind`, `success`, `error` | `Image`, `Video`, or `Audio`; success; error summary (the exception message, `Canceled.`, or the response errors; up to 2000 characters). |
| `startedutc`, `completedutc` | `Telemetry.StartUtc` and `EndUtc`. Analytics filters and buckets use `completedutc`. |
| `analysisms`, `queuewaitms` | `Telemetry.TotalMs` and `QueueWaitMs`. |
| `mediadurationms`, `mediabytes` | Probed duration (video and audio) and resolved size. |
| `realtimefactor` | `analysisms / mediadurationms` for video and audio; below 1 means faster than real time. |
| `visionendpoint`, `visionmodel`, `textendpoint`, `textmodel`, `transcriptionendpoint`, `transcriptionmodel` | The endpoints and models the analysis resolved, whether or not each was called. For an audio analysis that listened, `visionendpoint` and `visionmodel` hold the listening endpoint and model; an audio analysis that fell back leaves them null and records the text endpoint instead. |
| `modelcalls`, `failedmodelcalls` | Logical calls, from `Totals`. |
| `retries`, `repairattempts` | Summed over calls. |
| `prompttokens`, `completiontokens`, `totaltokens`, `cachedtokens`, `reasoningtokens` | From `Totals`. |
| `inputimages`, `inputimagebytes`, `inputaudiobytes` | What was sent to models. `inputaudiobytes` sums every call: transcription audio plus the audio clips attached to completion calls. Before listening existed it counted transcription audio only, so a listening analysis reports more audio bytes than the same request with `Listen.Enable` false. |
| `scenes`, `transcriptcharacters` | Scene count (video) and transcript length. |
| `fieldcount`, `invalidfields` | Extracted fields, including per-scene fields, and how many failed validation. |
| `stagesjson` | `Telemetry.Stages` as JSON. |

`modelcalls`, one row per attempt, so a call with one retry and one repair is three rows (indexes on `analysisid`, `startutc`, and `(endpoint, model)`):

| Column | Meaning |
| --- | --- |
| `id`, `analysisid` | Row id (`kfc_`) and owning analysis. |
| `purpose`, `target`, `endpoint`, `provider`, `model` | As in `ModelCalls[]`. |
| `attempt` | 1-based attempt number within the logical call. |
| `startutc`, `success`, `error`, `statuscode`, `finishreason` | Per attempt. A provider response whose output could not be used counts as a failed attempt with `Unusable output: ...` as its error. |
| `durationms`, `queuems`, `ttftms`, `generationms`, `tokenspersecond` | Per attempt, as defined above. |
| `prompttokens`, `completiontokens`, `totaltokens`, `cachedtokens`, `reasoningtokens` | Per attempt, null when not reported. |
| `inputimages`, `inputimagebytes`, `inputaudiobytes` | What the call sent. |

## Analytics API and computation

The routes are documented in [REST_API.md](REST_API.md#analytics); all need the admin key.

`GET /v1.0/analytics/summary` loads the analyses that completed in `[fromUtc, toUtc)` and match the filters, plus their model call rows, and aggregates them in memory (the same way request history summaries work), so every database returns identical numbers:

- **Range and buckets.** `toUtc` defaults to now and `fromUtc` to 24 hours earlier. `bucket` defaults to `minute` for ranges up to 2 hours, `hour` up to 7 days, otherwise `day`. `fromUtc` is aligned down to the bucket width (UTC), and every bucket in the range is returned, including empty ones (zero counts, null distributions). A range that needs more than 10000 buckets is a `400`.
- **Filters.** `mediaKind` matches the analysis. `endpoint` and `model` match an analysis when any of its vision, text, or transcription endpoints (or models) match, and restrict the model call statistics to calls to that endpoint or model.
- **Distributions.** `Count`, `Min`, `Max`, `Avg`, `Sum`, population `StdDev`, and `P50`, `P90`, `P95`, `P99` computed by linear interpolation between closest ranks (the spreadsheet `PERCENTILE.INC` method). Values are rounded to 3 decimals. A distribution with no samples is `null`.
- **What each distribution covers.** `AnalysisMs`, `QueueWaitMs`, `MediaDurationMs`, `RealtimeFactor` (video and audio only), `MediaBytes`, and `TokensPerAnalysis` are over analyses. `ModelCallMs`, `TimeToFirstTokenMs`, `GenerationMs`, and `TokensPerSecond` are over model call attempts, skipping attempts that did not report the value. Counts (`ModelCalls`, `Retries`, tokens, `Scenes`, ...) are sums over analyses.
- **Breakdowns.** `Totals`, `ByMediaKind` (always `Image`, `Video`, `Audio`), and each bucket are full `StatBlock`s. `ByModel` groups attempts by endpoint, provider, and model (most calls first). `ByStage` groups stage timings by name in pipeline order. `ByPurpose` groups attempts by purpose.

`GET /v1.0/analytics/analyses` and `GET /v1.0/analytics/analyses/{id}` return the stored rows; the single read includes every attempt in `Calls`. The delete routes remove analyses with their calls.

## How it fits together

```text
                        keyframe-server
        Keyframe meter + ActivitySource, Watson meter + ActivitySource,
        System.Net.Http meter, .NET runtime metrics
                               |
                    TelemetryHost (Radiant 0.1.3)
               |                                    |
   OTLP gRPC (traces, logs)              Prometheus scrape :9464/metrics
               |                                    |
     OpenTelemetry Collector                  Prometheus (host 9700)
        |               |                           |
   Tempo (host 3710)   Loki                         |
        \_______________|___________________________/
                               |
                     Grafana (host 3700), folder "Keyframe"
```

- `Keyframe.Core` records through the .NET base class library only (`System.Diagnostics.Metrics` and `ActivitySource`). With nothing listening, the calls cost almost nothing, and a telemetry failure never affects a request.
- Watson 7 measures the HTTP layer itself: request metrics and one server span per request. Keyframe spans nest under that span.
- `Keyframe.Server` starts one Radiant host at startup (`Observability/TelemetryHost.cs`). It subscribes to every source above, serves an in-process Prometheus endpoint, and exports traces and logs over OTLP when `Telemetry.OtlpEnable` is on. If it cannot start, the server logs why and keeps running without export.
- The collector forwards traces to Tempo and logs to Loki. OTLP metrics are discarded at the collector because Prometheus scrapes the server directly; exporting both would count every series twice.

## Sources

| Name | Kind | Contents |
| --- | --- | --- |
| `Keyframe` | Meter and ActivitySource | Every `keyframe_*` metric and every Keyframe span below. |
| `Watson` | Meter and ActivitySource | `http.server.*` and `watson.*` metrics, one server span per REST request. |
| `System.Net.Http` | Meter | `http.client.*` metrics for outbound provider and endpoint health calls. |
| `System.Runtime` | Meter (Radiant runtime instrumentation) | `dotnet.*` GC, JIT, thread pool, CPU, and memory metrics. |

## Configuration

Telemetry lives in the `Telemetry` section of `keyframe.json`. Changes need a restart.

| Setting | Default | Docker stack (`docker/keyframe.json`) | Notes |
| --- | --- | --- | --- |
| `Enable` | `true` | `true` | Master switch for the Radiant host. |
| `ServiceName` | `keyframe-server` | `keyframe-server` | Reported as `service.name`. |
| `OtlpEnable` | `false` | `true` | Off by default so a standalone server does not retry against a missing collector. |
| `OtlpEndpoint` | `http://127.0.0.1:4717` | `http://otel-collector:4317` | The compose stack publishes the collector on host ports 4717 (gRPC) and 4718 (HTTP). |
| `OtlpProtocol` | `grpc` | `grpc` | `grpc` or `httpprotobuf`. |
| `PrometheusEnable` | `true` | `true` | Serve the scrape endpoint on its own port. |
| `PrometheusHostname` | `127.0.0.1` | `keyframe-server` | The endpoint answers only requests whose Host header matches. Wildcards (`*`, `0.0.0.0`) are rejected by the exporter, so containers use the hostname the scraper resolves. |
| `PrometheusPort` | `9464` | `9464` | Internal to the compose network; not published on the host. |
| `PrometheusPath` | `/metrics` | `/metrics` | |
| `ExportLogs` | `false` | `true` | Forward server log lines into the OTLP log pipeline, stamped with trace and span ids. |
| `LokiEnable` | `false` | `false` | Push logs straight to Loki for deployments without a collector. Leave off when the collector forwards logs. |
| `LokiEndpoint` | `http://127.0.0.1:3100` | `http://loki:3100/otlp` | |
| `MetricExportIntervalMs` | `15000` | `15000` | OTLP metric push interval. |
| `TraceSamplingRatio` | `1.0` | `1.0` | Parent-based sampling ratio for new root traces. |

Startup logs one line with the outcome, for example `telemetry export enabled, Prometheus at http://keyframe-server:9464/metrics`, or the reason export is disabled.

## Metrics

The Prometheus exporter rewrites instrument names: dots become underscores, seconds histograms gain `_seconds`, and counters gain `_total`. Every duration histogram uses the same seconds buckets: 5 ms, 10 ms, 25 ms, 50 ms, 100 ms, 250 ms, 500 ms, 1 s, 2.5 s, 5 s, 10 s, 30 s, 60 s, 120 s, 300 s, 600 s, 1800 s. Quantiles are derived in Grafana with `histogram_quantile`.

| Instrument | Prometheus series | Type | Labels | Description |
| --- | --- | --- | --- | --- |
| `keyframe.analysis.total` | `keyframe_analysis_total` | Counter | `media_kind`, `outcome` | Analyses completed. |
| `keyframe.analysis.duration` | `keyframe_analysis_duration_seconds_bucket/_sum/_count` | Histogram | `media_kind` | End-to-end analysis duration. |
| `keyframe.stage.duration` | `keyframe_stage_duration_seconds_bucket/_sum/_count` | Histogram | `media_kind`, `stage` | Duration of each pipeline stage. |
| `keyframe.model.calls` | `keyframe_model_calls_total` | Counter | `purpose`, `provider`, `outcome` | Model calls, including transcription. |
| `keyframe.model.call.duration` | `keyframe_model_call_duration_seconds_bucket/_sum/_count` | Histogram | `purpose`, `provider` | Model call duration, including retries and repair attempts. |
| `keyframe.model.tokens` | `keyframe_model_tokens_total` | Counter | `provider`, `kind` | Tokens reported by the provider. |
| `keyframe.ffmpeg.invocations` | `keyframe_ffmpeg_invocations_total` | Counter | `operation`, `outcome` | ffmpeg and ffprobe invocations. |
| `keyframe.ffmpeg.duration` | `keyframe_ffmpeg_duration_seconds_bucket/_sum/_count` | Histogram | `operation` | ffmpeg and ffprobe process duration. |
| `keyframe.jobs.total` | `keyframe_jobs_total` | Counter | `outcome` | Video and audio jobs finished. |
| `keyframe.jobs.active` | `keyframe_jobs_active` | Gauge | none | Jobs running now. |
| `keyframe.jobs.queued` | `keyframe_jobs_queued` | Gauge | none | Jobs waiting for a slot. |
| `keyframe.auth.requests` | `keyframe_auth_requests_total` | Counter | `result` | Authentication outcomes. |
| `keyframe.endpoint.healthy` | `keyframe_endpoint_healthy` | Gauge | `endpoint` | 1 when a configured model endpoint is healthy, 0 when not. Updated by each health probe (see [Endpoint health probes](#endpoint-health-probes)). |
| `keyframe.model.queue.duration` | `keyframe_model_queue_duration_seconds_bucket/_sum/_count` | Histogram | `purpose`, `provider` | Time each model call attempt waited for a slot on its endpoint. |
| `keyframe.model.ttft` | `keyframe_model_ttft_seconds_bucket/_sum/_count` | Histogram | `purpose`, `provider` | Time to first token of each completion attempt (streamed). |
| `keyframe.model.generation.duration` | `keyframe_model_generation_duration_seconds_bucket/_sum/_count` | Histogram | `purpose`, `provider` | First token to last token of each completion attempt. |
| `keyframe.model.tokens_per_second` | `keyframe_model_tokens_per_second_bucket/_sum/_count` | Histogram | `purpose`, `provider` | Completion tokens per second of generation time. Unit `{token}/s`; uses the default histogram buckets, not the seconds buckets. |
| `keyframe.jobs.queue.duration` | `keyframe_jobs_queue_duration_seconds_bucket/_sum/_count` | Histogram | `media_kind` | Time a video or audio job waited in the queue before a worker started it. |
| `keyframe.analysis.realtime_factor` | `keyframe_analysis_realtime_factor_bucket/_sum/_count` | Histogram | `media_kind` | Analysis time divided by media duration for completed video and audio analyses. |
| `keyframe.analytics.writes` | `keyframe_analytics_writes_total` | Counter | `outcome` | Analytics rows written to the database. |
| `keyframe.model.input.audio.bytes` | `keyframe_model_input_audio_bytes_bucket/_sum/_count` | Histogram | `provider`, `purpose` | Bytes of audio attached to one completion call (listening and scene audio), before base64. Unit `By`; default histogram buckets. Calls without audio, and transcription calls, are not recorded, so `_count` is the number of completion calls that carried audio. |
| `keyframe.analysis.listen.total` | `keyframe_analysis_listen_total` | Counter | `media_kind`, `mode`, `reason` | One per audio analysis and per video analysis with scenes enabled, including fallbacks (`mode="none"`). Shows how many requests that asked to listen were answered from text. |

Audio analyses are counted by the existing `keyframe.analysis.total` and `keyframe.analysis.duration` instruments with `media_kind="audio"`. Listening adds the last two instruments. The listening fallback rate is the signal to watch: `sum by (reason) (rate(keyframe_analysis_listen_total{mode="none",reason!~"none|NotRequested|NoModelFunctions"}[5m]))` counts requests that wanted to listen and were answered from the transcript. The seven instruments above were added with audio analysis and streaming model calls; the provisioned Grafana dashboards chart them (Overview, Models, Jobs, Analysis, and Media), broken down by provider and purpose; they carry no endpoint or model label, so per-model views come from the analytics API. They are also queryable directly, for example `histogram_quantile(0.95, sum by (le, provider) (rate(keyframe_model_ttft_seconds_bucket[5m])))`.

### Label values

Every label has a small, fixed set of values. No ids, paths, prompts, or user input ever become labels.

| Label | Values |
| --- | --- |
| `media_kind` | `image`, `video`, `audio` |
| `outcome` (analysis) | `success`, `partial` (completed, but a field failed validation), `failure` |
| `stage` | `resolve`, `probe`, `normalize`, `scenedetection`, `frameextraction`, `measure`, `audioextraction`, `sectiondetection`, `listenextraction`, `transcribe`, `scenes`, `sections`, `summarize`, `extract` |
| `purpose` | `summarize`, `extract`, `scenesummarize`, `sceneextract`, `sectionsummarize`, `sectionextract`, `transcribe` |
| `provider` | Lowercased provider type: `mock`, `ollama`, `ollamacloud`, `openai`, `openaicompatible`, `azureopenai`, `anthropic`, `gemini`, `vertexai`, `bedrock`, `xai`, `cohere` |
| `outcome` (model call) | `success`, `failure` |
| `kind` | `prompt`, `completion` |
| `operation` | `version`, `probe`, `normalize`, `scene_detect`, `frame_extract`, `audio_extract`, `audio_measure` (measurements: one pass, plus a mid/side pass for stereo), `section_detect` (silence detection for sections), `listen_extract` (one per listening clip rendered), `segment_video`, `segment_audio`, `segment_frame` |
| `outcome` (ffmpeg) | `success`, `failure`, `timeout`, `canceled`, `unavailable` |
| `outcome` (jobs) | `succeeded`, `failed`, `canceled`, `interrupted` |
| `result` | `success`, `failure`, `rate_limited` |
| `outcome` (analytics writes) | `success`, `failure`, `dropped` (the in-memory queue of 10000 rows was full) |
| `endpoint` | Endpoint names from `Inference.Endpoints` in settings (operator defined, few) |
| `mode` (listening) | Lowercased `ListeningModeEnum`: `full`, `excerpts`, `sections`, `scenes`, `none` |
| `reason` (listening) | `none` when the audio was heard, otherwise the `ListenSkipReasonEnum` name as is: `NotRequested`, `NoModelFunctions`, `NoEndpointConfigured`, `EndpointLacksAudioInput`, `EndpointUnhealthy`, `NoAudioTrack` |

Whole-video summary and extraction, and audio summary and extraction (including a reduce from section results), record their time under the `summarize` and `extract` stages.

### Endpoint health probes

The health service probes every active endpoint within a second of startup. After that, an endpoint whose last probe succeeded is probed again after `Inference.HealthProbeIntervalMs` (default 300000, five minutes; 10000 to 3600000), and an endpoint that is unhealthy is probed again after `Inference.UnhealthyProbeIntervalMs` (default 15000; 1000 to 600000). Both apply live. Endpoints are probed concurrently, so one slow endpoint does not delay the rest. A scheduled probe never waits behind real work: when an endpoint's concurrency gate (`MaxConcurrentRequests`) is full, the endpoint is busy rather than down, the probe is skipped, and its last state is kept. A model call that cannot reach its endpoint at all (no HTTP response, for example connection refused) also marks the endpoint unhealthy at once; timeouts and HTTP errors do not. So a llama-server that stops is reported unhealthy by the first call that tries it, and from then on listening requests fall back with `EndpointUnhealthy`. Once it is back, the 15-second unhealthy interval means it is used again within about 15 seconds.

## Built-in metrics from Watson and .NET

These come with no Keyframe code and feed the Overview and HTTP dashboards.

| Series | Labels | Description |
| --- | --- | --- |
| `http_server_request_duration_seconds_bucket/_sum/_count` | `http_request_method`, `http_response_status_code`, `http_route` | REST request duration. `http_route` is always the route template, and unmatched requests carry no route. |
| `http_server_active_requests` | none | In-flight requests. |
| `http_server_request_body_size_bytes`, `http_server_response_body_size_bytes` | method, status, route | Payload size histograms. |
| `watson_server_up`, `watson_server_uptime_seconds` | none | Liveness and uptime. |
| `watson_server_connections_active`, `watson_server_connections_total` | `network_protocol_version` | Connections. |
| `watson_server_received_bytes_total`, `watson_server_sent_bytes_total` | none | Bytes on the wire. |
| `watson_route_matches_total` | `http_route`, `watson_route_type` | Route table matches. |
| `watson_auth_requests_total` | `watson_auth_mode` | Watson authentication hook decisions. |
| `http_client_request_duration_seconds_bucket/_sum/_count` | `server_address`, `http_response_status_code`, and others | Outbound HTTP to providers and endpoint health checks. Empty while only the mock provider is used. |
| `dotnet_*`, `process_*` | various | GC, JIT, thread pool, CPU, memory, and process metrics. |

## Spans

All Keyframe spans come from the `Keyframe` ActivitySource and nest under Watson's `{method} {route}` server span, so one trace runs from the HTTP request down to each model call and ffmpeg process. Video and audio jobs run in the background, so `job video` and `job audio` start their own traces.

| Span | Kind | Parent | Attributes |
| --- | --- | --- | --- |
| `analysis:image` | Internal | Watson request span | `keyframe.analysis_id`, `keyframe.endpoint` |
| `analysis:video` | Internal | Request span or `job video` | `keyframe.analysis_id`, `keyframe.endpoint` |
| `analysis:audio` | Internal | `job audio` | `keyframe.analysis_id` |
| `stage:<Name>` | Internal | Analysis span | `keyframe.media_kind`; the stage is in the span name (`stage:Probe`, `stage:SceneDetection`, `stage:Measure`, `stage:ListenExtraction`, `stage:Summarize`, ...) |
| `inference <Purpose>` | Client | Stage span | `keyframe.purpose`, `keyframe.endpoint`, `keyframe.provider`, `keyframe.model`, `keyframe.input_images`, `keyframe.attempts`, `keyframe.prompt_tokens`, `keyframe.completion_tokens`, and, when the call carries audio, `keyframe.input_audio_clips` and `keyframe.input_audio_bytes` |
| `inference Transcribe` | Client | Transcribe stage | `keyframe.endpoint`, `keyframe.provider` |
| `ffmpeg <operation>` | Internal | Stage span | `keyframe.ffmpeg.operation`, `keyframe.ffmpeg.exit_code` |
| `job video`, `job audio` | Internal | none (root) | `keyframe.job_id`, `keyframe.job_status` |

Clip offsets, URLs, and audio content never go on spans or metric labels.

Spans set `Error` status on caught failures (a failed model call after retries, a failed or timed-out ffmpeg process, a failed analysis or job), so Tempo's `status = error` filter finds them.

## Logs

Server log lines go to the console and to `logs/keyframe.log`. With `ExportLogs` on (the Docker default) they also go over OTLP to the collector and on to Loki, with `service_name="keyframe-server"` as a stream label and the trace and span ids as structured metadata. The Loki datasource links each line to its trace in Tempo. Loki is part of the stack because video analysis runs as background jobs, and their log lines are the only narrative of what a job did.

## Dashboards

The dashboard JSON in `assets/grafana/` is the source of truth. `docker/grafana/dashboards/` is a byte-for-byte mirror so the `docker/` folder works on its own, and CI fails if the two differ. After editing a dashboard, copy it across:

```bash
cp assets/grafana/*.json docker/grafana/dashboards/
```

Grafana provisions all six into the `Keyframe` folder. Datasources use the fixed UIDs `prometheus`, `tempo`, and `loki`.

| Dashboard | UID | What it answers |
| --- | --- | --- |
| Keyframe Overview | `keyframe-overview` | Is the server up, how busy is it, what is failing? Uptime, request rate, 5xx ratio, p95, analyses, jobs, endpoint health, auth failures, memory, recent analysis traces. Start here; it is also Grafana's home dashboard. |
| Keyframe HTTP | `keyframe-http` | Which route is slow or erroring? Rate by route and status, error ratios, latency quantiles, p95 by route, top routes, payload sizes, connections. |
| Keyframe Analysis | `keyframe-analysis` | Where does analysis time go? Throughput by outcome, end-to-end p50/p95/p99 by media kind, per-stage p95, stacked stage time, stage tables, the audio row (including "Listening analyses by mode" and "Listening fallbacks by reason"), slow analysis traces. |
| Keyframe Models | `keyframe-models` | Is a provider slow, failing, or expensive? Calls by purpose and provider, failure ratio, p95 by purpose and provider, outbound HTTP, tokens per second and per call, an "Audio input" row ("Audio bytes sent to models by provider" at p50 and p95, and "Model calls with audio by purpose"), endpoint health, failed inference traces. |
| Keyframe Jobs | `keyframe-jobs` | Is the job queue keeping up? Running and queued, completions by outcome, failure ratio, video analysis duration, job traces, job logs from Loki. |
| Keyframe Media | `keyframe-media` | Is ffmpeg the bottleneck? Invocations by operation, failures and timeouts, p95 and average duration, ffmpeg seconds per second, media stage p95 (now including `measure`, `sectiondetection`, and `listenextraction`), and audio ffmpeg p95 by operation (now including `audio_measure`, `section_detect`, and `listen_extract`). |

The listening panels: "Listening analyses by mode" charts `keyframe_analysis_listen_total` by media kind and mode, where `none` includes requests that did not ask to listen. "Listening fallbacks by reason" keeps only `mode="none"` with a reason other than `none`, `NotRequested`, or `NoModelFunctions`, so every line on it is a request that wanted the audio heard and got a transcript-only answer. "Audio bytes sent to models by provider" covers completion calls only; transcription audio is not in that histogram.

The Analysis, HTTP, Models, and Media dashboards have a variable (media kind, route, provider, operation) to narrow every panel.

## Viewing telemetry

Start the stack from the `docker/` folder:

```bash
cd docker
docker compose up -d --build
```

| Tool | URL | Credentials |
| --- | --- | --- |
| Grafana | `http://127.0.0.1:3700` | `admin` / `admin` |
| Prometheus | `http://127.0.0.1:9700` | none |
| Tempo API | `http://127.0.0.1:3710` | none |
| OTLP collector | `127.0.0.1:4717` (gRPC), `127.0.0.1:4718` (HTTP) | none |

Send a few requests (the factory samples in `docker/factory/samples/` work well), then open Grafana. Some quick checks:

```bash
# Prometheus is scraping the server
curl -s http://127.0.0.1:9700/api/v1/targets | grep -o '"health":"[a-z]*"'

# Analyses so far
curl -s http://127.0.0.1:9700/api/v1/query --data-urlencode 'query=sum by (media_kind, outcome) (keyframe_analysis_total)'

# Recent analysis traces in Tempo
curl -s -G http://127.0.0.1:3710/api/search --data-urlencode 'q={ name =~ "analysis:.*" }'
```

Running from source without Docker, the scrape endpoint is at `http://127.0.0.1:9464/metrics`. To send traces to a collector, set `OtlpEnable` to `true` and point `OtlpEndpoint` at it.

Running the image by itself, give the container the hostname the Prometheus endpoint binds, and send that Host header:

```bash
docker run -d --name keyframe --hostname keyframe-server -p 8700:8700 -p 127.0.0.1:9464:9464 jchristn77/keyframe-server:v0.1.0
curl -H "Host: keyframe-server:9464" http://127.0.0.1:9464/metrics
```

## Production notes

- Change the Grafana admin password for anything beyond a laptop. Set `GRAFANA_ADMIN_PASSWORD` in an untracked `.env` file next to `compose.yaml`; sign-up is disabled.
- Prometheus, Tempo, Loki, the collector, and the scrape endpoint have no authentication. The stack binds their host ports to `127.0.0.1` and leaves the scrape endpoint and Loki unpublished. Keep it that way on shared hosts.
- Ids and secrets never appear in metric labels. Spans carry analysis and job ids because spans are per request, never prompts, keys, or media content.
