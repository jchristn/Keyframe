# keyframe_sdk (Python)

Python client for the Keyframe REST API. Keyframe summarizes images, video, and audio, extracts typed metadata fields, cuts media segments, and records analytics.

- Python 3.10 or later.
- Built on [httpx](https://www.python-httpx.org/); dataclass models.

Version 0.1.0 (ALPHA).

## Install

```bash
pip install keyframe_sdk
# or, from this directory
pip install -e ".[test]"
```

## Quick start

```python
from keyframe_sdk import (
    ExtractSpec, FieldSpec, FieldType, FunctionsSpec, ImageAnalysisRequest,
    KeyframeClient, SummarizeSpec, media_from_file,
)

with KeyframeClient("http://127.0.0.1:8700", "default") as client:
    request = ImageAnalysisRequest(
        image=media_from_file("backyard.jpg"),
        context=[
            {"Label": "Source", "Text": "Photos submitted by pet adoption volunteers."},
            "Animals wearing orange collars are already adopted.",
        ],
        functions=FunctionsSpec(
            summarize=SummarizeSpec(enable=True, max_words=60),
            extract=ExtractSpec(enable=True, fields=[
                FieldSpec(key="hasCats", type=FieldType.BOOLEAN, prompt="Does this image have cats?"),
                FieldSpec(key="dogCount", type=FieldType.INTEGER, prompt="How many dogs?", minimum=0),
            ]),
        ),
    )
    result = client.image.analyze(request)
    print(result.results["Summary"], result.results["hasCats"], result.results["dogCount"])
```

Models use snake_case attributes and serialize to the server's PascalCase JSON (`max_words` becomes `MaxWords`). Every method that takes a request also accepts a plain PascalCase `dict`. Enums are `str` enums, so `JobStatus.SUCCEEDED == "Succeeded"`. `context` accepts a string, a list of strings, `ContextItem` objects, `{"Label", "Text"}` dicts, or a mix. `Results` values stay as the JSON types the server sent (bool, int, float, str, list, dict). Timestamps are ISO 8601 strings.

Unknown fields returned by the server are kept in each model's `extra` dict and written back by `to_dict()`, so a settings round trip does not drop fields this SDK version does not know about.

Base URLs use the IPv4 loopback `127.0.0.1` so Windows clients do not stall on an IPv6 fallback.

## Surface

| Member | Route |
|---|---|
| `health.get()`, `health.is_alive()` | `GET /v1.0/health`, `HEAD /` (anonymous) |
| `whoami.get()` | `GET /v1.0/whoami` |
| `image.analyze(request)` | `POST /v1.0/image` |
| `analyze_image_file(path, request)` | Reads the file, then `image.analyze` |
| `video.analyze(request)` | `POST /v1.0/video`, returns `VideoSubmitResult(status_code, job_id, job, result)` |
| `video.analyze_and_wait(request, poll_interval_s, timeout_s)`, alias `analyze_video_and_wait` | Submits, polls until terminal, fetches the result |
| `audio.analyze(request)` | `POST /v1.0/audio`, returns `AudioSubmitResult(status_code, job_id, job, result)` |
| `audio.analyze_and_wait(request, poll_interval_s, timeout_s)` | Submits, polls until terminal, fetches the `AudioAnalysisResponse` |
| `segments.get(request)` | `POST /v1.0/segment` with `ResponseFormat` forced to `Binary`, returns `SegmentMedia` |
| `segments.get_json(request)` | `POST /v1.0/segment` with `ResponseFormat` forced to `Json`, returns `SegmentResponse` |
| `probe.probe(request_or_media)` | `POST /v1.0/probe` |
| `jobs.list(...)`, `get(id)`, `get_result(id)`, `get_audio_result(id)`, `cancel(id)`, `delete(id)`, `wait(id, ...)` | `/v1.0/jobs` |
| `endpoints.list()`, `endpoints.test(name)` (admin) | `/v1.0/endpoints` |
| `request_history.list(...)`, `summary(...)`, `get(id)`, `delete(id)` (admin), `delete_many(...)` (admin) | `/v1.0/api/request-history` |
| `analytics.summary(from_utc, to_utc, bucket, media_kind, endpoint, model)` (admin) | `GET /v1.0/analytics/summary` |
| `analytics.list_analyses(media_kind, success, from_utc, to_utc, max_results, skip)` (admin) | `GET /v1.0/analytics/analyses` |
| `analytics.get_analysis(id)`, `delete_analysis(id)` (admin) | `/v1.0/analytics/analyses/{id}` |
| `analytics.delete_analyses(older_than_utc=None)` (admin) | `DELETE /v1.0/analytics/analyses` |
| `settings.get()`, `update(settings)`, `restart()` (all admin) | `/v1.0/settings`, `/v1.0/restart` |
| `send_raw(method, path, body, params, binary=False)` | Any route, no exception on error status. With `binary=True` the body bytes are in `content` |

List methods return `EnumerationResult` whose `items` are typed models (`JobResponse`, `RequestHistoryEntry`, `AnalysisRecord`).

`jobs.get_result(id)` parses a video job's result as `VideoAnalysisResponse`; use `jobs.get_audio_result(id)` for an audio job (`AudioAnalysisResponse`).

## Video

```python
from keyframe_sdk import (
    KeyframeJobError, ScenesSpec, SummarizeSpec, TranscribeSpec,
    VideoAnalysisRequest, VideoFunctionsSpec, media_from_url,
)

request = VideoAnalysisRequest(
    video=media_from_url("https://example.com/talks/keynote.mp4"),
    functions=VideoFunctionsSpec(
        summarize=SummarizeSpec(enable=True),
        scenes=ScenesSpec(enable=True, summarize=SummarizeSpec(enable=True)),
        transcribe=TranscribeSpec(enable=True),
    ),
)

try:
    result = client.analyze_video_and_wait(request, poll_interval_s=1.0, timeout_s=600)
    print(len(result.scenes), result.transcript.text if result.transcript else None)
except KeyframeJobError as e:
    print("job ended", e.job.status, e.job.error)
```

With `wait=True`, `video.analyze` may return `result` directly (the job id comes from the `x-keyframe-job-id` header). A job that finished without succeeding comes back as `job`.

## Audio

`AudioAnalysisRequest` takes an audio file (mp3, wav, flac, m4a or aac, ogg or opus, webm audio) or a video file (only its audio track is used). `AudioFunctionsSpec` defaults to Transcribe and Summarize on and Extract off. Summarize and Extract run on the transcript with `text_model` (else `Inference.DefaultText`); `transcription_model` picks the transcription endpoint.

```python
from keyframe_sdk import (
    AudioAnalysisRequest, AudioFunctionsSpec, ExtractSpec, FieldSpec, FieldType,
    ModelSelection, SummarizeSpec, SummaryStyle, TranscribeSpec, media_from_file,
)

request = AudioAnalysisRequest(
    audio=media_from_file("standup.mp3"),
    context="Daily standup of the platform team.",
    transcription_model=ModelSelection(endpoint="whisper"),
    functions=AudioFunctionsSpec(
        transcribe=TranscribeSpec(enable=True, language="en"),
        summarize=SummarizeSpec(enable=True, style=SummaryStyle.BULLETS, max_words=120),
        extract=ExtractSpec(enable=True, fields=[
            FieldSpec(key="blockers", type=FieldType.BOOLEAN, prompt="Does anyone report a blocker?"),
        ]),
    ),
)
result = client.audio.analyze_and_wait(request, poll_interval_s=1.0, timeout_s=600)
print(result.transcript.text, result.results["Summary"], result.telemetry.queue_wait_ms)
for call in result.telemetry.model_calls:
    print(call.purpose, call.duration_ms, call.queue_ms, call.time_to_first_token_ms, call.generation_ms, call.tokens_per_second)
```

`audio.analyze` behaves like `video.analyze`: `202` with `job`, or with `wait=True` either `result` (job id from `x-keyframe-job-id`) or a finished non-succeeded `job`.

## Segments

`segments.get` cuts `[start_ms, end_ms)` out of a video or audio source and returns the bytes. Keyframe keeps no uploaded media, so send the same source you analyzed. `output` is `Video` (`mp4` or `webm`), `Audio` (`mp3`, `wav`, `flac`, `m4a`, `ogg`), or `Frame` (`jpg` or `png`, one frame at `start_ms`, `end_ms` ignored). `max_edge` caps the long edge of video and frame output.

```python
from keyframe_sdk import SegmentOutput, SegmentRequest

scene = video_result.scenes[2]  # from video.analyze_and_wait
clip = client.segments.get(SegmentRequest(
    source=media_from_file("keynote.mp4"), start_ms=scene.start_ms, end_ms=scene.end_ms,
    output=SegmentOutput.VIDEO, max_edge=720,
))
print(clip.media_type, clip.filename, clip.start_ms, clip.end_ms, clip.duration_ms, clip.size_bytes)
clip.save(clip.filename)

frame = client.segments.get_json(SegmentRequest(source=media_from_file("keynote.mp4"), start_ms=12000, output="Frame"))
jpeg = frame.decode_data()  # len(jpeg) == frame.size_bytes
```

`SegmentMedia` holds `data` (bytes), `media_type` (Content-Type), `filename` (from Content-Disposition), `start_ms`, `end_ms`, `duration_ms` (from the `x-keyframe-segment-*` headers; an `end_ms` past the end of the media is clamped), `size_bytes`, and `request_id`. Invalid ranges and formats raise `KeyframeApiError` with status 400; a missing stream or a start past the end is 422.

## Analytics

Every finished analysis is stored with one row per model call attempt. The analytics routes need the admin key.

```python
from keyframe_sdk import MediaKind

summary = admin.analytics.summary(bucket="hour", media_kind=MediaKind.AUDIO)
print(summary.totals.count, summary.totals.success_rate, summary.totals.analysis_ms.p95 if summary.totals.analysis_ms else None)
for bucket in summary.buckets:
    print(bucket.start_utc, bucket.stats.count)
for m in summary.by_model:
    print(m.endpoint, m.model, m.calls, m.tokens_per_second.avg if m.tokens_per_second else None)

page = admin.analytics.list_analyses(media_kind="Audio", success=True, max_results=20)
record = admin.analytics.get_analysis(page.items[0].id)  # record.calls holds ModelCallRecord objects
admin.analytics.delete_analysis(record.id)
admin.analytics.delete_analyses(older_than_utc="2026-01-01T00:00:00Z")  # no cutoff deletes everything
```

`AnalyticsSummary` has `totals` and `by_media_kind` (`Image`, `Video`, `Audio`) as `StatBlock`, gap-filled `buckets` (`AnalyticsBucket`), and `by_model` (`ModelStats`), `by_stage` (`StageStats`), `by_purpose` (`PurposeStats`). Timing values are `Distribution` objects (`count`, `min`, `max`, `avg`, `p50`, `p90`, `p95`, `p99`, `std_dev`, `sum`) or None when there are no samples. `bucket` is `minute`, `hour`, or `day`; by default the range is the last 24 hours and the bucket is chosen from the range.

## Authentication

Pass the access key or admin key as `api_key`. The client sends `Authorization: Bearer <key>` by default, or `x-api-key` with `auth_header="x-api-key"`. The key is stored privately, sent per request (not as a default client header), and never appears in `repr()` or `str()`. Use `set_api_key()` to change it.

## Errors

| Exception | When |
|---|---|
| `KeyframeApiError` | Non-2xx response. `status_code`, `error` (for example `"NotFound"`), `api_error` (`ApiErrorResponse`), `details`, `request_id`, `response_body`, `method`, `path`. |
| `KeyframeJobError` | `analyze_and_wait` saw the job end `Failed`, `Canceled`, or `Interrupted`. `job` holds the final state. |
| `KeyframeTimeoutError` | A request exceeded `timeout_s` (default 300), or a wait exceeded its limit. |
| `KeyframeConnectionError` | The server could not be reached. |

All derive from `KeyframeError`.

## Settings

`settings.get()` returns secrets masked as `****` plus the last four characters. Send the object back through `settings.update()` with masked values unchanged and the server keeps the stored secrets. `update` sends nulls explicitly and replaces the whole object, so start from a fresh `get()`.

Settings include `analytics` (`enabled`, `retention_days`) and `media.max_segment_ms` and `media.max_audio_duration_ms`.

## Test harness

`tests/test_sdk.py` uses pytest and runs end to end against a live server using the mock provider. It generates its media fixtures (image, short and long video, a short wav tone) with ffmpeg at run time. Audio, segment, and analytics tests need a completion endpoint and a transcription endpoint (the mock providers).

```bash
cd sdk/python
python3 -m venv .venv && . .venv/bin/activate
pip install -e ".[test]"
KEYFRAME_URL=http://127.0.0.1:8710 KEYFRAME_ADMIN_KEY=keyframeadmin KEYFRAME_ACCESS_KEY=default pytest -v
```

The defaults are `http://127.0.0.1:8710`, `keyframeadmin`, and `default`. The harness performs one live settings write (it bumps `RequestHistory.RetentionDays` and restores it) and never calls `/v1.0/restart` with the admin key.

## License

MIT. See `LICENSE.md` in the repository root.
