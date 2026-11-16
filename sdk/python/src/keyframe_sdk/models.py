"""Wire models mirroring the Keyframe server DTOs.

Field names are snake_case; JSON is PascalCase. Enums are ``str`` enums, so they compare equal to
their wire strings (``JobStatus.SUCCEEDED == "Succeeded"``). Timestamps are ISO 8601 strings.
"""

from __future__ import annotations

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
    TRANSCRIBE = "Transcribe"


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
class ScenesSpec(Model):
    enable: bool = False
    detection: SceneDetectionSpec = field(default_factory=SceneDetectionSpec)
    frames_per_scene: int = 3
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
class ModelCallTelemetry(Model):
    purpose: ModelCallPurpose = ModelCallPurpose.SUMMARIZE
    target: Optional[str] = None
    endpoint: str = ""
    provider: ProviderType = ProviderType.MOCK
    model: Optional[str] = None
    start_utc: Optional[str] = None
    duration_ms: float = 0.0
    time_to_first_token_ms: Optional[float] = None
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
    stages: List[StageTiming] = field(default_factory=list)
    model_calls: List[ModelCallTelemetry] = field(default_factory=list)
    totals: TokenTotals = field(default_factory=TokenTotals)
    extra: Dict[str, Any] = _extra()


@dataclass
class AnalysisResponse(Model):
    id: str = ""
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
class VideoAnalysisResponse(AnalysisResponse):
    transcript: Optional[TranscriptResult] = None
    scenes: List[SceneResult] = field(default_factory=list)


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
    active: bool = True
    extra: Dict[str, Any] = _extra()


@dataclass
class InferenceSettings(Model):
    default_vision: Optional[str] = "mock"
    default_text: Optional[str] = None
    default_transcription: Optional[str] = "mock-stt"
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
    transcription_chunk_seconds: int = 600
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
    enabled: bool = True
    max_request_body_bytes: int = 65536
    max_response_body_bytes: int = 65536
    retention_days: int = 30
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
