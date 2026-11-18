// Type definitions for keyframe-sdk 0.1.0. Wire models use the server's PascalCase JSON names.

export declare const VERSION: string;
export declare const DEFAULT_BASE_URL: string;

export type MediaKind = 'Unknown' | 'Image' | 'Video' | 'Audio';
export type JobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled' | 'Interrupted';
export type FieldType = 'Boolean' | 'String' | 'Integer' | 'Number' | 'Enum' | 'StringList' | 'Object';
export type ExtractMode = 'Combined' | 'PerField';
export type SummaryStyle = 'Paragraph' | 'Bullets' | 'Caption';
export type ModelCallPurpose =
  | 'Summarize' | 'Extract' | 'SceneSummarize' | 'SceneExtract' | 'SectionSummarize' | 'SectionExtract' | 'Transcribe';
/** What the whole-recording model calls heard. Scenes appears only on video. None whenever Listened is false. */
export type ListeningMode = 'Full' | 'Sections' | 'Excerpts' | 'Scenes' | 'None';
/** Stable reason code for a request that did not listen to its audio. */
export type ListenSkipReason =
  | 'NotRequested' | 'NoModelFunctions' | 'NoEndpointConfigured' | 'EndpointLacksAudioInput' | 'EndpointUnhealthy' | 'NoAudioTrack';
/** Container and codec of listening audio. OpenAI-compatible endpoints accept only Mp3 and Wav. */
export type ListenFormat = 'Mp3' | 'Wav' | 'Flac';
/** Which model calls a rendered listening clip was sent to. */
export type ListenClipScope = 'Recording' | 'Section' | 'Scene';
export type EndpointKind = 'Completion' | 'Transcription';
export type ProviderType =
  | 'Mock' | 'Ollama' | 'OllamaCloud' | 'OpenAi' | 'OpenAiCompatible' | 'AzureOpenAi'
  | 'Anthropic' | 'Gemini' | 'VertexAi' | 'Bedrock' | 'XAi' | 'Cohere';
export type SegmentOutput = 'Video' | 'Audio' | 'Frame';
export type SegmentResponseFormat = 'Binary' | 'Json';
export type AnalyticsBucketSize = 'minute' | 'hour' | 'day';
export type DatabaseType = 'Sqlite' | 'Mysql' | 'Postgresql' | 'SqlServer';
export type ApiErrorCode =
  | 'BadRequest' | 'RequestBodyMissing' | 'DeserializationError' | 'AuthenticationFailed' | 'AuthorizationFailed'
  | 'NotFound' | 'Conflict' | 'UnprocessableMedia' | 'TooManyRequests' | 'BadGateway' | 'Timeout' | 'InternalError';

// Requests

export interface MediaSource { Url?: string | null; Data?: string | null; MediaType?: string | null; }
export interface ContextItem { Label?: string | null; Text: string; }
/** A string, an array of strings, context items, or a mix. The server always echoes items. */
export type ContextInput = string | Array<string | ContextItem>;
export interface ModelSelection { Endpoint?: string | null; Name?: string | null; }
export interface SummarizeSpec { Enable?: boolean; Key?: string; MaxWords?: number; Style?: SummaryStyle; Instructions?: string | null; }
export interface FieldSpec {
  Key: string;
  Type?: FieldType;
  Prompt: string;
  Required?: boolean;
  Default?: unknown;
  Options?: string[] | null;
  Minimum?: number | null;
  Maximum?: number | null;
  MaxLength?: number | null;
  MaxItems?: number | null;
  Schema?: string | null;
}
export interface ExtractSpec { Enable?: boolean; Mode?: ExtractMode; Fields?: FieldSpec[]; }
export interface FunctionsSpec { Summarize?: SummarizeSpec; Extract?: ExtractSpec; }
export interface SceneDetectionSpec { Threshold?: number; MinSceneMs?: number; MaxSceneMs?: number; MaxScenes?: number; }
/**
 * Quality of the audio sent to a listening model. Each null or omitted member uses its Media.Listen* server default.
 * In a response (ListeningInfo.Rendition) every member is filled with the effective value.
 */
