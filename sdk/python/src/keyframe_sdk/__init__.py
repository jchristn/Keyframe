"""Keyframe Python SDK: summaries and typed metadata fields from images and video."""

from .client import (
    DEFAULT_BASE_URL,
    VERSION,
    KeyframeClient,
    RawResponse,
    media_from_bytes,
    media_from_file,
    media_from_url,
)
from .errors import (
    KeyframeApiError,
    KeyframeConnectionError,
    KeyframeError,
    KeyframeJobError,
    KeyframeTimeoutError,
)
from . import models as _models
from .models import *  # noqa: F401,F403
from .models import is_terminal_status, normalize_context

__version__ = VERSION

__all__ = [
    "DEFAULT_BASE_URL",
    "VERSION",
    "KeyframeClient",
    "RawResponse",
    "media_from_bytes",
    "media_from_file",
    "media_from_url",
    "KeyframeApiError",
    "KeyframeConnectionError",
    "KeyframeError",
    "KeyframeJobError",
    "KeyframeTimeoutError",
    "is_terminal_status",
    "normalize_context",
] + [n for n in dir(_models) if not n.startswith("_") and isinstance(getattr(_models, n), type) and getattr(_models, n).__module__ == _models.__name__]
