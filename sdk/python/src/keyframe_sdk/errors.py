"""Exception types raised by the Keyframe SDK."""

from __future__ import annotations

from typing import List, Optional

from .models import ApiErrorResponse, JobResponse


class KeyframeError(Exception):
    """Base class for every SDK error."""


class KeyframeApiError(KeyframeError):
    """The server answered with a non-2xx status code."""

    def __init__(
        self,
        method: str,
        path: str,
        status_code: int,
        api_error: Optional[ApiErrorResponse],
        response_body: str,
        request_id: Optional[str],
    ) -> None:
        message = f"{method} {path} returned {status_code}"
        if api_error is not None:
            message += f" {_text(api_error.error)}: {api_error.message}"
            if api_error.details:
                message += " (" + "; ".join(api_error.details) + ")"
        else:
            message += "."
        super().__init__(message)
        self.method = method
        self.path = path
        self.status_code = status_code
        self.api_error = api_error
        self.response_body = response_body
        self.request_id = (api_error.request_id if api_error and api_error.request_id else None) or request_id

    @property
    def error(self) -> Optional[str]:
        """Error category string such as ``"NotFound"``, or None when the body was not an ApiErrorResponse."""
        return _text(self.api_error.error) if self.api_error is not None else None

    @property
    def details(self) -> List[str]:
        """Validation details, possibly empty."""
        return list(self.api_error.details) if self.api_error is not None else []


class KeyframeJobError(KeyframeError):
    """A waited-on job finished Failed, Canceled, or Interrupted."""

    def __init__(self, job: JobResponse) -> None:
        super().__init__(f"Job {job.id} finished with status {_text(job.status)}" + (f": {job.error}" if job.error else "."))
        self.job = job


class KeyframeTimeoutError(KeyframeError):
    """A request or a wait exceeded its time limit."""


class KeyframeConnectionError(KeyframeError):
    """The server could not be reached."""


def _text(value: object) -> str:
    return getattr(value, "value", None) or str(value)
