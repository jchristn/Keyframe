// Deterministic API fixtures shaped like the Keyframe contract. Used by the unit tests and by
// the Playwright visual check (a route interceptor outside the repo), so keep this file free of Vite-only
// imports.

function rand(seed) {
  let s = seed >>> 0;
  return () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 4294967296;
  };
}

export function dist(base, spread = 0.6, count = 40) {
  if (!count) return null;
  return {
    Count: count,
    Min: base * 0.3,
    Max: base * (2.4 + spread),
    Avg: base * 1.08,
    P50: base,
    P90: base * (1.5 + spread / 2),
    P95: base * (1.8 + spread / 2),
    P99: base * (2.2 + spread),
    StdDev: base * 0.4,
    Sum: base * count
  };
}

export function statBlock(scale = 1, { kind = null, seed = 1 } = {}) {
  const r = rand(seed);
  const count = Math.max(0, Math.round((20 + r() * 30) * scale));
  const failed = Math.round(count * (0.02 + r() * 0.06));
  const video = kind === 'Video';
  const audio = kind === 'Audio';
  const image = kind === 'Image';
  return {
    Count: count,
    Succeeded: count - failed,
    Failed: failed,
    SuccessRate: count ? (count - failed) / count : 0,
    AnalysisMs: count ? dist((video ? 42000 : audio ? 18000 : image ? 3200 : 12000) * (0.8 + r() * 0.4), 0.5, count) : null,
    QueueWaitMs: count ? dist((image ? 0.5 : 900) * (0.6 + r()), 1, count) : null,
    MediaDurationMs: count && !image ? dist(video ? 95000 : 160000, 0.3, count) : null,
    RealtimeFactor: count && !image ? dist(video ? 0.44 : 0.11, 0.3, count) : null,
    MediaBytes: count ? dist(image ? 380000 : 24000000, 0.4, count) : null,
    ModelCalls: count * (video ? 14 : audio ? 3 : 2),
    FailedModelCalls: Math.round(failed * 1.5),
    Retries: Math.round(failed * 2),
    RepairAttempts: Math.round(count * 0.05),
    ModelCallMs: count ? dist(1800 * (0.8 + r() * 0.4), 0.6, count * 3) : null,
    TimeToFirstTokenMs: count ? dist(420 * (0.8 + r() * 0.5), 0.7, count * 3) : null,
    GenerationMs: count ? dist(1300 * (0.8 + r() * 0.4), 0.5, count * 3) : null,
    TokensPerSecond: count ? dist(38 * (0.85 + r() * 0.3), 0.2, count * 3) : null,
    PromptTokens: count * 1800,
    CompletionTokens: count * 420,
    TotalTokens: count * 2220,
    CachedTokens: count * 300,
    ReasoningTokens: count * 90,
    TokensPerAnalysis: count ? dist(2220, 0.5, count) : null,
    InputImages: image ? count : video ? count * 24 : 0,
    InputImageBytes: (image ? count : video ? count * 24 : 0) * 210000,
    InputAudioBytes: audio || video ? count * 1900000 : 0,
    Scenes: video ? count * 12 : 0,
    TranscriptCharacters: audio || video ? count * 5400 : 0
  };
}

