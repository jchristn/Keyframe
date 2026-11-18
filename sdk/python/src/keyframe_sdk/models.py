"""Wire models mirroring the Keyframe server DTOs.

Field names are snake_case; JSON is PascalCase. Enums are ``str`` enums, so they compare equal to
their wire strings (``JobStatus.SUCCEEDED == "Succeeded"``). Timestamps are ISO 8601 strings.
"""

from __future__ import annotations

import base64 as _base64
import os as _os
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, Iterable, List, Optional, Union

from ._base import Model


def _extra() -> Any:
    return field(default_factory=dict, repr=False, compare=False)


# Enums


class MediaKind(str, Enum):
    UNKNOWN = "Unknown"
    IMAGE = "Image"
    VIDEO = "Video"
    AUDIO = "Audio"


class JobStatus(str, Enum):
    QUEUED = "Queued"
    RUNNING = "Running"
    SUCCEEDED = "Succeeded"
    FAILED = "Failed"
    CANCELED = "Canceled"
    INTERRUPTED = "Interrupted"

    @property
    def is_terminal(self) -> bool:
        return self in (JobStatus.SUCCEEDED, JobStatus.FAILED, JobStatus.CANCELED, JobStatus.INTERRUPTED)


class FieldType(str, Enum):
    BOOLEAN = "Boolean"
    STRING = "String"
    INTEGER = "Integer"
    NUMBER = "Number"
    ENUM = "Enum"
    STRING_LIST = "StringList"
    OBJECT = "Object"


class ExtractMode(str, Enum):
    COMBINED = "Combined"
    PER_FIELD = "PerField"


class SummaryStyle(str, Enum):
    PARAGRAPH = "Paragraph"
    BULLETS = "Bullets"
    CAPTION = "Caption"


class ModelCallPurpose(str, Enum):
    SUMMARIZE = "Summarize"
    EXTRACT = "Extract"
    SCENE_SUMMARIZE = "SceneSummarize"
    SCENE_EXTRACT = "SceneExtract"
    SECTION_SUMMARIZE = "SectionSummarize"
    SECTION_EXTRACT = "SectionExtract"
    TRANSCRIBE = "Transcribe"


class ListeningMode(str, Enum):
    """What the whole-recording model calls heard. SCENES appears only on video. NONE whenever nothing listened."""

    FULL = "Full"
    SECTIONS = "Sections"
    EXCERPTS = "Excerpts"
    SCENES = "Scenes"
    NONE = "None"


class ListenSkipReason(str, Enum):
    """Stable reason code for a request that did not listen to its audio."""

    NOT_REQUESTED = "NotRequested"
    NO_MODEL_FUNCTIONS = "NoModelFunctions"
    NO_ENDPOINT_CONFIGURED = "NoEndpointConfigured"
    ENDPOINT_LACKS_AUDIO_INPUT = "EndpointLacksAudioInput"
    ENDPOINT_UNHEALTHY = "EndpointUnhealthy"
    NO_AUDIO_TRACK = "NoAudioTrack"


class ListenFormat(str, Enum):
    """Container and codec of listening audio. OpenAI-compatible endpoints accept only MP3 and WAV."""

    MP3 = "Mp3"
    WAV = "Wav"
    FLAC = "Flac"


class ListenClipScope(str, Enum):
    """Which model calls a rendered listening clip was sent to."""

    RECORDING = "Recording"
    SECTION = "Section"
    SCENE = "Scene"


class EndpointKind(str, Enum):
    COMPLETION = "Completion"
    TRANSCRIPTION = "Transcription"


class ProviderType(str, Enum):
    MOCK = "Mock"
    OLLAMA = "Ollama"
    OLLAMA_CLOUD = "OllamaCloud"
    OPEN_AI = "OpenAi"
    OPEN_AI_COMPATIBLE = "OpenAiCompatible"
    AZURE_OPEN_AI = "AzureOpenAi"
    ANTHROPIC = "Anthropic"
    GEMINI = "Gemini"
    VERTEX_AI = "VertexAi"
    BEDROCK = "Bedrock"
    X_AI = "XAi"
    COHERE = "Cohere"


class SegmentOutput(str, Enum):
    VIDEO = "Video"
    AUDIO = "Audio"
    FRAME = "Frame"


class SegmentResponseFormat(str, Enum):
    BINARY = "Binary"
    JSON = "Json"


class DatabaseType(str, Enum):
    SQLITE = "Sqlite"
    MYSQL = "Mysql"
    POSTGRESQL = "Postgresql"
    SQL_SERVER = "SqlServer"


class ApiErrorCode(str, Enum):
    BAD_REQUEST = "BadRequest"
    REQUEST_BODY_MISSING = "RequestBodyMissing"
    DESERIALIZATION_ERROR = "DeserializationError"
    AUTHENTICATION_FAILED = "AuthenticationFailed"
    AUTHORIZATION_FAILED = "AuthorizationFailed"
    NOT_FOUND = "NotFound"
    CONFLICT = "Conflict"
    UNPROCESSABLE_MEDIA = "UnprocessableMedia"
    TOO_MANY_REQUESTS = "TooManyRequests"
    BAD_GATEWAY = "BadGateway"
    TIMEOUT = "Timeout"
    INTERNAL_ERROR = "InternalError"


