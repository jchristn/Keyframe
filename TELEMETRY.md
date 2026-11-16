# Keyframe Telemetry

Keyframe reports telemetry on two layers. Each analysis response carries its own `Telemetry` block (stage timings, every model call, token totals), which is described in [REST_API.md](REST_API.md). This document covers the second layer: the operational metrics, traces, and logs the server emits for the observability stack, and the Grafana dashboards built on them.

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
| `keyframe.jobs.total` | `keyframe_jobs_total` | Counter | `outcome` | Video jobs finished. |
| `keyframe.jobs.active` | `keyframe_jobs_active` | Gauge | none | Jobs running now. |
| `keyframe.jobs.queued` | `keyframe_jobs_queued` | Gauge | none | Jobs waiting for a slot. |
| `keyframe.auth.requests` | `keyframe_auth_requests_total` | Counter | `result` | Authentication outcomes. |
| `keyframe.endpoint.healthy` | `keyframe_endpoint_healthy` | Gauge | `endpoint` | 1 when a configured model endpoint is healthy, 0 when not. |

### Label values

Every label has a small, fixed set of values. No ids, paths, prompts, or user input ever become labels.

| Label | Values |
| --- | --- |
| `media_kind` | `image`, `video` |
| `outcome` (analysis) | `success`, `partial` (completed, but a field failed validation), `failure` |
| `stage` | `resolve`, `probe`, `normalize`, `scenedetection`, `frameextraction`, `audioextraction`, `transcribe`, `scenes`, `summarize`, `extract` |
| `purpose` | `summarize`, `extract`, `scenesummarize`, `sceneextract`, `transcribe` |
| `provider` | Lowercased provider type: `mock`, `ollama`, `ollamacloud`, `openai`, `openaicompatible`, `azureopenai`, `anthropic`, `gemini`, `vertexai`, `bedrock`, `xai`, `cohere` |
| `outcome` (model call) | `success`, `failure` |
| `kind` | `prompt`, `completion` |
| `operation` | `version`, `probe`, `normalize`, `scene_detect`, `frame_extract`, `audio_extract` |
| `outcome` (ffmpeg) | `success`, `failure`, `timeout`, `canceled`, `unavailable` |
| `outcome` (jobs) | `succeeded`, `failed`, `canceled`, `interrupted` |
| `result` | `success`, `failure`, `rate_limited` |
| `endpoint` | Endpoint names from `Inference.Endpoints` in settings (operator defined, few) |

Whole-video summary and extraction record their time under the `summarize` and `extract` stages.

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

All Keyframe spans come from the `Keyframe` ActivitySource and nest under Watson's `{method} {route}` server span, so one trace runs from the HTTP request down to each model call and ffmpeg process. Video jobs run in the background, so `job video` starts its own trace.

| Span | Kind | Parent | Attributes |
| --- | --- | --- | --- |
| `analysis:image` | Internal | Watson request span | `keyframe.analysis_id`, `keyframe.endpoint` |
| `analysis:video` | Internal | Request span or `job video` | `keyframe.analysis_id`, `keyframe.endpoint` |
| `stage:<Name>` | Internal | Analysis span | `keyframe.media_kind`; the stage is in the span name (`stage:Probe`, `stage:SceneDetection`, `stage:Summarize`, ...) |
| `inference <Purpose>` | Client | Stage span | `keyframe.purpose`, `keyframe.endpoint`, `keyframe.provider`, `keyframe.model`, `keyframe.input_images`, `keyframe.attempts`, `keyframe.prompt_tokens`, `keyframe.completion_tokens` |
| `inference Transcribe` | Client | Transcribe stage | `keyframe.endpoint`, `keyframe.provider` |
| `ffmpeg <operation>` | Internal | Stage span | `keyframe.ffmpeg.operation`, `keyframe.ffmpeg.exit_code` |
| `job video` | Internal | none (root) | `keyframe.job_id`, `keyframe.job_status` |

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
| Keyframe Analysis | `keyframe-analysis` | Where does analysis time go? Throughput by outcome, end-to-end p50/p95/p99 by media kind, per-stage p95, stacked stage time, stage tables, slow analysis traces. |
| Keyframe Models | `keyframe-models` | Is a provider slow, failing, or expensive? Calls by purpose and provider, failure ratio, p95 by purpose and provider, outbound HTTP, tokens per second and per call, endpoint health, failed inference traces. |
| Keyframe Jobs | `keyframe-jobs` | Is the job queue keeping up? Running and queued, completions by outcome, failure ratio, video analysis duration, job traces, job logs from Loki. |
| Keyframe Media | `keyframe-media` | Is ffmpeg the bottleneck? Invocations by operation, failures and timeouts, p95 and average duration, ffmpeg seconds per second, media stage p95. |

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