export function makeSummary({ now = Date.parse('2026-11-15T18:00:00Z'), hours = 24, bucket = 'hour' } = {}) {
  const step = bucket === 'minute' ? 60000 : bucket === 'day' ? 86400000 : 3600000;
  const n = bucket === 'minute' ? 60 : hours;
  const buckets = [];
  for (let i = 0; i < n; i += 1) {
    const wave = 0.35 + 0.65 * Math.abs(Math.sin(i / 3.2));
    buckets.push({ StartUtc: new Date(now - (n - i) * step).toISOString(), Stats: i === 5 ? statBlock(0, { seed: i }) : statBlock(wave / 6, { seed: i + 7 }) });
  }
  return {
    FromUtc: new Date(now - n * step).toISOString(),
    ToUtc: new Date(now).toISOString(),
    Bucket: bucket,
    MediaKind: null,
    Totals: statBlock(4, { seed: 3 }),
    ByMediaKind: { Image: statBlock(2, { kind: 'Image', seed: 4 }), Video: statBlock(0.8, { kind: 'Video', seed: 5 }), Audio: statBlock(1.2, { kind: 'Audio', seed: 6 }) },
    Buckets: buckets,
    ByModel: [
      { Endpoint: 'ollama-gemma3-4b', Provider: 'Ollama', Model: 'gemma3:4b', Calls: 412, Failed: 6, DurationMs: dist(2100), TimeToFirstTokenMs: dist(380), GenerationMs: dist(1600), TokensPerSecond: dist(41), PromptTokens: 612000, CompletionTokens: 151000 },
      { Endpoint: 'ollama-gpt-oss-20b', Provider: 'Ollama', Model: 'gpt-oss:20b', Calls: 188, Failed: 2, DurationMs: dist(5200), TimeToFirstTokenMs: dist(910), GenerationMs: dist(4100), TokensPerSecond: dist(23), PromptTokens: 402000, CompletionTokens: 98000 },
      { Endpoint: 'whisper', Provider: 'OpenAiCompatible', Model: 'Systran/faster-whisper-small', Calls: 64, Failed: 1, DurationMs: dist(14000), TimeToFirstTokenMs: null, GenerationMs: null, TokensPerSecond: null, PromptTokens: 0, CompletionTokens: 0 }
    ],
    ByStage: [
      { Stage: 'Resolve', DurationMs: dist(120) },
      { Stage: 'Probe', DurationMs: dist(260) },
      { Stage: 'Normalize', DurationMs: dist(540) },
      { Stage: 'SceneDetection', DurationMs: dist(6200) },
      { Stage: 'FrameExtraction', DurationMs: dist(3900) },
      { Stage: 'Transcribe', DurationMs: dist(11800) },
      { Stage: 'Summarize', DurationMs: dist(4300) },
      { Stage: 'Extract', DurationMs: dist(3600) }
    ],
    ByPurpose: [
      { Purpose: 'Summarize', Calls: 240, Failed: 3, DurationMs: dist(4100), TimeToFirstTokenMs: dist(640) },
      { Purpose: 'Extract', Calls: 198, Failed: 4, DurationMs: dist(3300), TimeToFirstTokenMs: dist(520) },
      { Purpose: 'SceneSummarize', Calls: 162, Failed: 1, DurationMs: dist(1900), TimeToFirstTokenMs: dist(330) },
      { Purpose: 'Transcribe', Calls: 64, Failed: 1, DurationMs: dist(14000), TimeToFirstTokenMs: null }
    ]
  };
}