export interface AudioRenditionSpec {
  /** Null uses Media.ListenFormat (default Mp3). */
  Format?: ListenFormat | null;
  /** Hz. Null uses Media.ListenSampleRate (default 16000). 8000 to 48000. */
  SampleRate?: number | null;
  /** Null uses Media.ListenChannels (default 1). 1 to 2. */
  Channels?: number | null;
  /** Mp3 only. Null uses Media.ListenBitrateKbps (default 64). 16 to 320. */
  BitrateKbps?: number | null;
}
/** Scene audio: attach each scene's audio to that scene's model calls, when the vision endpoint can take audio. */
export interface SceneAudioSpec {
  /** Default true. When the vision endpoint cannot take audio, scenes run on frames and transcript; never a 400. */
  Enable?: boolean;
  Rendition?: AudioRenditionSpec;
}
export interface ScenesSpec {
  Enable?: boolean; Detection?: SceneDetectionSpec; FramesPerScene?: number; Summarize?: SummarizeSpec; Extract?: ExtractSpec;
  /** Scene audio options. On by default. */
  Audio?: SceneAudioSpec;
}
/** Listening options for audio analysis: whether the model hears the audio itself, which model, and how much of it. */
export interface ListenSpec {
  /**
   * Send the audio itself to the listening model for Summarize, Extract, and per-section functions. Falls back to the
   * transcript, with Listening.Listened false, when no audio-capable endpoint is usable. Default true.
   */
  Enable?: boolean;
  /** Null uses Inference.DefaultListening. Naming an endpoint without audio input is a 400. */
  Model?: ModelSelection | null;
  Rendition?: AudioRenditionSpec;
  /** Excerpts sent when the recording is too long to send whole and sections are off. Default 4, 1 to 32. */
  SampleClips?: number;
  /** Length of each excerpt in seconds. Default 30, 5 to 300. */
  ClipSeconds?: number;
}
/** Deterministic loudness, level, silence, and stereo measurements. No model calls. */
export interface MeasureSpec { /** Default true. */ Enable?: boolean; }
export interface SectionDetectionSpec {
  /** dBFS below which audio counts as silence. Default -40, -90 to -10. */
  SilenceDb?: number;
  /** Shortest silence that can separate sections. Default 1500, 100 to 30000. */
  MinSilenceMs?: number;
  /** Shorter sections merge into a neighbor. Default 20000, 0 to 600000. */
  MinSectionMs?: number;
  /** Longer sections split evenly. Default 600000, 10000 to 3600000. */
  MaxSectionMs?: number;
  /** Excess sections merge. Default 100, 1 to 1000. */
  MaxSections?: number;
}
/** Split the recording at silences and run Summarize and Extract on each section. */
export interface SectionsSpec { /** Default false. */ Enable?: boolean; Detection?: SectionDetectionSpec; Summarize?: SummarizeSpec; Extract?: ExtractSpec; }
export interface TranscribeSpec { Enable?: boolean; Endpoint?: string | null; Model?: string | null; Language?: string | null; Prompt?: string | null; }
export interface VideoFunctionsSpec extends FunctionsSpec { Scenes?: ScenesSpec; Transcribe?: TranscribeSpec; }
/**
 * Defaults: Transcribe, Summarize, and Measure on; Extract and Sections off. Summarize, Extract, and section functions
 * need Transcribe or Listen.Enable.
 */
export interface AudioFunctionsSpec extends FunctionsSpec { Transcribe?: TranscribeSpec; Measure?: MeasureSpec; Sections?: SectionsSpec; }
export interface ImageAnalysisRequest {
  Image: MediaSource;
  Context?: ContextInput;
  Model?: ModelSelection | null;
  Functions: FunctionsSpec;
  IncludeProperties?: boolean;
}
export interface VideoAnalysisRequest {
  Video: MediaSource;
  Context?: ContextInput;
  Model?: ModelSelection | null;
  TextModel?: ModelSelection | null;
  Wait?: boolean;
  WaitTimeoutMs?: number;
  SampleFrames?: number;
  IncludeKeyframeData?: boolean;
  IncludeProperties?: boolean;
  Functions: VideoFunctionsSpec;
}
export interface AudioAnalysisRequest {
  /** Audio file, or a video file (only its audio track is used). */
  Audio: MediaSource;
  Context?: ContextInput;
  /** Takes precedence over Functions.Transcribe.Endpoint and Model. */
  TranscriptionModel?: ModelSelection | null;
  /** Listening options. By default the listening model hears the audio (Listen.Enable true). */
  Listen?: ListenSpec;
  /** Endpoint for Summarize and Extract on the transcript when the analysis does not listen, and for reducing section results. */
  TextModel?: ModelSelection | null;
  Wait?: boolean;
  /** Default 120000, 1000 to 3600000, also capped by Jobs.MaxWaitTimeoutMs. */
  WaitTimeoutMs?: number;
  IncludeProperties?: boolean;
  Functions?: AudioFunctionsSpec;
}
export interface SegmentRequest {
  /** The video or audio to cut, sent again as Url or Data. */
  Source: MediaSource;
  /** Start of the range, or the frame time for Frame. Default 0. */
  StartMs?: number;
  /** Exclusive end. Required for Video and Audio; clamped to the media duration. */
  EndMs?: number | null;
  /** Default Video. */
  Output?: SegmentOutput;
  /** mp4 or webm (Video), mp3, wav, flac, m4a, or ogg (Audio), jpg or png (Frame). */
  Format?: string | null;
  /** 16 to 4096; long-edge cap for video and frames. */
  MaxEdge?: number | null;
  /** Set by segments.get (Binary) and segments.getJson (Json). */
  ResponseFormat?: SegmentResponseFormat;
}
export interface ProbeRequest { Media: MediaSource; }

