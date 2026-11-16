"""Synchronous Keyframe client built on httpx."""

from __future__ import annotations

import base64
import json
import mimetypes
import time
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Mapping, Optional, Type, TypeVar, Union
from urllib.parse import quote, urlsplit

import httpx

from ._base import Model, encode
from .errors import (
    KeyframeApiError,
    KeyframeConnectionError,
    KeyframeJobError,
    KeyframeTimeoutError,
)
from .models import (
    ApiErrorResponse,
    DeleteResult,
    EndpointResponse,
    EndpointTestResponse,
    EnumerationResult,
    HealthResponse,
    ImageAnalysisRequest,
    ImageAnalysisResponse,
    JobResponse,
    JobStatus,
    KeyframeSettings,
    MediaProperties,
    MediaSource,
    ProbeRequest,
    RequestHistoryEntry,
    RequestHistorySummary,
    RestartResponse,
    SettingsResponse,
    VideoAnalysisRequest,
    VideoAnalysisResponse,
    VideoSubmitResult,
    WhoAmIResponse,
    is_terminal_status,
)

__all__ = ["KeyframeClient", "RawResponse", "media_from_url", "media_from_bytes", "media_from_file", "DEFAULT_BASE_URL"]

DEFAULT_BASE_URL = "http://127.0.0.1:8700"
VERSION = "0.1.0"

T = TypeVar("T")
RequestLike = Union[Model, Mapping[str, Any]]

_EXTRA_TYPES = {
    ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".png": "image/png", ".gif": "image/gif", ".webp": "image/webp",
    ".mp4": "video/mp4", ".m4v": "video/mp4", ".mov": "video/quicktime", ".webm": "video/webm", ".mkv": "video/x-matroska",
}


def media_from_url(url: str, media_type: Optional[str] = None) -> MediaSource:
    """Media referenced by an absolute http or https URL."""
    parts = urlsplit(url or "")
    if parts.scheme not in ("http", "https") or not parts.netloc:
        raise ValueError("URL must be an absolute http or https URL.")
    return MediaSource(url=url, media_type=media_type)


def media_from_bytes(data: bytes, media_type: Optional[str] = None) -> MediaSource:
    """Media sent inline as base64."""
    if not data:
        raise ValueError("Media data is empty.")
    return MediaSource(data=base64.b64encode(data).decode("ascii"), media_type=media_type)


def media_from_file(path: Union[str, Path], media_type: Optional[str] = None) -> MediaSource:
    """Media read from a local file and sent inline as base64. The MIME type is guessed from the extension."""
    p = Path(path)
    guessed = media_type or _EXTRA_TYPES.get(p.suffix.lower()) or mimetypes.guess_type(p.name)[0]
    return media_from_bytes(p.read_bytes(), guessed)


@dataclass
class RawResponse:
    """Status, body, and headers of one exchange."""

    status_code: int
    body: str
    headers: httpx.Headers

    @property
    def request_id(self) -> Optional[str]:
        return self.headers.get("x-request-id")


def _body(request: Optional[RequestLike], omit_none: bool) -> Any:
    if request is None:
        return None
    if isinstance(request, Model):
        return request.to_dict(omit_none=omit_none)
    if isinstance(request, Mapping):
        return encode(dict(request), omit_none)
    raise TypeError("request must be a model instance or a dict.")


def _iso(value: Any) -> Optional[str]:
    if value is None or value == "":
        return None
    if isinstance(value, datetime):
        if value.tzinfo is None:
            value = value.replace(tzinfo=timezone.utc)
        return value.astimezone(timezone.utc).isoformat().replace("+00:00", "Z")
    return str(getattr(value, "value", value))


def _segment(value: str, name: str) -> str:
    if not isinstance(value, str) or not value.strip():
        raise ValueError(f"{name} is required.")
    return quote(value, safe="")


def _query(values: Dict[str, Any]) -> Dict[str, str]:
    out: Dict[str, str] = {}
    for key, value in values.items():
        text = _iso(value)
        if text is not None:
            out[key] = text
    return out


