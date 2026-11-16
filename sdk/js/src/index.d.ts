// Type definitions for keyframe-sdk 0.1.0. Wire models use the server's PascalCase JSON names.

export declare const VERSION: string;
export declare const DEFAULT_BASE_URL: string;

export type MediaKind = 'Unknown' | 'Image' | 'Video' | 'Audio';
export type JobStatus = 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled' | 'Interrupted';
export type FieldType = 'Boolean' | 'String' | 'Integer' | 'Number' | 'Enum' | 'StringList' | 'Object';
export type ExtractMode = 'Combined' | 'PerField';
export type SummaryStyle = 'Paragraph' | 'Bullets' | 'Caption';
export type ModelCallPurpose = 'Summarize' | 'Extract' | 'SceneSummarize' | 'SceneExtract' | 'Transcribe';
export type EndpointKind = 'Completion' | 'Transcription';
export type ProviderType =
  | 'Mock' | 'Ollama' | 'OllamaCloud' | 'OpenAi' | 'OpenAiCompatible' | 'AzureOpenAi'
  | 'Anthropic' | 'Gemini' | 'VertexAi' | 'Bedrock' | 'XAi' | 'Cohere';
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
export interface ScenesSpec { Enable?: boolean; Detection?: SceneDetectionSpec; FramesPerScene?: number; Summarize?: SummarizeSpec; Extract?: ExtractSpec; }
export interface TranscribeSpec { Enable?: boolean; Endpoint?: string | null; Model?: string | null; Language?: string | null; Prompt?: string | null; }
export interface VideoFunctionsSpec extends FunctionsSpec { Scenes?: ScenesSpec; Transcribe?: TranscribeSpec; }
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
export interface ProbeRequest { Media: MediaSource; }

export interface JobFilter {
  status?: JobStatus; mediaKind?: MediaKind; principalName?: string;
  fromUtc?: Date | string; toUtc?: Date | string; pageNumber?: number; pageSize?: number;
}
export interface RequestHistoryFilter {
  method?: string; statusCode?: number; pathContains?: string; principalName?: string;
  fromUtc?: Date | string; toUtc?: Date | string; pageNumber?: number; pageSize?: number; bucketMinutes?: number;
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
  StartUtc: string; DurationMs: number; TimeToFirstTokenMs: number | null; Attempts: number; RepairAttempts: number;
  Success: boolean; Error: string | null; StatusCode: number | null; InputImages: number; InputImageBytes: number;
  InputAudioBytes: number; PromptTokens: number | null; CompletionTokens: number | null; TotalTokens: number | null;
  CachedTokens: number | null; ReasoningTokens: number | null; FinishReason: string | null;
}
export interface TokenTotals {
  ModelCalls: number; FailedModelCalls: number; ModelMs: number; PromptTokens: number; CompletionTokens: number;
  TotalTokens: number; CachedTokens: number; ReasoningTokens: number; InputImages: number; InputImageBytes: number;
}
export interface AnalysisTelemetry {
  StartUtc: string; EndUtc: string | null; TotalMs: number; QueuedMs: number | null;
  Stages: StageTiming[]; ModelCalls: ModelCallTelemetry[]; Totals: TokenTotals;
}
export interface AnalysisResponse {
  Id: string; Success: boolean; MediaKind: MediaKind; Properties: MediaProperties | null;
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
export interface VideoAnalysisResponse extends AnalysisResponse { Transcript: TranscriptResult | null; Scenes: SceneResult[]; }
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
export interface EnumerationResult<T> { Items: T[]; PageNumber: number; PageSize: number; TotalCount: number; TotalPages: number; }
export interface HealthResponse {
  Status: string; Product: string; Version: string; StartedUtc: string; UptimeSeconds: number; FfmpegAvailable: boolean;
  FfmpegVersion: string | null; Database: string; ActiveJobs: number; QueuedJobs: number; Endpoints: Record<string, boolean>;
}
export interface WhoAmIResponse { IsAuthenticated: boolean; IsAdmin: boolean; PrincipalName: string | null; AuthenticationRequired: boolean; Version: string; }
export interface EndpointResponse {
  Name: string; Provider: ProviderType; Kind: EndpointKind; Endpoint: string | null; ApiKeySet: boolean; ApiKeyLast4: string | null;
  Model: string | null; MaxConcurrentRequests: number; TimeoutMs: number; Active: boolean; IsDefault: boolean;
  ProviderAvailable: boolean; SupportsImages: boolean; SupportsStructuredOutput: boolean; SupportsTranscription: boolean; Healthy: boolean; HealthMessage: string | null; LastCheckedUtc: string | null;
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
  MaxConcurrentRequests: number; TimeoutMs: number; Temperature: number | null; MaxOutputTokens: number; Active: boolean;
}
export interface InferenceSettings {
  DefaultVision: string | null; DefaultText: string | null; DefaultTranscription: string | null; MaxRetries: number;
  MaxRepairAttempts: number; RetryBackoffMs: number; RequestTimeoutMs: number; MaxTranscriptCharacters: number; Endpoints: EndpointSettings[];
}
export interface MediaSettings {
  TempDirectory: string; FfmpegPath: string; FfprobePath: string; MaxDownloadBytes: number; DownloadTimeoutMs: number;
  AllowPrivateNetworkUrls: boolean; ImageMaxEdge: number; ThumbnailMaxEdge: number; JpegQuality: number; MaxParallelFfmpeg: number;
  FfmpegTimeoutMs: number; MaxVideoDurationMs: number; TranscriptionChunkSeconds: number;
}
export interface JobSettings { MaxConcurrentJobs: number; MaxQueuedJobs: number; RetentionDays: number; MaxWaitTimeoutMs: number; }
export interface RequestHistorySettings { Enabled: boolean; MaxRequestBodyBytes: number; MaxResponseBodyBytes: number; RetentionDays: number; }
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
  Media: MediaSettings; Jobs: JobSettings; RequestHistory: RequestHistorySettings; Logging: LoggingSettings; Telemetry: TelemetrySettings;
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
  probe: { probe(request: ProbeRequest | MediaSource, options?: RequestOptions): Promise<MediaProperties>; };
  jobs: {
    defaultPollIntervalMs: number;
    defaultWaitTimeoutMs: number;
    list(filter?: JobFilter, options?: RequestOptions): Promise<EnumerationResult<JobResponse>>;
    get(id: string, options?: RequestOptions): Promise<JobResponse>;
    getResult(id: string, options?: RequestOptions): Promise<VideoAnalysisResponse>;
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
  toString(): string;
  toJSON(): { baseUrl: string; hasApiKey: boolean; authHeader: string; timeoutMs: number };
}

export default KeyframeClient;