export interface JobFilter {
  status?: JobStatus; mediaKind?: MediaKind; principalName?: string;
  fromUtc?: Date | string; toUtc?: Date | string; pageNumber?: number; pageSize?: number;
}
export interface RequestHistoryFilter {
  method?: string; statusCode?: number; pathContains?: string; principalName?: string;
  fromUtc?: Date | string; toUtc?: Date | string; pageNumber?: number; pageSize?: number; bucketMinutes?: number;
}
export interface AnalyticsSummaryFilter {
  /** Default 24 hours before toUtc; aligned down to the bucket width. */
  fromUtc?: Date | string;
  /** Exclusive. Default now. */
  toUtc?: Date | string;
  /** Default by range: minute up to 2 hours, hour up to 7 days, otherwise day. */
  bucket?: AnalyticsBucketSize;
  mediaKind?: MediaKind;
  endpoint?: string;
  model?: string;
}
export interface AnalyticsAnalysesFilter {
  mediaKind?: MediaKind; success?: boolean; fromUtc?: Date | string; toUtc?: Date | string;
  /** 1 to 1000, default 50. */
  maxResults?: number;
  /** Default 0. */
  skip?: number;
}
export interface AnalyticsDeleteFilter {
  /** Exclusive cutoff on completion time. Omit to delete every analysis. */
  olderThanUtc?: Date | string;
}

// Responses