TERMINAL_STATUSES = frozenset({"Succeeded", "Failed", "Canceled", "Interrupted"})


def is_terminal_status(status: Union[str, JobStatus, None]) -> bool:
    """Whether a job status is terminal."""
    if status is None:
        return False
    return (status.value if isinstance(status, JobStatus) else str(status)) in TERMINAL_STATUSES


# Requests


@dataclass
class MediaSource(Model):
    url: Optional[str] = None
    data: Optional[str] = None
    media_type: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class ContextItem(Model):
    text: str = ""
    label: Optional[str] = None
    extra: Dict[str, Any] = _extra()


ContextInput = Union[str, ContextItem, Dict[str, Any], Iterable[Union[str, ContextItem, Dict[str, Any]]], None]


def normalize_context(value: ContextInput) -> List[ContextItem]:
    """Accept a string, a ContextItem, a dict, or a list of any of those, and return ContextItem objects."""
    if value is None:
        return []
    if isinstance(value, (str, ContextItem, dict)):
        value = [value]
    items: List[ContextItem] = []
    for entry in value:
        if entry is None:
            continue
        if isinstance(entry, ContextItem):
            items.append(entry)
        elif isinstance(entry, str):
            items.append(ContextItem(text=entry))
        elif isinstance(entry, dict):
            items.append(ContextItem.from_dict(entry))
        else:
            raise TypeError("Context entries must be strings, ContextItem objects, or dicts with Label and Text.")
    return items


@dataclass
class ModelSelection(Model):
    endpoint: Optional[str] = None
    name: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class SummarizeSpec(Model):
    enable: bool = False
    key: str = "Summary"
    max_words: int = 100
    style: SummaryStyle = SummaryStyle.PARAGRAPH
    instructions: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class FieldSpec(Model):
    key: str = ""
    type: FieldType = FieldType.STRING
    prompt: str = ""
    required: bool = False
    default: Any = None
    options: Optional[List[str]] = None
    minimum: Optional[float] = None
    maximum: Optional[float] = None
    max_length: Optional[int] = None
    max_items: Optional[int] = None
    schema: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class ExtractSpec(Model):
    enable: bool = False
    mode: ExtractMode = ExtractMode.COMBINED
    fields: List[FieldSpec] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class FunctionsSpec(Model):
    summarize: SummarizeSpec = field(default_factory=SummarizeSpec)
    extract: ExtractSpec = field(default_factory=ExtractSpec)
    extra: Dict[str, Any] = _extra()


@dataclass
class SceneDetectionSpec(Model):
    threshold: float = 0.30
    min_scene_ms: int = 2000
    max_scene_ms: int = 60000
    max_scenes: int = 200
    extra: Dict[str, Any] = _extra()


@dataclass
class AudioRenditionSpec(Model):
    """Quality of the audio sent to a listening model. Each None member uses its Media.Listen* server default; in a
    response (ListeningInfo.rendition) every member is filled with the effective value.

    ``format`` defaults to Media.ListenFormat (MP3). ``sample_rate`` in Hz, 8000 to 48000 (default 16000).
    ``channels`` 1 to 2 (default 1). ``bitrate_kbps`` 16 to 320, MP3 only (default 64).
    """

    format: Optional[ListenFormat] = None
    sample_rate: Optional[int] = None
    channels: Optional[int] = None
    bitrate_kbps: Optional[int] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class SceneAudioSpec(Model):
    """Attach each scene's audio to that scene's model calls when the vision endpoint can take audio. Default on.
    When it cannot, scenes run on frames and transcript and VideoAnalysisResponse.listening says why; never a 400."""

    enable: bool = True
    rendition: AudioRenditionSpec = field(default_factory=AudioRenditionSpec)
    extra: Dict[str, Any] = _extra()


@dataclass
class ScenesSpec(Model):
    enable: bool = False
    detection: SceneDetectionSpec = field(default_factory=SceneDetectionSpec)
    frames_per_scene: int = 3
    summarize: SummarizeSpec = field(default_factory=SummarizeSpec)
    extract: ExtractSpec = field(default_factory=ExtractSpec)
    audio: SceneAudioSpec = field(default_factory=SceneAudioSpec)
    extra: Dict[str, Any] = _extra()


@dataclass
class ListenSpec(Model):
    """Listening options for audio analysis.

    ``enable`` (default True) sends the audio itself to the listening model for Summarize, Extract, and per-section
    functions, falling back to the transcript when no audio-capable endpoint is usable. ``model`` None uses
    Inference.DefaultListening; naming an endpoint without audio input is a 400. ``sample_clips`` 1 to 32 (default 4)
    and ``clip_seconds`` 5 to 300 (default 30) shape the excerpts sent when the recording is too long to send whole.
    """

    enable: bool = True
    model: Optional[ModelSelection] = None
    rendition: AudioRenditionSpec = field(default_factory=AudioRenditionSpec)
    sample_clips: int = 4
    clip_seconds: int = 30
    extra: Dict[str, Any] = _extra()


@dataclass
class MeasureSpec(Model):
    """Deterministic loudness, level, silence, and stereo measurements. No model calls. Default on."""

    enable: bool = True
    extra: Dict[str, Any] = _extra()


