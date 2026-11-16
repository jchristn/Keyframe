# keyframe_sdk (Python)

Python client for the Keyframe REST API. Keyframe summarizes images and video and extracts typed metadata fields.

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
| `probe.probe(request_or_media)` | `POST /v1.0/probe` |
| `jobs.list(...)`, `get(id)`, `get_result(id)`, `cancel(id)`, `delete(id)`, `wait(id, ...)` | `/v1.0/jobs` |
| `endpoints.list()`, `endpoints.test(name)` (admin) | `/v1.0/endpoints` |
| `request_history.list(...)`, `summary(...)`, `get(id)`, `delete(id)` (admin), `delete_many(...)` (admin) | `/v1.0/api/request-history` |
| `settings.get()`, `update(settings)`, `restart()` (all admin) | `/v1.0/settings`, `/v1.0/restart` |
| `send_raw(method, path, body, params)` | Any route, no exception on error status |

List methods return `EnumerationResult` whose `items` are typed models (`JobResponse`, `RequestHistoryEntry`).

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

## Test harness

`tests/test_sdk.py` uses pytest and runs end to end against a live server using the mock provider. It generates its media fixtures with ffmpeg at run time.

```bash
cd sdk/python
python3 -m venv .venv && . .venv/bin/activate
pip install -e ".[test]"
KEYFRAME_URL=http://127.0.0.1:8710 KEYFRAME_ADMIN_KEY=keyframeadmin KEYFRAME_ACCESS_KEY=default pytest -v
```

The defaults are `http://127.0.0.1:8710`, `keyframeadmin`, and `default`. The harness performs one live settings write (it bumps `RequestHistory.RetentionDays` and restores it) and never calls `/v1.0/restart` with the admin key.

## License

MIT. See `LICENSE.md` in the repository root.