export function analysisRecord(i, now = Date.parse('2026-11-15T18:00:00Z')) {
  const kinds = ['Image', 'Video', 'Audio'];
  const kind = kinds[i % 3];
  const started = new Date(now - (i + 1) * 7 * 60000).toISOString();
  const ms = kind === 'Image' ? 2900 + i * 37 : kind === 'Video' ? 41000 + i * 410 : 17000 + i * 230;
  const media = kind === 'Image' ? null : kind === 'Video' ? 96000 : 152000;
  return {
    Id: 'kfa_' + String(1000 + i),
    RequestId: 'req_' + i,
    JobId: kind === 'Image' ? null : 'kfj_' + String(500 + i),
    MediaKind: kind,
    PrincipalName: i % 4 === 0 ? 'admin' : 'default',
    Success: i % 7 !== 3,
    Error: i % 7 === 3 ? 'Endpoint ollama-gemma3-4b returned 500 after 3 attempts.' : null,
    StartedUtc: started,
    CompletedUtc: new Date(Date.parse(started) + ms).toISOString(),
    AnalysisMs: ms,
    QueueWaitMs: kind === 'Image' ? 0 : 640 + i * 11,
    MediaDurationMs: media,
    MediaBytes: kind === 'Image' ? 412000 : 24800000,
    RealtimeFactor: media ? ms / media : null,
    VisionEndpoint: kind === 'Audio' ? null : 'ollama-gemma3-4b',
    VisionModel: kind === 'Audio' ? null : 'gemma3:4b',
    TextEndpoint: kind === 'Image' ? null : 'ollama-gpt-oss-20b',
    TextModel: kind === 'Image' ? null : 'gpt-oss:20b',
    TranscriptionEndpoint: kind === 'Image' ? null : 'whisper',
    TranscriptionModel: kind === 'Image' ? null : 'Systran/faster-whisper-small',
    ModelCalls: kind === 'Video' ? 14 : kind === 'Audio' ? 3 : 2,
    FailedModelCalls: i % 7 === 3 ? 1 : 0,
    Retries: i % 7 === 3 ? 2 : 0,
    RepairAttempts: 0,
    PromptTokens: 1800,
    CompletionTokens: 410,
    TotalTokens: 2210,
    CachedTokens: 256,
    ReasoningTokens: 64,
    InputImages: kind === 'Image' ? 1 : kind === 'Video' ? 24 : 0,
    InputImageBytes: kind === 'Image' ? 210000 : 5040000,
    InputAudioBytes: kind === 'Image' ? 0 : 1900000,
    Scenes: kind === 'Video' ? 12 : 0,
    TranscriptCharacters: kind === 'Image' ? 0 : 5400,
    FieldCount: 3,
    InvalidFields: i % 5 === 0 ? 1 : 0,
    Stages: [
      { Name: 'Resolve', StartUtc: started, DurationMs: 120, Success: true },
      { Name: 'Normalize', StartUtc: new Date(Date.parse(started) + 120).toISOString(), DurationMs: 480, Success: true },
      { Name: 'Summarize', StartUtc: new Date(Date.parse(started) + 600).toISOString(), DurationMs: ms * 0.45, Success: true },
      { Name: 'Extract', StartUtc: new Date(Date.parse(started) + 600 + ms * 0.45).toISOString(), DurationMs: ms * 0.4, Success: i % 7 !== 3 }
    ]
  };
}

export function analysisDetail(id = 'kfa_1001') {
  const base = analysisRecord(1);
  const start = Date.parse(base.StartedUtc);
  const call = (n, purpose, offset, duration, extra = {}) => ({
    Id: 'kfc_' + n,
    AnalysisId: id,
    Purpose: purpose,
    Target: purpose.startsWith('Scene') ? 'scene ' + n : null,
    Endpoint: 'ollama-gemma3-4b',
    Provider: 'Ollama',
    Model: 'gemma3:4b',
    Attempt: 1,
    StartUtc: new Date(start + offset).toISOString(),
    Success: true,
    Error: null,
    StatusCode: 200,
    DurationMs: duration,
    QueueMs: 120,
    TimeToFirstTokenMs: duration * 0.22,
    GenerationMs: duration * 0.78,
    TokensPerSecond: 39.5,
    PromptTokens: 900,
    CompletionTokens: 210,
    TotalTokens: 1110,
    CachedTokens: 128,
    ReasoningTokens: 32,
    InputImages: 3,
    InputImageBytes: 630000,
    InputAudioBytes: 0,
    FinishReason: 'stop',
    ...extra
  });
  return {
    ...base,
    Id: id,
    Calls: [
      call(1, 'SceneSummarize', 700, 2400),
      call(2, 'SceneSummarize', 3300, 2600),
      call(3, 'Summarize', 6100, 9800, { Endpoint: 'ollama-gpt-oss-20b', Model: 'gpt-oss:20b' }),
      call(4, 'Extract', 16200, 5100, { Attempt: 1, Success: false, Error: 'Invalid JSON', StatusCode: 200 }),
      call(5, 'Extract', 21500, 4800, { Attempt: 2 })
    ]
  };
}