export interface ApiErrorResponse { Error: ApiErrorCode; StatusCode: number; Message: string; Details: string[]; RequestId: string | null; }
export interface MediaProperties {
  MediaKind: MediaKind; ContentType: string | null; Format: string | null; SizeBytes: number; Sha256: string | null;
  Width: number | null; Height: number | null; DurationMs: number | null; FrameRate: number | null; BitRate: number | null;
  VideoCodec: string | null; HasAudio: boolean; AudioCodec: string | null; AudioChannels: number | null;
  AudioSampleRate: number | null; Tags: Record<string, string>;
}
export interface FieldResult { Key: string; Type: FieldType; Value: unknown; Valid: boolean; RawValue: string | null; Error: string | null; Warnings: string[]; }
export interface StageTiming { Name: string; StartUtc: string; DurationMs: number; Success: boolean; }
export interface ModelCallTelemetry {
  Purpose: ModelCallPurpose; Target: string | null; Endpoint: string; Provider: ProviderType; Model: string | null;
  StartUtc: string; DurationMs: number; TimeToFirstTokenMs: number | null; GenerationMs: number | null; TokensPerSecond: number | null;
  QueueMs: number; Attempts: number; RepairAttempts: number;
  Success: boolean; Error: string | null; StatusCode: number | null; InputImages: number; InputImageBytes: number;
  InputAudioBytes: number; PromptTokens: number | null; CompletionTokens: number | null; TotalTokens: number | null;
  CachedTokens: number | null; ReasoningTokens: number | null; FinishReason: string | null;
}
export interface TokenTotals {
  ModelCalls: number; FailedModelCalls: number; ModelMs: number; PromptTokens: number; CompletionTokens: number;
  TotalTokens: number; CachedTokens: number; ReasoningTokens: number; InputImages: number; InputImageBytes: number;
}
export interface AnalysisTelemetry {
  StartUtc: string; EndUtc: string | null; TotalMs: number;
  /** Equals QueueWaitMs for jobs; null for images. */
  QueuedMs: number | null;
  QueueWaitMs: number;
  Stages: StageTiming[]; ModelCalls: ModelCallTelemetry[]; Totals: TokenTotals;
}
export interface AnalysisResponse {
  Id: string;
  /** The x-request-id of the submitting request. */
  RequestId: string | null;
  Success: boolean; MediaKind: MediaKind; Properties: MediaProperties | null;
  /** Caller-defined keys (summary key and field keys) mapped to coerced values. */
  Results: Record<string, unknown>;
  Fields: FieldResult[]; Telemetry: AnalysisTelemetry; Warnings: string[]; Errors: string[];
}
export type ImageAnalysisResponse = AnalysisResponse;
export interface KeyframeInfo { TimestampMs: number; Sha256: string | null; Width: number | null; Height: number | null; Data: string | null; }
export interface SceneResult {
  Index: number; StartMs: number; EndMs: number; DurationMs: number; Keyframes: KeyframeInfo[];
  TranscriptText: string | null; Results: Record<string, unknown>; Fields: FieldResult[]; Errors: string[];
}
export interface TranscriptSegmentResult { StartMs: number; EndMs: number; Text: string; }
export interface TranscriptResult { Language: string | null; Text: string; Segments: TranscriptSegmentResult[]; }
/** One audio clip rendered and sent to a listening model. */
export interface ListenClip {
  Scope: ListenClipScope;
  /** Section or scene index; null for Recording clips. */
  Index: number | null;
  StartMs: number; EndMs: number;
  /** Size of the rendered clip in bytes, before base64 encoding. */
  SizeBytes: number;
}
/** Whether, how, and by which model the audio was heard. */
export interface ListeningInfo {
  /** True when at least one model call carried audio, including a call that then failed. */
  Listened: boolean;
  Mode: ListeningMode;
  /** Null when listening happened. */
  Reason: ListenSkipReason | null;
  Endpoint: string | null; Model: string | null;
  /** The effective rendition, every member filled. */
  Rendition: AudioRenditionSpec;
  DurationMs: number;
  /** Distinct audio the models heard; overlapping clips are counted once. */
  HeardMs: number;
  Clips: ListenClip[];
}
/** Deterministic measurements. Levels are dB relative to full scale; a member is null when ffmpeg could not measure it. */
export interface AudioMeasurements {
  IntegratedLufs: number | null; LoudnessRangeLu: number | null; TruePeakDbtp: number | null;
  PeakDbfs: number | null; RmsDbfs: number | null; CrestFactorDb: number | null; PeakCount: number | null;
  /** TruePeakDbtp at or above Media.ClippingTruePeakDbtp and PeakCount per minute at or above Media.ClippingMinPeaksPerMinute. */
  ClippingLikely: boolean;
  /** 0 to 1, using Media.MeasureSilenceDb and Media.MeasureMinSilenceMs. */
  SilenceRatio: number; SilenceCount: number; LeadingSilenceMs: number; TrailingSilenceMs: number;
  Channels: number;
  /** Side-to-mid RMS difference in dB; null for sources that are not stereo. */
  StereoWidthDb: number | null;
  EffectivelyMono: boolean;
}
/** Result for one audio section. Each section can be cut back out with segments.get. */
export interface SectionResult {
  Index: number; StartMs: number; EndMs: number; DurationMs: number;
  TranscriptText: string | null; Results: Record<string, unknown>; Fields: FieldResult[]; Errors: string[];
}
export interface VideoAnalysisResponse extends AnalysisResponse {
  Transcript: TranscriptResult | null; Scenes: SceneResult[];
  /** Whether scene calls heard the scene audio; present whenever scenes are enabled. */
  Listening?: ListeningInfo | null;
}
export interface AudioAnalysisResponse extends AnalysisResponse {
  Transcript: TranscriptResult | null;
  /** Always present. */
  Listening: ListeningInfo;
  /** Present when Functions.Measure.Enable is true and the measurement succeeded. */
  Measurements?: AudioMeasurements | null;
  /** Present when Functions.Sections.Enable is true. */
  Sections?: SectionResult[] | null;
}
export interface JobProgress { Stage: string | null; Completed: number; Total: number; Percent: number; }
export interface JobResponse {
  Id: string; Status: JobStatus; MediaKind: MediaKind; PrincipalName: string | null; SourceUrl: string | null;
  SizeBytes: number; DurationMs: number; Progress: JobProgress; Error: string | null; CreatedUtc: string;
  StartedUtc: string | null; CompletedUtc: string | null; ResultUrl: string | null;
}
export interface VideoSubmitResult {
  /** 202 for a queued job, 200 when Wait finished. */
  statusCode: number;
  jobId: string;
  /** Set for 202, or for 200 when the job finished without succeeding. */
  job: JobResponse | null;
  /** Set when Wait was true and the job succeeded in time. */
  result: VideoAnalysisResponse | null;
}
export interface AudioSubmitResult {
  /** 202 for a queued job, 200 when Wait finished. */
  statusCode: number;
  jobId: string;
  /** Set for 202, or for 200 when the job finished without succeeding. */
  job: JobResponse | null;
  /** Set when Wait was true and the job succeeded in time. */
  result: AudioAnalysisResponse | null;
}
export interface SegmentResponse {
  MediaType: string; Filename: string;
  /** After clamping; EndMs equals StartMs for frames. */
  StartMs: number; EndMs: number;
  /** 0 for frames. */
  DurationMs: number;
  SizeBytes: number;
  /** Base64 media. */
  Data: string;
}
/** Result of segments.get: the media bytes plus the parsed response headers. */
export interface SegmentBinaryResult {
  data: Uint8Array;
  /** Content-Type. */
  mediaType: string | null;
  /** From Content-Disposition. */
  filename: string | null;
  /** From x-keyframe-segment-start-ms. */
  startMs: number | null;
  /** From x-keyframe-segment-end-ms. */
  endMs: number | null;
  /** From x-keyframe-segment-duration-ms. */
  durationMs: number | null;
  sizeBytes: number;
  requestId: string | null;
}
export interface EnumerationResult<T> { Items: T[]; PageNumber: number; PageSize: number; TotalCount: number; TotalPages: number; }
export interface HealthResponse {
  Status: string; Product: string; Version: string; StartedUtc: string; UptimeSeconds: number; FfmpegAvailable: boolean;
  FfmpegVersion: string | null; Database: string; ActiveJobs: number; QueuedJobs: number; Endpoints: Record<string, boolean>;
}
export interface WhoAmIResponse { IsAuthenticated: boolean; IsAdmin: boolean; PrincipalName: string | null; AuthenticationRequired: boolean; Version: string; }
export interface EndpointResponse {
  Name: string; Provider: ProviderType; Kind: EndpointKind; Endpoint: string | null; ApiKeySet: boolean; ApiKeyLast4: string | null;
  Model: string | null; MaxConcurrentRequests: number; TimeoutMs: number; Active: boolean; IsDefault: boolean;
  ProviderAvailable: boolean; SupportsImages: boolean; SupportsStructuredOutput: boolean; SupportsTranscription: boolean;
  /** Whether the provider accepts audio input on chat, so the endpoint can be a listening endpoint (Gemini, OpenAiCompatible, Mock). */
  SupportsAudioInput: boolean; Healthy: boolean; HealthMessage: string | null; LastCheckedUtc: string | null;
}
export interface EndpointTestResponse { Name: string; Success: boolean; DurationMs: number; Model: string | null; Error: string | null; }
export interface RequestHistoryEntry {
  Id: string; RequestId: string | null; PrincipalName: string | null; Method: string; Path: string; Url: string;
  StatusCode: number; Success: boolean; DurationMs: number; SourceIp: string | null; RequestContentType: string | null;
  ResponseContentType: string | null; RequestHeaders: Record<string, string> | null; RequestBody: string | null;
  RequestBodyBytes: number; RequestBodyTruncated: boolean; ResponseHeaders: Record<string, string> | null;
  ResponseBody: string | null; ResponseBodyBytes: number; ResponseBodyTruncated: boolean; CreatedUtc: string; CompletedUtc: string | null;
}
export interface RequestHistorySummaryBucket { BucketStartUtc: string; BucketEndUtc: string; SuccessCount: number; FailureCount: number; AverageDurationMs: number; }
export interface RequestHistorySummary {
  TotalCount: number; TotalSuccess: number; TotalFailure: number; AverageDurationMs: number;
  BucketMinutes: number; Buckets: RequestHistorySummaryBucket[];
}
export interface DeleteResult { DeletedCount: number; }