@dataclass
class SectionDetectionSpec(Model):
    """Where silences split a recording. ``silence_db`` -90 to -10 (default -40). ``min_silence_ms`` 100 to 30000
    (default 1500). ``min_section_ms`` 0 to 600000 (default 20000). ``max_section_ms`` 10000 to 3600000 (default 600000).
    ``max_sections`` 1 to 1000 (default 100)."""

    silence_db: float = -40.0
    min_silence_ms: int = 1500
    min_section_ms: int = 20000
    max_section_ms: int = 600000
    max_sections: int = 100
    extra: Dict[str, Any] = _extra()


@dataclass
class SectionsSpec(Model):
    """Split the recording at silences and run Summarize and Extract on each section. Default off."""

    enable: bool = False
    detection: SectionDetectionSpec = field(default_factory=SectionDetectionSpec)
    summarize: SummarizeSpec = field(default_factory=SummarizeSpec)
    extract: ExtractSpec = field(default_factory=ExtractSpec)
    extra: Dict[str, Any] = _extra()


@dataclass
class TranscribeSpec(Model):
    enable: bool = False
    endpoint: Optional[str] = None
    model: Optional[str] = None
    language: Optional[str] = None
    prompt: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class VideoFunctionsSpec(FunctionsSpec):
    scenes: ScenesSpec = field(default_factory=ScenesSpec)
    transcribe: TranscribeSpec = field(default_factory=TranscribeSpec)


@dataclass
class ImageAnalysisRequest(Model):
    image: Optional[MediaSource] = None
    context: Any = field(default_factory=list)
    model: Optional[ModelSelection] = None
    functions: FunctionsSpec = field(default_factory=FunctionsSpec)
    include_properties: bool = True
    extra: Dict[str, Any] = _extra()

    def __post_init__(self) -> None:
        self.context = normalize_context(self.context)


@dataclass
class VideoAnalysisRequest(Model):
    video: Optional[MediaSource] = None
    context: Any = field(default_factory=list)
    model: Optional[ModelSelection] = None
    text_model: Optional[ModelSelection] = None
    wait: bool = False
    wait_timeout_ms: int = 120000
    sample_frames: int = 8
    include_keyframe_data: bool = False
    include_properties: bool = True
    functions: VideoFunctionsSpec = field(default_factory=VideoFunctionsSpec)
    extra: Dict[str, Any] = _extra()

    def __post_init__(self) -> None:
        self.context = normalize_context(self.context)


@dataclass
class AudioFunctionsSpec(FunctionsSpec):
    """Audio functions. Transcribe, Summarize, and Measure default on; Extract and Sections off. Summarize, Extract,
    and section functions need Transcribe or Listen enabled."""

    summarize: SummarizeSpec = field(default_factory=lambda: SummarizeSpec(enable=True))
    transcribe: TranscribeSpec = field(default_factory=lambda: TranscribeSpec(enable=True))
    measure: MeasureSpec = field(default_factory=MeasureSpec)
    sections: SectionsSpec = field(default_factory=SectionsSpec)


@dataclass
class AudioAnalysisRequest(Model):
    audio: Optional[MediaSource] = None
    context: Any = field(default_factory=list)
    wait: bool = False
    wait_timeout_ms: int = 120000
    transcription_model: Optional[ModelSelection] = None
    listen: ListenSpec = field(default_factory=ListenSpec)
    text_model: Optional[ModelSelection] = None
    include_properties: bool = True
    functions: AudioFunctionsSpec = field(default_factory=AudioFunctionsSpec)
    extra: Dict[str, Any] = _extra()

    def __post_init__(self) -> None:
        self.context = normalize_context(self.context)


@dataclass
class SegmentRequest(Model):
    """Body of POST /v1.0/segment. ``end_ms`` is required except for Frame output."""

    source: Optional[MediaSource] = None
    start_ms: int = 0
    end_ms: Optional[int] = None
    output: Union[SegmentOutput, str] = SegmentOutput.VIDEO
    format: Optional[str] = None
    max_edge: Optional[int] = None
    response_format: Union[SegmentResponseFormat, str] = SegmentResponseFormat.BINARY
    extra: Dict[str, Any] = _extra()


@dataclass
class ProbeRequest(Model):
    media: Optional[MediaSource] = None
    extra: Dict[str, Any] = _extra()


# Responses


@dataclass
class ApiErrorResponse(Model):
    error: Union[ApiErrorCode, str] = ApiErrorCode.INTERNAL_ERROR
    status_code: int = 500
    message: str = ""
    details: List[str] = field(default_factory=list)
    request_id: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class MediaProperties(Model):
    media_kind: MediaKind = MediaKind.UNKNOWN
    content_type: Optional[str] = None
    format: Optional[str] = None
    size_bytes: int = 0
    sha256: Optional[str] = None
    width: Optional[int] = None
    height: Optional[int] = None
    duration_ms: Optional[int] = None
    frame_rate: Optional[float] = None
    bit_rate: Optional[int] = None
    video_codec: Optional[str] = None
    has_audio: bool = False
    audio_codec: Optional[str] = None
    audio_channels: Optional[int] = None
    audio_sample_rate: Optional[int] = None
    tags: Dict[str, str] = field(default_factory=dict)
    extra: Dict[str, Any] = _extra()