const CHECKED = '2026-11-15T17:59:00Z';
const OLLAMA_CAPS = { SupportsImages: true, SupportsStructuredOutput: true, SupportsTranscription: false, SupportsAudioInput: false };

export const ENDPOINTS = [
  { Name: 'ollama-gemma3-4b', Provider: 'Ollama', Kind: 'Completion', Model: 'gemma3:4b', Active: true, Healthy: true, LastCheckedUtc: CHECKED, ...OLLAMA_CAPS },
  { Name: 'ollama-qwen3-vl-8b', Provider: 'Ollama', Kind: 'Completion', Model: 'qwen3-vl:8b', Active: true, Healthy: true, LastCheckedUtc: CHECKED, ...OLLAMA_CAPS },
  { Name: 'ollama-gpt-oss-20b', Provider: 'Ollama', Kind: 'Completion', Model: 'gpt-oss:20b', Active: true, Healthy: true, LastCheckedUtc: CHECKED, ...OLLAMA_CAPS },
  { Name: 'llama-listen', Provider: 'OpenAiCompatible', Kind: 'Completion', Model: 'voxtral-mini-3b', Active: true, Healthy: true, LastCheckedUtc: CHECKED, SupportsImages: true, SupportsStructuredOutput: true, SupportsTranscription: true, SupportsAudioInput: true },
  { Name: 'whisper', Provider: 'OpenAiCompatible', Kind: 'Transcription', Model: 'Systran/faster-whisper-small', Active: true, Healthy: false, HealthMessage: 'Connection refused', LastCheckedUtc: CHECKED, SupportsImages: true, SupportsStructuredOutput: true, SupportsTranscription: true, SupportsAudioInput: true }
];

/** Settings response with a default listening endpoint, for views that read Inference.DefaultListening. */
export function settingsResponse(over = {}) {
  return {
    SettingsFile: '/app/keyframe.json',
    RestartRequired: false,
    Fields: [],
    Settings: {
      Inference: { DefaultVision: 'ollama-gemma3-4b', DefaultText: 'ollama-gpt-oss-20b', DefaultTranscription: 'whisper', DefaultListening: 'llama-listen', Endpoints: [], ...over },
      Media: { ListenFormat: 'Mp3', ListenSampleRate: 16000, ListenChannels: 1, ListenBitrateKbps: 64 }
    }
  };
}

export const OPENAPI = {
  openapi: '3.0.1',
  info: { title: 'Keyframe', version: '0.1.0' },
  tags: [{ name: 'Analysis' }, { name: 'Jobs' }, { name: 'Analytics' }, { name: 'System' }],
  paths: {
    '/v1.0/image': { post: { tags: ['Analysis'], operationId: 'AnalyzeImage', summary: 'Analyze an image', requestBody: { content: { 'application/json': { schema: { type: 'object', properties: { Image: { type: 'object' } } } } } } } },
    '/v1.0/video': { post: { tags: ['Analysis'], operationId: 'AnalyzeVideo', summary: 'Queue a video analysis job', requestBody: { content: { 'application/json': { schema: { type: 'object' } } } } } },
    '/v1.0/audio': { post: { tags: ['Analysis'], operationId: 'AnalyzeAudio', summary: 'Queue an audio analysis job', requestBody: { content: { 'application/json': { schema: { type: 'object' } } } } } },
    '/v1.0/segment': { post: { tags: ['Analysis'], operationId: 'GetSegment', summary: 'Cut a clip, audio, or frame from a source', requestBody: { content: { 'application/json': { schema: { type: 'object', properties: { StartMs: { type: 'integer' }, EndMs: { type: 'integer' } } } } } } } },
    '/v1.0/jobs': { get: { tags: ['Jobs'], operationId: 'ListJobs', summary: 'List jobs', parameters: [{ name: 'status', in: 'query', schema: { type: 'string', enum: ['Queued', 'Running', 'Succeeded', 'Failed'] } }, { name: 'pageSize', in: 'query', schema: { type: 'integer' } }] } },
    '/v1.0/jobs/{id}': {
      get: { tags: ['Jobs'], operationId: 'GetJob', summary: 'Get a job', parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'string' } }] },
      delete: { tags: ['Jobs'], operationId: 'DeleteJob', summary: 'Delete a job', parameters: [{ name: 'id', in: 'path', required: true, schema: { type: 'string' } }] }
    },
    '/v1.0/analytics/summary': { get: { tags: ['Analytics'], operationId: 'GetAnalyticsSummary', summary: 'Analytics summary', parameters: [{ name: 'fromUtc', in: 'query', schema: { type: 'string', format: 'date-time' } }, { name: 'mediaKind', in: 'query', schema: { type: 'string', enum: ['Image', 'Video', 'Audio'] } }] } },
    '/v1.0/health': { get: { tags: ['System'], operationId: 'GetHealth', summary: 'Server health' } }
  }
};