class KeyframeClient:
    """Client for the Keyframe REST API.

    The API key is held privately and never logged, returned, or included in ``repr``.
    Use as a context manager, or call ``close()``.
    """

    def __init__(
        self,
        base_url: Optional[str] = None,
        api_key: Optional[str] = None,
        *,
        auth_header: str = "bearer",
        timeout_s: float = 300.0,
        transport: Optional[httpx.BaseTransport] = None,
    ) -> None:
        url = (base_url or DEFAULT_BASE_URL).strip()
        parts = urlsplit(url)
        if parts.scheme not in ("http", "https") or not parts.netloc:
            raise ValueError("base_url must be an absolute http or https URL.")
        self.base_url = url.rstrip("/")
        self.__api_key = api_key or None
        self.auth_header = "x-api-key" if auth_header == "x-api-key" else "bearer"
        self.timeout_s = min(3600.0, max(1.0, float(timeout_s)))
        self._http = httpx.Client(transport=transport, timeout=self.timeout_s, follow_redirects=False)

        self.health = HealthClient(self)
        self.whoami = WhoAmIClient(self)
        self.image = ImageClient(self)
        self.video = VideoClient(self)
        self.probe = ProbeClient(self)
        self.jobs = JobsClient(self)
        self.endpoints = EndpointsClient(self)
        self.request_history = RequestHistoryClient(self)
        self.settings = SettingsClient(self)

    # Lifecycle

    def close(self) -> None:
        """Close the underlying HTTP connection pool."""
        self._http.close()

    def __enter__(self) -> "KeyframeClient":
        return self

    def __exit__(self, *exc: Any) -> None:
        self.close()

    def __repr__(self) -> str:
        return f"KeyframeClient({self.base_url!r}, {'key set' if self.has_api_key else 'no key'})"

    __str__ = __repr__

    # Credentials

    @property
    def has_api_key(self) -> bool:
        """Whether an API key is configured. The key itself is not readable."""
        return bool(self.__api_key)

    def set_api_key(self, api_key: Optional[str]) -> None:
        """Replace the API key. Empty or None clears it."""
        self.__api_key = api_key or None

    # Convenience

    def analyze_image_file(self, path: Union[str, Path], request: Optional[RequestLike] = None) -> ImageAnalysisResponse:
        """Read an image file and analyze it. ``request`` supplies Context, Functions, and Model."""
        body = _body(request, True) if request is not None else {}
        body["Image"] = media_from_file(path).to_dict(omit_none=True)
        return self.image.analyze(body)

    def analyze_video_and_wait(
        self, request: RequestLike, poll_interval_s: Optional[float] = None, timeout_s: Optional[float] = None
    ) -> VideoAnalysisResponse:
        """Alias of ``video.analyze_and_wait``."""
        return self.video.analyze_and_wait(request, poll_interval_s=poll_interval_s, timeout_s=timeout_s)

    # Transport

    def send_raw(
        self, method: str, path: str, body: Any = None, params: Optional[Dict[str, str]] = None
    ) -> RawResponse:
        """Send a request and return the raw response without raising on error status codes."""
        headers = {"Accept": "application/json", "User-Agent": f"keyframe-sdk-python/{VERSION}"}
        if self.__api_key:
            if self.auth_header == "x-api-key":
                headers["x-api-key"] = self.__api_key
            else:
                headers["Authorization"] = "Bearer " + self.__api_key
        content = None
        if body is not None:
            headers["Content-Type"] = "application/json"
            content = json.dumps(body).encode("utf-8")
        url = self.base_url + (path if path.startswith("/") else "/" + path)
        try:
            response = self._http.request(method, url, headers=headers, content=content, params=params)
        except httpx.TimeoutException as e:
            raise KeyframeTimeoutError(f"{method} {path} did not complete within {self.timeout_s}s.") from e
        except httpx.TransportError as e:
            raise KeyframeConnectionError(f"{method} {path} failed: unable to reach {self.base_url}.") from e
        return RawResponse(status_code=response.status_code, body=response.text, headers=response.headers)

    def _checked(self, method: str, path: str, body: Any = None, params: Optional[Dict[str, str]] = None) -> RawResponse:
        raw = self.send_raw(method, path, body, params)
        if raw.status_code < 200 or raw.status_code > 299:
            api_error: Optional[ApiErrorResponse] = None
            if raw.body:
                try:
                    parsed = json.loads(raw.body)
                    if isinstance(parsed, dict):
                        api_error = ApiErrorResponse.from_dict(parsed)
                except ValueError:
                    api_error = None
            raise KeyframeApiError(method, path, raw.status_code, api_error, raw.body, raw.request_id)
        return raw

    def _json(self, method: str, path: str, body: Any = None, params: Optional[Dict[str, str]] = None) -> Any:
        raw = self._checked(method, path, body, params)
        if not raw.body:
            raise KeyframeApiError(method, path, raw.status_code, None, raw.body, raw.request_id)
        return json.loads(raw.body)

    def _model(self, cls: Type[T], method: str, path: str, body: Any = None, params: Optional[Dict[str, str]] = None) -> T:
        return cls.from_dict(self._json(method, path, body, params))  # type: ignore[attr-defined]

    def _page(self, item: Type[Any], path: str, params: Dict[str, str]) -> EnumerationResult:
        data = self._json("GET", path, None, params)
        page = EnumerationResult.from_dict(data)
        page.items = [item.from_dict(i) for i in data.get("Items", [])]
        return page