@dataclass
class FieldResult(Model):
    key: str = ""
    type: FieldType = FieldType.STRING
    value: Any = None
    valid: bool = False
    raw_value: Optional[str] = None
    error: Optional[str] = None
    warnings: List[str] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class StageTiming(Model):
    name: str = ""
    start_utc: Optional[str] = None
    duration_ms: float = 0.0
    success: bool = True
    extra: Dict[str, Any] = _extra()


@dataclass
class ModelCallAttempt(Model):
    attempt: int = 1
    start_utc: Optional[str] = None
    success: bool = False
    error: Optional[str] = None
    status_code: Optional[int] = None
    duration_ms: float = 0.0
    queue_ms: float = 0.0
    time_to_first_token_ms: Optional[float] = None
    generation_ms: Optional[float] = None
    tokens_per_second: Optional[float] = None
    prompt_tokens: Optional[int] = None
    completion_tokens: Optional[int] = None
    total_tokens: Optional[int] = None
    cached_tokens: Optional[int] = None
    reasoning_tokens: Optional[int] = None
    finish_reason: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class ModelCallTelemetry(Model):
    purpose: ModelCallPurpose = ModelCallPurpose.SUMMARIZE
    target: Optional[str] = None
    endpoint: str = ""
    provider: ProviderType = ProviderType.MOCK
    model: Optional[str] = None
    start_utc: Optional[str] = None
    duration_ms: float = 0.0
    time_to_first_token_ms: Optional[float] = None
    generation_ms: Optional[float] = None
    tokens_per_second: Optional[float] = None
    queue_ms: float = 0.0
    attempts: int = 1
    repair_attempts: int = 0
    success: bool = False
    error: Optional[str] = None
    status_code: Optional[int] = None
    input_images: int = 0
    input_image_bytes: int = 0
    input_audio_bytes: int = 0
    prompt_tokens: Optional[int] = None
    completion_tokens: Optional[int] = None
    total_tokens: Optional[int] = None
    cached_tokens: Optional[int] = None
    reasoning_tokens: Optional[int] = None
    finish_reason: Optional[str] = None
    attempt_details: List[ModelCallAttempt] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class TokenTotals(Model):
    model_calls: int = 0
    failed_model_calls: int = 0
    model_ms: float = 0.0
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0
    cached_tokens: int = 0
    reasoning_tokens: int = 0
    input_images: int = 0
    input_image_bytes: int = 0
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalysisTelemetry(Model):
    start_utc: Optional[str] = None
    end_utc: Optional[str] = None
    total_ms: float = 0.0
    queued_ms: Optional[float] = None
    queue_wait_ms: float = 0.0
    stages: List[StageTiming] = field(default_factory=list)
    model_calls: List[ModelCallTelemetry] = field(default_factory=list)
    totals: TokenTotals = field(default_factory=TokenTotals)
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalysisResponse(Model):
    id: str = ""
    request_id: Optional[str] = None
    success: bool = False
    media_kind: MediaKind = MediaKind.UNKNOWN
    properties: Optional[MediaProperties] = None
    results: Dict[str, Any] = field(default_factory=dict)
    fields: List[FieldResult] = field(default_factory=list)
    telemetry: AnalysisTelemetry = field(default_factory=AnalysisTelemetry)
    warnings: List[str] = field(default_factory=list)
    errors: List[str] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()

    def field_result(self, key: str) -> Optional[FieldResult]:
        """The FieldResult for a key, or None."""
        return next((f for f in self.fields if f.key == key), None)


@dataclass
class ImageAnalysisResponse(AnalysisResponse):
    pass


@dataclass
class KeyframeInfo(Model):
    timestamp_ms: int = 0
    sha256: Optional[str] = None
    width: Optional[int] = None
    height: Optional[int] = None
    data: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class SceneResult(Model):
    index: int = 0
    start_ms: int = 0
    end_ms: int = 0
    duration_ms: int = 0
    keyframes: List[KeyframeInfo] = field(default_factory=list)
    transcript_text: Optional[str] = None
    results: Dict[str, Any] = field(default_factory=dict)
    fields: List[FieldResult] = field(default_factory=list)
    errors: List[str] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class TranscriptSegmentResult(Model):
    start_ms: int = 0
    end_ms: int = 0
    text: str = ""
    extra: Dict[str, Any] = _extra()


@dataclass
class TranscriptResult(Model):
    language: Optional[str] = None
    text: str = ""
    segments: List[TranscriptSegmentResult] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class ListenClip(Model):
    """One audio clip rendered and sent to a listening model. ``index`` is the section or scene index, None for
    Recording clips. ``size_bytes`` is before base64 encoding."""

    scope: ListenClipScope = ListenClipScope.RECORDING
    index: Optional[int] = None
    start_ms: int = 0
    end_ms: int = 0
    size_bytes: int = 0
    extra: Dict[str, Any] = _extra()