// Analytics

export interface Distribution {
  Count: number; Min: number; Max: number; Avg: number; P50: number; P90: number; P95: number; P99: number; StdDev: number; Sum: number;
}
export interface StatBlock {
  Count: number; Succeeded: number; Failed: number; SuccessRate: number;
  AnalysisMs: Distribution | null; QueueWaitMs: Distribution | null; MediaDurationMs: Distribution | null;
  RealtimeFactor: Distribution | null; MediaBytes: Distribution | null;
  /** Logical calls, from the analyses rows. */
  ModelCalls: number; FailedModelCalls: number; Retries: number; RepairAttempts: number;
  ModelCallMs: Distribution | null; TimeToFirstTokenMs: Distribution | null; GenerationMs: Distribution | null; TokensPerSecond: Distribution | null;
  PromptTokens: number; CompletionTokens: number; TotalTokens: number; CachedTokens: number; ReasoningTokens: number;
  TokensPerAnalysis: Distribution | null; InputImages: number; InputImageBytes: number; InputAudioBytes: number;
  Scenes: number; TranscriptCharacters: number;
}
export interface AnalyticsBucket { StartUtc: string; Stats: StatBlock; }
export interface ModelStats {
  Endpoint: string; Provider: ProviderType; Model: string | null;
  /** Attempts. */
  Calls: number; Failed: number;
  DurationMs: Distribution | null; TimeToFirstTokenMs: Distribution | null; GenerationMs: Distribution | null; TokensPerSecond: Distribution | null;
  PromptTokens: number; CompletionTokens: number;
}
export interface PurposeStats {
  Purpose: ModelCallPurpose;
  /** Attempts. */
  Calls: number; Failed: number; DurationMs: Distribution | null; TimeToFirstTokenMs: Distribution | null;
}
export interface StageStats { Stage: string; DurationMs: Distribution | null; }
export interface AnalyticsSummary {
  FromUtc: string; ToUtc: string; Bucket: AnalyticsBucketSize; MediaKind: MediaKind | null; Endpoint: string | null; Model: string | null;
  Totals: StatBlock;
  /** Always has Image, Video, and Audio. */
  ByMediaKind: Record<'Image' | 'Video' | 'Audio', StatBlock> & Record<string, StatBlock>;
  /** Every bucket in the range, gap-filled with zero counts and null distributions. */
  Buckets: AnalyticsBucket[];
  ByModel: ModelStats[]; ByStage: StageStats[]; ByPurpose: PurposeStats[];
}
/** One model call attempt. */
export interface ModelCallRecord {
  Id: string; AnalysisId: string; Purpose: ModelCallPurpose; Target: string | null; Endpoint: string; Provider: ProviderType;
  Model: string | null; Attempt: number; StartUtc: string; Success: boolean; Error: string | null; StatusCode: number | null;
  DurationMs: number; QueueMs: number; TimeToFirstTokenMs: number | null; GenerationMs: number | null; TokensPerSecond: number | null;
  PromptTokens: number | null; CompletionTokens: number | null; TotalTokens: number | null; CachedTokens: number | null;
  ReasoningTokens: number | null; InputImages: number; InputImageBytes: number; InputAudioBytes: number; FinishReason: string | null;
}
export interface AnalysisRecord {
  Id: string; RequestId: string | null; JobId: string | null; MediaKind: MediaKind; PrincipalName: string | null;
  Success: boolean; Error: string | null; StartedUtc: string; CompletedUtc: string; AnalysisMs: number; QueueWaitMs: number;
  MediaDurationMs: number | null; MediaBytes: number; RealtimeFactor: number | null;
  VisionEndpoint: string | null; VisionModel: string | null; TextEndpoint: string | null; TextModel: string | null;
  TranscriptionEndpoint: string | null; TranscriptionModel: string | null;
  ModelCalls: number; FailedModelCalls: number; Retries: number; RepairAttempts: number;
  PromptTokens: number; CompletionTokens: number; TotalTokens: number; CachedTokens: number; ReasoningTokens: number;
  InputImages: number; InputImageBytes: number; InputAudioBytes: number; Scenes: number; TranscriptCharacters: number;
  FieldCount: number; InvalidFields: number;
  /** Null on listed records; filled by getAnalysis. */
  Calls: ModelCallRecord[] | null;
}
export interface RestartResponse { Accepted: boolean; Message: string; }