export function audioJob(status = 'Running', over = {}) {
  return {
    Id: 'kfj_900',
    Status: status,
    MediaKind: 'Audio',
    PrincipalName: 'admin',
    SourceUrl: 'https://media.example.com/talk.mp3',
    SizeBytes: 4200000,
    DurationMs: 182000,
    Progress: status === 'Succeeded' ? { Stage: 'Done', Completed: 1, Total: 1, Percent: 100 } : { Stage: 'Transcribe', Completed: 2, Total: 6, Percent: 38 },
    Error: null,
    CreatedUtc: '2026-11-15T18:00:00Z',
    StartedUtc: '2026-11-15T18:00:01Z',
    CompletedUtc: status === 'Succeeded' ? '2026-11-15T18:00:21Z' : null,
    ...over
  };
}

export function audioResult() {
  return {
    Success: true,
    RequestId: 'req_audio',
    MediaKind: 'Audio',
    Properties: { MediaKind: 'Audio', DurationMs: 182000, SizeBytes: 4200000, Container: 'mp3', AudioCodec: 'mp3', SampleRate: 44100, Channels: 2, BitRate: 192000 },
    Results: { Summary: 'Two speakers discuss the quarterly roadmap, agree to ship audio analysis first, and schedule a follow-up review.', speakerCount: 2 },
    Fields: [{ Key: 'speakerCount', Type: 'Integer', Value: 2, Valid: true, RawValue: '2', Error: null, Warnings: [] }],
    Telemetry: {
      StartUtc: '2026-11-15T18:00:01Z',
      TotalMs: 20100,
      QueueWaitMs: 850,
      Stages: [
        { Name: 'Resolve', StartUtc: '2026-11-15T18:00:01Z', DurationMs: 300, Success: true },
        { Name: 'AudioExtraction', StartUtc: '2026-11-15T18:00:01.300Z', DurationMs: 1400, Success: true },
        { Name: 'Transcribe', StartUtc: '2026-11-15T18:00:02.700Z', DurationMs: 12100, Success: true },
        { Name: 'Summarize', StartUtc: '2026-11-15T18:00:14.800Z', DurationMs: 5300, Success: true }
      ],
      ModelCalls: [
        { Purpose: 'Transcribe', Endpoint: 'whisper', Provider: 'OpenAiCompatible', Model: 'Systran/faster-whisper-small', StartUtc: '2026-11-15T18:00:02.700Z', DurationMs: 12000, Attempts: 1, Success: true, TotalTokens: 0, InputAudioBytes: 2100000 },
        { Purpose: 'Summarize', Endpoint: 'ollama-gpt-oss-20b', Provider: 'Ollama', Model: 'gpt-oss:20b', StartUtc: '2026-11-15T18:00:14.800Z', DurationMs: 5200, QueueMs: 90, TimeToFirstTokenMs: 900, GenerationMs: 4300, TokensPerSecond: 31.6, Attempts: 1, Success: true, PromptTokens: 1650, CompletionTokens: 136, TotalTokens: 1786 }
      ],
      Totals: { ModelMs: 17200, ModelCalls: 2, FailedModelCalls: 0, PromptTokens: 1650, CompletionTokens: 136, TotalTokens: 1786, InputImages: 0, InputImageBytes: 0 }
    },
    Transcript: {
      Language: 'en',
      Text: 'Welcome everyone. Let us review the roadmap.',
      Segments: [
        { StartMs: 0, EndMs: 4200, Text: 'Welcome everyone, thanks for joining the roadmap review.' },
        { StartMs: 4200, EndMs: 9800, Text: 'First item: audio analysis ships before the analytics page.' },
        { StartMs: 9800, EndMs: 15100, Text: 'Agreed. We will schedule a follow-up for the dashboard palettes.' }
      ]
    },
    Errors: [],
    Warnings: []
  };
}