@dataclass
class ListeningInfo(Model):
    """Whether, how, and by which model the audio was heard. ``listened`` is True when at least one model call carried
    audio, including a call that then failed. ``reason`` is None when listening happened. ``heard_ms`` counts
    overlapping clips once."""

    listened: bool = False
    mode: ListeningMode = ListeningMode.NONE
    reason: Optional[ListenSkipReason] = None
    endpoint: Optional[str] = None
    model: Optional[str] = None
    rendition: AudioRenditionSpec = field(default_factory=AudioRenditionSpec)
    duration_ms: int = 0
    heard_ms: int = 0
    clips: List[ListenClip] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class AudioMeasurements(Model):
    """Deterministic measurements. Levels are dB relative to full scale; a member is None when ffmpeg could not
    measure it. ``stereo_width_db`` is None for sources that are not stereo. ``clipping_likely`` and
    ``effectively_mono`` use the Media.Clipping* and Media.MonoMaxSideDb thresholds."""

    integrated_lufs: Optional[float] = None
    loudness_range_lu: Optional[float] = None
    true_peak_dbtp: Optional[float] = None
    peak_dbfs: Optional[float] = None
    rms_dbfs: Optional[float] = None
    crest_factor_db: Optional[float] = None
    peak_count: Optional[int] = None
    clipping_likely: bool = False
    silence_ratio: float = 0.0
    silence_count: int = 0
    leading_silence_ms: int = 0
    trailing_silence_ms: int = 0
    channels: int = 0
    stereo_width_db: Optional[float] = None
    effectively_mono: bool = False
    extra: Dict[str, Any] = _extra()


@dataclass
class SectionResult(Model):
    """Result for one audio section. Each section can be cut back out with ``segments.get``."""

    index: int = 0
    start_ms: int = 0
    end_ms: int = 0
    duration_ms: int = 0
    transcript_text: Optional[str] = None
    results: Dict[str, Any] = field(default_factory=dict)
    fields: List[FieldResult] = field(default_factory=list)
    errors: List[str] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class VideoAnalysisResponse(AnalysisResponse):
    transcript: Optional[TranscriptResult] = None
    scenes: List[SceneResult] = field(default_factory=list)
    listening: Optional[ListeningInfo] = None


@dataclass
class AudioAnalysisResponse(AnalysisResponse):
    transcript: Optional[TranscriptResult] = None
    listening: ListeningInfo = field(default_factory=ListeningInfo)
    measurements: Optional[AudioMeasurements] = None
    sections: Optional[List[SectionResult]] = None


@dataclass
class JobProgress(Model):
    stage: Optional[str] = None
    completed: int = 0
    total: int = 0
    percent: float = 0.0
    extra: Dict[str, Any] = _extra()


@dataclass
class JobResponse(Model):
    id: str = ""
    status: Union[JobStatus, str] = JobStatus.QUEUED
    media_kind: MediaKind = MediaKind.VIDEO
    principal_name: Optional[str] = None
    source_url: Optional[str] = None
    size_bytes: int = 0
    duration_ms: int = 0
    progress: JobProgress = field(default_factory=JobProgress)
    error: Optional[str] = None
    created_utc: Optional[str] = None
    started_utc: Optional[str] = None
    completed_utc: Optional[str] = None
    result_url: Optional[str] = None
    extra: Dict[str, Any] = _extra()

    @property
    def is_terminal(self) -> bool:
        return is_terminal_status(self.status)


@dataclass
class VideoSubmitResult:
    """Outcome of POST /v1.0/video. Exactly one of ``job`` or ``result`` is set."""

    status_code: int
    job_id: str
    job: Optional[JobResponse] = None
    result: Optional[VideoAnalysisResponse] = None


@dataclass
class AudioSubmitResult:
    """Outcome of POST /v1.0/audio. Exactly one of ``job`` or ``result`` is set."""

    status_code: int
    job_id: str
    job: Optional[JobResponse] = None
    result: Optional[AudioAnalysisResponse] = None


@dataclass
class SegmentResponse(Model):
    """JSON form of POST /v1.0/segment (ResponseFormat Json). ``data`` is base64."""

    media_type: str = ""
    filename: str = ""
    start_ms: int = 0
    end_ms: int = 0
    duration_ms: int = 0
    size_bytes: int = 0
    data: str = ""
    extra: Dict[str, Any] = _extra()

    def decode_data(self) -> bytes:
        """The segment media decoded from base64."""
        return _base64.b64decode(self.data) if self.data else b""


@dataclass
class SegmentMedia:
    """Binary form of POST /v1.0/segment. Metadata comes from the response headers."""

    data: bytes
    media_type: Optional[str] = None
    filename: Optional[str] = None
    start_ms: Optional[int] = None
    end_ms: Optional[int] = None
    duration_ms: Optional[int] = None
    size_bytes: int = 0
    request_id: Optional[str] = None

    def save(self, path: Union[str, "_os.PathLike[str]"]) -> None:
        """Write the media bytes to a file."""
        with open(path, "wb") as handle:
            handle.write(self.data)


@dataclass
class HealthResponse(Model):
    status: str = "Healthy"
    product: str = "Keyframe"
    version: str = ""
    started_utc: Optional[str] = None
    uptime_seconds: int = 0
    ffmpeg_available: bool = False
    ffmpeg_version: Optional[str] = None
    database: str = ""
    active_jobs: int = 0
    queued_jobs: int = 0
    endpoints: Dict[str, bool] = field(default_factory=dict)
    extra: Dict[str, Any] = _extra()