// Settings

export interface RestSettings { Hostname: string; Port: number; Ssl: boolean; MaxRequestBodyBytes: number; }
export interface AccessKeySettings { Name: string; Key: string; Active: boolean; }
export interface AuthSettings { RequireAuthentication: boolean; AdminApiKey: string; AccessKeys: AccessKeySettings[]; MaxFailedAttemptsPerMinute: number; }
export interface DatabaseSettings {
  Type: DatabaseType; Filename: string; Hostname: string | null; Port: number; DatabaseName: string | null;
  Username: string | null; Password: string | null; TrustServerCertificate: boolean; CommandTimeoutSeconds: number;
}
export interface EndpointSettings {
  Name: string; Provider: ProviderType; Kind: EndpointKind; Endpoint: string | null; ApiKey: string | null; Model: string | null;
  MaxConcurrentRequests: number; TimeoutMs: number; Temperature: number | null; MaxOutputTokens: number;
  /** Largest inline media per call, counted as encoded on the wire. Default 20971520, 1048576 to 1073741824. */
  MaxInlineMediaBytes: number;
  /** Listen budget in seconds for this endpoint. Null uses Media.MaxListenSeconds. 10 to 7200. */
  MaxListenSeconds: number | null;
  Active: boolean;
}
export interface InferenceSettings {
  DefaultVision: string | null; DefaultText: string | null; DefaultTranscription: string | null;
  /** Completion endpoint used to listen to audio when a request does not name one. Its provider must accept audio input. Default null. */
  DefaultListening: string | null;
  /** Re-probe interval for healthy endpoints. Default 300000, 10000 to 3600000. */
  HealthProbeIntervalMs: number;
  /** Re-probe interval for unhealthy or never-checked endpoints. Default 15000, 1000 to 600000. */
  UnhealthyProbeIntervalMs: number;
  MaxRetries: number;
  MaxRepairAttempts: number; RetryBackoffMs: number; RequestTimeoutMs: number; MaxTranscriptCharacters: number; Endpoints: EndpointSettings[];
}
export interface MediaSettings {
  TempDirectory: string; FfmpegPath: string; FfprobePath: string; MaxDownloadBytes: number; DownloadTimeoutMs: number;
  AllowPrivateNetworkUrls: boolean; ImageMaxEdge: number; ThumbnailMaxEdge: number; JpegQuality: number; MaxParallelFfmpeg: number;
  FfmpegTimeoutMs: number; MaxVideoDurationMs: number; MaxSegmentMs: number; MaxAudioDurationMs: number; TranscriptionChunkSeconds: number;
  /** Default Listen.Rendition.Format. Default Mp3. */
  ListenFormat: ListenFormat;
  /** Hz. Default 16000, 8000 to 48000. */
  ListenSampleRate: number;
  /** Default 1, 1 to 2. */
  ListenChannels: number;
  /** Mp3 only. Default 64, 16 to 320. */
  ListenBitrateKbps: number;
  /** Default listen budget in seconds. Default 1200, 10 to 7200. */
  MaxListenSeconds: number;
  /** Silence threshold for Measurements in dBFS. Default -50, -90 to -10. */
  MeasureSilenceDb: number;
  /** Shortest silence counted in Measurements. Default 500, 100 to 30000. */
  MeasureMinSilenceMs: number;
  /** ClippingLikely true-peak threshold in dBTP. Default -0.1, -6 to 3. */
  ClippingTruePeakDbtp: number;
  /** ClippingLikely peak-count threshold per minute. Default 10, 0 to 100000. */
  ClippingMinPeaksPerMinute: number;
  /** EffectivelyMono threshold on StereoWidthDb. Default -40, -120 to 0. */
  MonoMaxSideDb: number;
}
export interface JobSettings { MaxConcurrentJobs: number; MaxQueuedJobs: number; RetentionDays: number; MaxWaitTimeoutMs: number; }
export interface RequestHistorySettings { Enable: boolean; MaxRequestBodyBytes: number; MaxResponseBodyBytes: number; RetentionDays: number; }
export interface AnalyticsSettings { Enable: boolean; RetentionDays: number; }
export interface SyslogServerSettings { Hostname: string; Port: number; }
export interface LoggingSettings {
  ConsoleLogging: boolean; EnableColors: boolean; LogDirectory: string | null; LogFilename: string | null;
  MinimumSeverity: number; Servers: SyslogServerSettings[];
}
export interface TelemetrySettings {
  Enable: boolean; ServiceName: string; OtlpEnable: boolean; OtlpEndpoint: string; OtlpProtocol: string;
  PrometheusEnable: boolean; PrometheusHostname: string; PrometheusPort: number; PrometheusPath: string;
  LokiEnable: boolean; LokiEndpoint: string; ExportLogs: boolean; MetricExportIntervalMs: number; TraceSamplingRatio: number;
}
export interface KeyframeSettings {
  CreatedUtc: string; Rest: RestSettings; Auth: AuthSettings; Database: DatabaseSettings; Inference: InferenceSettings;
  Media: MediaSettings; Jobs: JobSettings; RequestHistory: RequestHistorySettings; Analytics: AnalyticsSettings;
  Logging: LoggingSettings; Telemetry: TelemetrySettings;
}
export interface SettingsFieldMetadata { Path: string; ApplyMode: 'Live' | 'Restart'; OverriddenByEnvironment: boolean; EnvironmentVariable: string | null; Secret: boolean; }
export interface SettingsResponse { Settings: KeyframeSettings; Fields: SettingsFieldMetadata[]; RestartRequired: boolean; SettingsFile: string | null; }