/** Audio measurements shaped like AudioMeasurements. */
export function measurements(over = {}) {
  return {
    IntegratedLufs: -9.4,
    LoudnessRangeLu: 5.1,
    TruePeakDbtp: -0.2,
    PeakDbfs: -0.3,
    RmsDbfs: -11.8,
    CrestFactorDb: 11.5,
    PeakCount: 214,
    ClippingLikely: true,
    SilenceRatio: 0.03,
    SilenceCount: 2,
    LeadingSilenceMs: 400,
    TrailingSilenceMs: 2100,
    Channels: 2,
    StereoWidthDb: -14.2,
    EffectivelyMono: false,
    ...over
  };
}

/** ListeningInfo for a listened analysis; pass Mode/Reason overrides for fallbacks. */
export function listening(over = {}) {
  return {
    Listened: true,
    Mode: 'Sections',
    Reason: null,
    Endpoint: 'llama-listen',
    Model: 'voxtral-mini-3b',
    Rendition: { Format: 'Mp3', SampleRate: 16000, Channels: 1, BitrateKbps: 64 },
    DurationMs: 182000,
    HeardMs: 150000,
    Clips: [
      { Scope: 'Section', Index: 0, StartMs: 0, EndMs: 60000, SizeBytes: 480000 },
      { Scope: 'Section', Index: 1, StartMs: 60000, EndMs: 120000, SizeBytes: 480000 },
      { Scope: 'Section', Index: 2, StartMs: 136000, EndMs: 166000, SizeBytes: 240000 }
    ],
    ...over
  };
}

/** A transcript-only fallback: the listening endpoint was unhealthy. */
export function fallbackListening(over = {}) {
  return listening({ Listened: false, Mode: 'None', Reason: 'EndpointUnhealthy', Endpoint: null, Model: null, HeardMs: 0, Clips: [], ...over });
}

/** Audio result with listening, measurements, and three sections (the last one failed). */
export function listeningResult(over = {}) {
  return {
    ...audioResult(),
    Results: { Summary: 'A live set: crowd noise, a rock band, and a long instrumental outro.', recording: 'Live' },
    Fields: [{ Key: 'recording', Type: 'Enum', Value: 'Live', Valid: true, RawValue: 'Live', Error: null, Warnings: [] }],
    Listening: listening(),
    Measurements: measurements(),
    Sections: [
      { Index: 0, StartMs: 0, EndMs: 60000, DurationMs: 60000, TranscriptText: 'Good evening, everybody!', Results: { Summary: 'Crowd cheering, then the band starts.' }, Fields: [], Errors: [] },
      { Index: 1, StartMs: 60000, EndMs: 120000, DurationMs: 60000, TranscriptText: 'This next one is an old favorite.', Results: { Summary: 'A mid-tempo song with a sing-along chorus.' }, Fields: [], Errors: [] },
      { Index: 2, StartMs: 120000, EndMs: 182000, DurationMs: 62000, TranscriptText: null, Results: {}, Fields: [], Errors: ['Section 3 summary failed: endpoint llama-listen timed out.'] }
    ],
    ...over
  };
}