@dataclass
class WhoAmIResponse(Model):
    is_authenticated: bool = False
    is_admin: bool = False
    principal_name: Optional[str] = None
    authentication_required: bool = True
    version: str = ""
    extra: Dict[str, Any] = _extra()


@dataclass
class EndpointResponse(Model):
    name: str = ""
    provider: ProviderType = ProviderType.MOCK
    kind: EndpointKind = EndpointKind.COMPLETION
    endpoint: Optional[str] = None
    api_key_set: bool = False
    api_key_last4: Optional[str] = None
    model: Optional[str] = None
    max_concurrent_requests: int = 0
    timeout_ms: int = 0
    active: bool = True
    is_default: bool = False
    provider_available: bool = False
    supports_images: bool = False
    supports_structured_output: bool = False
    supports_transcription: bool = False
    supports_audio_input: bool = False
    healthy: bool = False
    health_message: Optional[str] = None
    last_checked_utc: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class EndpointTestResponse(Model):
    name: str = ""
    success: bool = False
    duration_ms: float = 0.0
    model: Optional[str] = None
    error: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class RequestHistoryEntry(Model):
    id: str = ""
    request_id: Optional[str] = None
    principal_name: Optional[str] = None
    method: str = ""
    path: str = ""
    url: str = ""
    status_code: int = 0
    success: bool = False
    duration_ms: float = 0.0
    source_ip: Optional[str] = None
    request_content_type: Optional[str] = None
    response_content_type: Optional[str] = None
    request_headers: Optional[Dict[str, str]] = None
    request_body: Optional[str] = None
    request_body_bytes: int = 0
    request_body_truncated: bool = False
    response_headers: Optional[Dict[str, str]] = None
    response_body: Optional[str] = None
    response_body_bytes: int = 0
    response_body_truncated: bool = False
    created_utc: Optional[str] = None
    completed_utc: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class RequestHistorySummaryBucket(Model):
    bucket_start_utc: Optional[str] = None
    bucket_end_utc: Optional[str] = None
    success_count: int = 0
    failure_count: int = 0
    average_duration_ms: float = 0.0
    extra: Dict[str, Any] = _extra()


@dataclass
class RequestHistorySummary(Model):
    total_count: int = 0
    total_success: int = 0
    total_failure: int = 0
    average_duration_ms: float = 0.0
    bucket_minutes: int = 60
    buckets: List[RequestHistorySummaryBucket] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class EnumerationResult(Model):
    """A page of items. ``items`` holds model instances of the type the call returns."""

    items: List[Any] = field(default_factory=list)
    page_number: int = 1
    page_size: int = 25
    total_count: int = 0
    total_pages: int = 0
    extra: Dict[str, Any] = _extra()


@dataclass
class DeleteResult(Model):
    deleted_count: int = 0
    extra: Dict[str, Any] = _extra()


# Analytics


@dataclass
class Distribution(Model):
    count: int = 0
    min: float = 0.0
    max: float = 0.0
    avg: float = 0.0
    p50: float = 0.0
    p90: float = 0.0
    p95: float = 0.0
    p99: float = 0.0
    std_dev: float = 0.0
    sum: float = 0.0
    extra: Dict[str, Any] = _extra()


@dataclass
class StatBlock(Model):
    count: int = 0
    succeeded: int = 0
    failed: int = 0
    success_rate: float = 0.0
    analysis_ms: Optional[Distribution] = None
    queue_wait_ms: Optional[Distribution] = None
    media_duration_ms: Optional[Distribution] = None
    realtime_factor: Optional[Distribution] = None
    media_bytes: Optional[Distribution] = None
    model_calls: int = 0
    failed_model_calls: int = 0
    retries: int = 0
    repair_attempts: int = 0
    model_call_ms: Optional[Distribution] = None
    time_to_first_token_ms: Optional[Distribution] = None
    generation_ms: Optional[Distribution] = None
    tokens_per_second: Optional[Distribution] = None
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0
    cached_tokens: int = 0
    reasoning_tokens: int = 0
    tokens_per_analysis: Optional[Distribution] = None
    input_images: int = 0
    input_image_bytes: int = 0
    input_audio_bytes: int = 0
    scenes: int = 0
    transcript_characters: int = 0
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalyticsBucket(Model):
    start_utc: Optional[str] = None
    stats: StatBlock = field(default_factory=StatBlock)
    extra: Dict[str, Any] = _extra()


@dataclass
class ModelStats(Model):
    endpoint: str = ""
    provider: Union[ProviderType, str] = ProviderType.MOCK
    model: Optional[str] = None
    calls: int = 0
    failed: int = 0
    duration_ms: Optional[Distribution] = None
    time_to_first_token_ms: Optional[Distribution] = None
    generation_ms: Optional[Distribution] = None
    tokens_per_second: Optional[Distribution] = None
    prompt_tokens: int = 0
    completion_tokens: int = 0
    extra: Dict[str, Any] = _extra()