// Errors

export declare class KeyframeApiError extends Error {
  method: string;
  path: string;
  statusCode: number;
  apiError: ApiErrorResponse | null;
  error: ApiErrorCode | null;
  details: string[];
  requestId: string | null;
  responseBody: string;
}
export declare class KeyframeJobError extends Error { job: JobResponse; }
export declare class KeyframeTimeoutError extends Error {}
export declare class KeyframeConnectionError extends Error {}

// Helpers

export declare function isTerminalStatus(status: string): boolean;
export declare function mediaFromUrl(url: string, mediaType?: string): MediaSource;
export declare function mediaFromBytes(bytes: Uint8Array | ArrayBuffer, mediaType?: string): MediaSource;
/** Node only. */
export declare function mediaFromFile(path: string, mediaType?: string): Promise<MediaSource>;
export declare function guessMediaType(name: string): string | undefined;

// Client

export interface RequestOptions { signal?: AbortSignal; }
export interface WaitOptions extends RequestOptions { pollIntervalMs?: number; timeoutMs?: number; }
export interface RawResponse { statusCode: number; body: string; headers: Headers; requestId: string | null; }
export interface RawBytesResponse { statusCode: number; body: Uint8Array; headers: Headers; requestId: string | null; }
export interface KeyframeClientOptions {
  baseUrl?: string;
  apiKey?: string;
  authHeader?: 'bearer' | 'x-api-key';
  timeoutMs?: number;
  fetch?: typeof fetch;
}