class _Sub:
    def __init__(self, client: KeyframeClient) -> None:
        self._c = client


class HealthClient(_Sub):
    def get(self) -> HealthResponse:
        """GET /v1.0/health (anonymous)."""
        return self._c._model(HealthResponse, "GET", "/v1.0/health")

    def is_alive(self) -> bool:
        """HEAD / returns 200. Never raises for HTTP or connection errors."""
        try:
            return self._c.send_raw("HEAD", "/").status_code == 200
        except (KeyframeConnectionError, KeyframeTimeoutError):
            return False


class WhoAmIClient(_Sub):
    def get(self) -> WhoAmIResponse:
        """GET /v1.0/whoami."""
        return self._c._model(WhoAmIResponse, "GET", "/v1.0/whoami")


class ImageClient(_Sub):
    def analyze(self, request: RequestLike) -> ImageAnalysisResponse:
        """POST /v1.0/image. Accepts an ImageAnalysisRequest or a PascalCase dict."""
        return self._c._model(ImageAnalysisResponse, "POST", "/v1.0/image", _body(request, True))


class ProbeClient(_Sub):
    def probe(self, request: Union[ProbeRequest, MediaSource, Mapping[str, Any]]) -> MediaProperties:
        """POST /v1.0/probe. Accepts a ProbeRequest, a MediaSource, or a dict."""
        if isinstance(request, MediaSource):
            request = ProbeRequest(media=request)
        return self._c._model(MediaProperties, "POST", "/v1.0/probe", _body(request, True))


class VideoClient(_Sub):
    def analyze(self, request: RequestLike) -> VideoSubmitResult:
        """POST /v1.0/video. Returns a VideoSubmitResult holding either ``job`` (202, or a finished non-succeeded job)
        or ``result`` (200 when Wait was true and the job succeeded in time)."""
        raw = self._c._checked("POST", "/v1.0/video", _body(request, True))
        data = json.loads(raw.body)
        if raw.status_code == 202 or isinstance(data.get("Status"), str):
            job = JobResponse.from_dict(data)
            return VideoSubmitResult(status_code=raw.status_code, job_id=job.id, job=job)
        return VideoSubmitResult(
            status_code=raw.status_code,
            job_id=raw.headers.get("x-keyframe-job-id", ""),
            result=VideoAnalysisResponse.from_dict(data),
        )

    def analyze_and_wait(
        self, request: RequestLike, poll_interval_s: Optional[float] = None, timeout_s: Optional[float] = None
    ) -> VideoAnalysisResponse:
        """Submit, poll the job until terminal, then fetch the result.
        Raises KeyframeJobError when the job ends Failed, Canceled, or Interrupted."""
        submitted = self.analyze(request)
        if submitted.result is not None:
            return submitted.result
        assert submitted.job is not None
        job = submitted.job if submitted.job.is_terminal else self._c.jobs.wait(submitted.job_id, poll_interval_s, timeout_s)
        if job.status != JobStatus.SUCCEEDED:
            raise KeyframeJobError(job)
        return self._c.jobs.get_result(job.id)