@dataclass
class StageStats(Model):
    stage: str = ""
    duration_ms: Optional[Distribution] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class PurposeStats(Model):
    purpose: Union[ModelCallPurpose, str] = ModelCallPurpose.SUMMARIZE
    calls: int = 0
    failed: int = 0
    duration_ms: Optional[Distribution] = None
    time_to_first_token_ms: Optional[Distribution] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalyticsSummary(Model):
    from_utc: Optional[str] = None
    to_utc: Optional[str] = None
    bucket: str = "hour"
    media_kind: Optional[Union[MediaKind, str]] = None
    endpoint: Optional[str] = None
    model: Optional[str] = None
    totals: StatBlock = field(default_factory=StatBlock)
    by_media_kind: Dict[str, StatBlock] = field(default_factory=dict)
    buckets: List[AnalyticsBucket] = field(default_factory=list)
    by_model: List[ModelStats] = field(default_factory=list)
    by_stage: List[StageStats] = field(default_factory=list)
    by_purpose: List[PurposeStats] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class ModelCallRecord(Model):
    id: str = ""
    analysis_id: str = ""
    purpose: Union[ModelCallPurpose, str] = ModelCallPurpose.SUMMARIZE
    target: Optional[str] = None
    endpoint: str = ""
    provider: Union[ProviderType, str] = ProviderType.MOCK
    model: Optional[str] = None
    attempt: int = 1
    start_utc: Optional[str] = None
    success: bool = False
    error: Optional[str] = None
    status_code: Optional[int] = None
    duration_ms: float = 0.0
    queue_ms: float = 0.0
    time_to_first_token_ms: Optional[float] = None
    generation_ms: Optional[float] = None
    tokens_per_second: Optional[float] = None
    prompt_tokens: Optional[int] = None
    completion_tokens: Optional[int] = None
    total_tokens: Optional[int] = None
    cached_tokens: Optional[int] = None
    reasoning_tokens: Optional[int] = None
    input_images: int = 0
    input_image_bytes: int = 0
    input_audio_bytes: int = 0
    finish_reason: Optional[str] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalysisRecord(Model):
    """A stored analysis. ``calls`` is filled by ``analytics.get_analysis`` and None in listings."""

    id: str = ""
    request_id: Optional[str] = None
    job_id: Optional[str] = None
    media_kind: MediaKind = MediaKind.UNKNOWN
    principal_name: Optional[str] = None
    success: bool = False
    error: Optional[str] = None
    started_utc: Optional[str] = None
    completed_utc: Optional[str] = None
    analysis_ms: float = 0.0
    queue_wait_ms: float = 0.0
    media_duration_ms: Optional[int] = None
    media_bytes: int = 0
    realtime_factor: Optional[float] = None
    vision_endpoint: Optional[str] = None
    vision_model: Optional[str] = None
    text_endpoint: Optional[str] = None
    text_model: Optional[str] = None
    transcription_endpoint: Optional[str] = None
    transcription_model: Optional[str] = None
    model_calls: int = 0
    failed_model_calls: int = 0
    retries: int = 0
    repair_attempts: int = 0
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0
    cached_tokens: int = 0
    reasoning_tokens: int = 0
    input_images: int = 0
    input_image_bytes: int = 0
    input_audio_bytes: int = 0
    scenes: int = 0
    transcript_characters: int = 0
    field_count: int = 0
    invalid_fields: int = 0
    stages: List[StageTiming] = field(default_factory=list)
    calls: Optional[List[ModelCallRecord]] = None
    extra: Dict[str, Any] = _extra()


@dataclass
class RestartResponse(Model):
    accepted: bool = True
    message: str = ""
    extra: Dict[str, Any] = _extra()


# Settings


@dataclass
class RestSettings(Model):
    hostname: str = "127.0.0.1"
    port: int = 8700
    ssl: bool = False
    max_request_body_bytes: int = 268435456
    extra: Dict[str, Any] = _extra()


@dataclass
class AccessKeySettings(Model):
    name: str = "default"
    key: str = "default"
    active: bool = True
    extra: Dict[str, Any] = _extra()


@dataclass
class AuthSettings(Model):
    require_authentication: bool = True
    admin_api_key: str = "keyframeadmin"
    access_keys: List[AccessKeySettings] = field(default_factory=list)
    max_failed_attempts_per_minute: int = 30
    extra: Dict[str, Any] = _extra()


@dataclass
class DatabaseSettings(Model):
    type: Union[DatabaseType, str] = DatabaseType.SQLITE
    filename: str = "data/keyframe.db"
    hostname: Optional[str] = None
    port: int = 0
    database_name: Optional[str] = None
    username: Optional[str] = None
    password: Optional[str] = None
    trust_server_certificate: bool = True
    command_timeout_seconds: int = 30
    extra: Dict[str, Any] = _extra()


@dataclass
class EndpointSettings(Model):
    name: str = "mock"
    provider: Union[ProviderType, str] = ProviderType.MOCK
    kind: Union[EndpointKind, str] = EndpointKind.COMPLETION
    endpoint: Optional[str] = None
    api_key: Optional[str] = None
    model: Optional[str] = None
    max_concurrent_requests: int = 2
    timeout_ms: int = 120000
    temperature: Optional[float] = None
    max_output_tokens: int = 2048
    max_inline_media_bytes: int = 20971520
    max_listen_seconds: Optional[int] = None
    active: bool = True
    extra: Dict[str, Any] = _extra()