export declare class KeyframeClient {
  constructor(options?: KeyframeClientOptions);
  readonly baseUrl: string;
  authHeader: 'bearer' | 'x-api-key';
  timeoutMs: number;
  readonly hasApiKey: boolean;
  setApiKey(apiKey: string | null): void;
  /** Node only: read a file and analyze it as the request's Image. */
  analyzeImageFile(path: string, request?: Omit<ImageAnalysisRequest, 'Image'>, options?: RequestOptions): Promise<ImageAnalysisResponse>;
  sendRaw(method: string, path: string, body?: unknown, options?: RequestOptions): Promise<RawResponse>;
  /** Like sendRaw, but reads the body as bytes. */
  sendRawBytes(method: string, path: string, body?: unknown, options?: RequestOptions): Promise<RawBytesResponse>;
  health: {
    get(options?: RequestOptions): Promise<HealthResponse>;
    isAlive(options?: RequestOptions): Promise<boolean>;
  };
  whoami: { get(options?: RequestOptions): Promise<WhoAmIResponse>; };
  image: { analyze(request: ImageAnalysisRequest, options?: RequestOptions): Promise<ImageAnalysisResponse>; };
  video: {
    analyze(request: VideoAnalysisRequest, options?: RequestOptions): Promise<VideoSubmitResult>;
    analyzeAndWait(request: VideoAnalysisRequest, options?: WaitOptions): Promise<VideoAnalysisResponse>;
  };
  audio: {
    analyze(request: AudioAnalysisRequest, options?: RequestOptions): Promise<AudioSubmitResult>;
    analyzeAndWait(request: AudioAnalysisRequest, options?: WaitOptions): Promise<AudioAnalysisResponse>;
  };
  segments: {
    /** ResponseFormat is forced to Binary. */
    get(request: SegmentRequest, options?: RequestOptions): Promise<SegmentBinaryResult>;
    /** ResponseFormat is forced to Json. */
    getJson(request: SegmentRequest, options?: RequestOptions): Promise<SegmentResponse>;
  };
  probe: { probe(request: ProbeRequest | MediaSource, options?: RequestOptions): Promise<MediaProperties>; };
  jobs: {
    defaultPollIntervalMs: number;
    defaultWaitTimeoutMs: number;
    list(filter?: JobFilter, options?: RequestOptions): Promise<EnumerationResult<JobResponse>>;
    get(id: string, options?: RequestOptions): Promise<JobResponse>;
    /** Video jobs return VideoAnalysisResponse; audio jobs return AudioAnalysisResponse. */
    getResult<T extends AnalysisResponse = VideoAnalysisResponse>(id: string, options?: RequestOptions): Promise<T>;
    cancel(id: string, options?: RequestOptions): Promise<JobResponse>;
    delete(id: string, options?: RequestOptions): Promise<void>;
    wait(id: string, options?: WaitOptions): Promise<JobResponse>;
  };
  endpoints: {
    list(options?: RequestOptions): Promise<EndpointResponse[]>;
    test(name: string, options?: RequestOptions): Promise<EndpointTestResponse>;
  };
  requestHistory: {
    list(filter?: RequestHistoryFilter, options?: RequestOptions): Promise<EnumerationResult<RequestHistoryEntry>>;
    summary(filter?: RequestHistoryFilter, options?: RequestOptions): Promise<RequestHistorySummary>;
    get(id: string, options?: RequestOptions): Promise<RequestHistoryEntry>;
    delete(id: string, options?: RequestOptions): Promise<void>;
    deleteMany(filter?: RequestHistoryFilter, options?: RequestOptions): Promise<DeleteResult>;
  };
  settings: {
    get(options?: RequestOptions): Promise<SettingsResponse>;
    update(settings: KeyframeSettings, options?: RequestOptions): Promise<SettingsResponse>;
    restart(options?: RequestOptions): Promise<RestartResponse>;
  };
  /** Admin key required. */
  analytics: {
    summary(filter?: AnalyticsSummaryFilter, options?: RequestOptions): Promise<AnalyticsSummary>;
    listAnalyses(filter?: AnalyticsAnalysesFilter, options?: RequestOptions): Promise<EnumerationResult<AnalysisRecord>>;
    getAnalysis(id: string, options?: RequestOptions): Promise<AnalysisRecord>;
    deleteAnalysis(id: string, options?: RequestOptions): Promise<DeleteResult>;
    /** Without olderThanUtc, deletes every analysis. */
    deleteAnalyses(filter?: AnalyticsDeleteFilter, options?: RequestOptions): Promise<DeleteResult>;
  };
  toString(): string;
  toJSON(): { baseUrl: string; hasApiKey: boolean; authHeader: string; timeoutMs: number };
}

export default KeyframeClient;