class JobsClient(_Sub):
    #: Default polling interval in seconds for ``wait`` (clamped 0.1 to 60).
    default_poll_interval_s: float = 1.0
    #: Default overall wait limit in seconds for ``wait`` (clamped 1 to 86400).
    default_wait_timeout_s: float = 600.0

    def list(
        self,
        status: Optional[Union[JobStatus, str]] = None,
        media_kind: Optional[str] = None,
        principal_name: Optional[str] = None,
        from_utc: Optional[Union[datetime, str]] = None,
        to_utc: Optional[Union[datetime, str]] = None,
        page_number: Optional[int] = None,
        page_size: Optional[int] = None,
    ) -> EnumerationResult:
        """GET /v1.0/jobs. ``items`` holds JobResponse objects."""
        params = _query({
            "status": status, "mediaKind": media_kind, "principalName": principal_name, "fromUtc": from_utc,
            "toUtc": to_utc, "pageNumber": page_number, "pageSize": page_size,
        })
        return self._c._page(JobResponse, "/v1.0/jobs", params)

    def get(self, job_id: str) -> JobResponse:
        """GET /v1.0/jobs/{id}."""
        return self._c._model(JobResponse, "GET", "/v1.0/jobs/" + _segment(job_id, "job_id"))

    def get_result(self, job_id: str) -> VideoAnalysisResponse:
        """GET /v1.0/jobs/{id}/result. 409 until the job succeeded."""
        return self._c._model(VideoAnalysisResponse, "GET", "/v1.0/jobs/" + _segment(job_id, "job_id") + "/result")

    def cancel(self, job_id: str) -> JobResponse:
        """POST /v1.0/jobs/{id}/cancel. 409 when already finished."""
        return self._c._model(JobResponse, "POST", "/v1.0/jobs/" + _segment(job_id, "job_id") + "/cancel")

    def delete(self, job_id: str) -> None:
        """DELETE /v1.0/jobs/{id}. 409 while active."""
        self._c._checked("DELETE", "/v1.0/jobs/" + _segment(job_id, "job_id"))

    def wait(self, job_id: str, poll_interval_s: Optional[float] = None, timeout_s: Optional[float] = None) -> JobResponse:
        """Poll until the job is terminal and return it. Raises KeyframeTimeoutError after ``timeout_s``."""
        _segment(job_id, "job_id")
        interval = min(60.0, max(0.1, poll_interval_s if poll_interval_s is not None else self.default_poll_interval_s))
        limit = min(86400.0, max(1.0, timeout_s if timeout_s is not None else self.default_wait_timeout_s))
        deadline = time.monotonic() + limit
        while True:
            job = self.get(job_id)
            if is_terminal_status(job.status):
                return job
            if time.monotonic() >= deadline:
                raise KeyframeTimeoutError(f"Job {job_id} is still {job.status} after {limit}s.")
            time.sleep(interval)


class EndpointsClient(_Sub):
    def list(self) -> List[EndpointResponse]:
        """GET /v1.0/endpoints."""
        return [EndpointResponse.from_dict(e) for e in self._c._json("GET", "/v1.0/endpoints")]

    def test(self, name: str) -> EndpointTestResponse:
        """POST /v1.0/endpoints/{name}/test (admin)."""
        return self._c._model(EndpointTestResponse, "POST", "/v1.0/endpoints/" + _segment(name, "name") + "/test")