@dataclass
class InferenceSettings(Model):
    default_vision: Optional[str] = "ollama-gemma3-4b"
    default_text: Optional[str] = "ollama-gpt-oss-20b"
    default_transcription: Optional[str] = "whisper"
    default_listening: Optional[str] = None
    health_probe_interval_ms: int = 300000
    unhealthy_probe_interval_ms: int = 15000
    max_retries: int = 2
    max_repair_attempts: int = 1
    retry_backoff_ms: int = 500
    request_timeout_ms: int = 300000
    max_transcript_characters: int = 60000
    endpoints: List[EndpointSettings] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class MediaSettings(Model):
    temp_directory: str = "temp"
    ffmpeg_path: str = "ffmpeg"
    ffprobe_path: str = "ffprobe"
    max_download_bytes: int = 2147483648
    download_timeout_ms: int = 300000
    allow_private_network_urls: bool = False
    image_max_edge: int = 1568
    thumbnail_max_edge: int = 320
    jpeg_quality: int = 3
    max_parallel_ffmpeg: int = 4
    ffmpeg_timeout_ms: int = 600000
    max_video_duration_ms: int = 14400000
    max_segment_ms: int = 600000
    max_audio_duration_ms: int = 14400000
    transcription_chunk_seconds: int = 600
    listen_format: Union[ListenFormat, str] = ListenFormat.MP3
    listen_sample_rate: int = 16000
    listen_channels: int = 1
    listen_bitrate_kbps: int = 64
    max_listen_seconds: int = 1200
    measure_silence_db: float = -50.0
    measure_min_silence_ms: int = 500
    clipping_true_peak_dbtp: float = -0.1
    clipping_min_peaks_per_minute: float = 10.0
    mono_max_side_db: float = -40.0
    extra: Dict[str, Any] = _extra()


@dataclass
class JobSettings(Model):
    max_concurrent_jobs: int = 2
    max_queued_jobs: int = 100
    retention_days: int = 7
    max_wait_timeout_ms: int = 600000
    extra: Dict[str, Any] = _extra()


@dataclass
class RequestHistorySettings(Model):
    enable: bool = True
    max_request_body_bytes: int = 65536
    max_response_body_bytes: int = 65536
    retention_days: int = 30
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalyticsSettings(Model):
    enable: bool = True
    retention_days: int = 90
    extra: Dict[str, Any] = _extra()


@dataclass
class SyslogServerSettings(Model):
    hostname: str = "127.0.0.1"
    port: int = 514
    extra: Dict[str, Any] = _extra()


@dataclass
class LoggingSettings(Model):
    console_logging: bool = True
    enable_colors: bool = True
    log_directory: Optional[str] = "logs/"
    log_filename: Optional[str] = "keyframe.log"
    minimum_severity: int = 0
    servers: List[SyslogServerSettings] = field(default_factory=list)
    extra: Dict[str, Any] = _extra()


@dataclass
class TelemetrySettings(Model):
    enable: bool = True
    service_name: str = "keyframe-server"
    otlp_enable: bool = False
    otlp_endpoint: str = "http://127.0.0.1:4717"
    otlp_protocol: str = "grpc"
    prometheus_enable: bool = True
    prometheus_hostname: str = "127.0.0.1"
    prometheus_port: int = 9464
    prometheus_path: str = "/metrics"
    loki_enable: bool = False
    loki_endpoint: str = "http://127.0.0.1:3100"
    export_logs: bool = False
    metric_export_interval_ms: int = 15000
    trace_sampling_ratio: float = 1.0
    extra: Dict[str, Any] = _extra()


@dataclass
class KeyframeSettings(Model):
    created_utc: Optional[str] = None
    rest: RestSettings = field(default_factory=RestSettings)
    auth: AuthSettings = field(default_factory=AuthSettings)
    database: DatabaseSettings = field(default_factory=DatabaseSettings)
    inference: InferenceSettings = field(default_factory=InferenceSettings)
    media: MediaSettings = field(default_factory=MediaSettings)
    jobs: JobSettings = field(default_factory=JobSettings)
    request_history: RequestHistorySettings = field(default_factory=RequestHistorySettings)
    analytics: AnalyticsSettings = field(default_factory=AnalyticsSettings)
    logging: LoggingSettings = field(default_factory=LoggingSettings)
    telemetry: TelemetrySettings = field(default_factory=TelemetrySettings)
    extra: Dict[str, Any] = _extra()


@dataclass
class SettingsFieldMetadata(Model):
    path: str = ""
    apply_mode: str = "Live"
    overridden_by_environment: bool = False
    environment_variable: Optional[str] = None
    secret: bool = False
    extra: Dict[str, Any] = _extra()


@dataclass
class SettingsResponse(Model):
    settings: KeyframeSettings = field(default_factory=KeyframeSettings)
    fields: List[SettingsFieldMetadata] = field(default_factory=list)
    restart_required: bool = False
    settings_file: Optional[str] = None
    extra: Dict[str, Any] = _extra()