_HISTORY = "/v1.0/api/request-history"


def _history_filters(
    method: Optional[str], status_code: Optional[int], path_contains: Optional[str],
    principal_name: Optional[str], from_utc: Any, to_utc: Any,
) -> Dict[str, Any]:
    return {
        "method": method, "statusCode": status_code, "pathContains": path_contains,
        "principalName": principal_name, "fromUtc": from_utc, "toUtc": to_utc,
    }


class RequestHistoryClient(_Sub):
    def list(
        self,
        method: Optional[str] = None,
        status_code: Optional[int] = None,
        path_contains: Optional[str] = None,
        principal_name: Optional[str] = None,
        from_utc: Optional[Union[datetime, str]] = None,
        to_utc: Optional[Union[datetime, str]] = None,
        page_number: Optional[int] = None,
        page_size: Optional[int] = None,
    ) -> EnumerationResult:
        """GET /v1.0/api/request-history. Bodies are omitted. ``items`` holds RequestHistoryEntry objects."""
        values = _history_filters(method, status_code, path_contains, principal_name, from_utc, to_utc)
        values.update({"pageNumber": page_number, "pageSize": page_size})
        return self._c._page(RequestHistoryEntry, _HISTORY, _query(values))

    def summary(
        self,
        method: Optional[str] = None,
        status_code: Optional[int] = None,
        path_contains: Optional[str] = None,
        principal_name: Optional[str] = None,
        from_utc: Optional[Union[datetime, str]] = None,
        to_utc: Optional[Union[datetime, str]] = None,
        bucket_minutes: Optional[int] = None,
    ) -> RequestHistorySummary:
        """GET /v1.0/api/request-history/summary."""
        values = _history_filters(method, status_code, path_contains, principal_name, from_utc, to_utc)
        values["bucketMinutes"] = bucket_minutes
        return self._c._model(RequestHistorySummary, "GET", _HISTORY + "/summary", None, _query(values))

    def get(self, entry_id: str) -> RequestHistoryEntry:
        """GET /v1.0/api/request-history/{id}."""
        return self._c._model(RequestHistoryEntry, "GET", _HISTORY + "/" + _segment(entry_id, "entry_id"))

    def delete(self, entry_id: str) -> None:
        """DELETE /v1.0/api/request-history/{id} (admin)."""
        self._c._checked("DELETE", _HISTORY + "/" + _segment(entry_id, "entry_id"))

    def delete_many(
        self,
        method: Optional[str] = None,
        status_code: Optional[int] = None,
        path_contains: Optional[str] = None,
        principal_name: Optional[str] = None,
        from_utc: Optional[Union[datetime, str]] = None,
        to_utc: Optional[Union[datetime, str]] = None,
    ) -> DeleteResult:
        """DELETE /v1.0/api/request-history (admin). With no filter, deletes every entry."""
        values = _history_filters(method, status_code, path_contains, principal_name, from_utc, to_utc)
        return self._c._model(DeleteResult, "DELETE", _HISTORY, None, _query(values))


class SettingsClient(_Sub):
    def get(self) -> SettingsResponse:
        """GET /v1.0/settings (admin). Secrets are masked."""
        return self._c._model(SettingsResponse, "GET", "/v1.0/settings")

    def update(self, settings: Union[KeyframeSettings, Mapping[str, Any]]) -> SettingsResponse:
        """PUT /v1.0/settings (admin). Send the complete object; masked secrets sent back unchanged are kept.
        Null values are sent as null, and unknown server fields captured in ``extra`` are written back."""
        return self._c._model(SettingsResponse, "PUT", "/v1.0/settings", _body(settings, False))

    def restart(self) -> RestartResponse:
        """POST /v1.0/restart (admin)."""
        return self._c._model(RestartResponse, "POST", "/v1.0/restart")
