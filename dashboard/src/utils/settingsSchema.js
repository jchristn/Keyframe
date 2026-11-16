// Form schema for KeyframeSettings (Keyframe.Core/Settings/*.cs). Ranges mirror the
// server's Math.Clamp bounds so the UI never offers a value the server would silently change.
// Labels and help are i18n keys under settings.fields.<path>.

export const PROVIDERS = ['Mock', 'Ollama', 'OllamaCloud', 'OpenAi', 'OpenAiCompatible', 'AzureOpenAi', 'Anthropic', 'Gemini', 'VertexAi', 'Bedrock', 'XAi', 'Cohere'];
export const ENDPOINT_KINDS = ['Completion', 'Transcription'];
export const DATABASE_TYPES = ['Sqlite', 'Mysql', 'Postgresql', 'SqlServer'];
export const OTLP_PROTOCOLS = ['grpc', 'httpprotobuf'];

export const SETTINGS_SECTIONS = [
  {
    id: 'rest',
    key: 'Rest',
    fields: [
      { path: 'Rest.Hostname', type: 'text', required: true },
      { path: 'Rest.Port', type: 'number', min: 1, max: 65535 },
      { path: 'Rest.Ssl', type: 'bool' },
      { path: 'Rest.MaxRequestBodyBytes', type: 'number', min: 1024, max: 4294967296, unit: 'bytes' }
    ]
  },
  {
    id: 'auth',
    key: 'Auth',
    fields: [
      { path: 'Auth.RequireAuthentication', type: 'bool' },
      { path: 'Auth.AdminApiKey', type: 'secret', required: true },
      { path: 'Auth.MaxFailedAttemptsPerMinute', type: 'number', min: 1, max: 100000 }
    ],
    arrays: [
      {
        path: 'Auth.AccessKeys',
        itemFields: [
          { name: 'Name', type: 'text', required: true },
          { name: 'Key', type: 'secret', required: true },
          { name: 'Active', type: 'bool' }
        ],
        newItem: () => ({ Name: '', Key: '', Active: true })
      }
    ]
  },
  {
    id: 'database',
    key: 'Database',
    fields: [
      { path: 'Database.Type', type: 'select', options: DATABASE_TYPES },
      { path: 'Database.Filename', type: 'text', required: true, showIf: (s) => s.Database.Type === 'Sqlite' },
      { path: 'Database.Hostname', type: 'nullableText', showIf: (s) => s.Database.Type !== 'Sqlite' },
      { path: 'Database.Port', type: 'number', min: 0, max: 65535, showIf: (s) => s.Database.Type !== 'Sqlite' },
      { path: 'Database.DatabaseName', type: 'nullableText', showIf: (s) => s.Database.Type !== 'Sqlite' },
      { path: 'Database.Username', type: 'nullableText', showIf: (s) => s.Database.Type !== 'Sqlite' },
      { path: 'Database.Password', type: 'secret', nullable: true, showIf: (s) => s.Database.Type !== 'Sqlite' },
      { path: 'Database.TrustServerCertificate', type: 'bool', showIf: (s) => s.Database.Type !== 'Sqlite' },
      { path: 'Database.CommandTimeoutSeconds', type: 'number', min: 1, max: 3600 }
    ]
  },
  {
    id: 'inference',
    key: 'Inference',
    fields: [
      { path: 'Inference.DefaultVision', type: 'endpointRef', kind: 'Completion' },
      { path: 'Inference.DefaultText', type: 'endpointRef', kind: 'Completion' },
      { path: 'Inference.DefaultTranscription', type: 'endpointRef', kind: 'Transcription' },
      { path: 'Inference.MaxRetries', type: 'number', min: 0, max: 10 },
      { path: 'Inference.MaxRepairAttempts', type: 'number', min: 0, max: 5 },
      { path: 'Inference.RetryBackoffMs', type: 'number', min: 0, max: 60000, unit: 'ms' },
      { path: 'Inference.RequestTimeoutMs', type: 'number', min: 1000, max: 3600000, unit: 'ms' },
      { path: 'Inference.MaxTranscriptCharacters', type: 'number', min: 1000, max: 2000000 }
    ],
    arrays: [
      {
        path: 'Inference.Endpoints',
        titleField: 'Name',
        itemFields: [
          { name: 'Name', type: 'text', required: true, maxLength: 64 },
          { name: 'Provider', type: 'select', options: PROVIDERS },
          { name: 'Kind', type: 'select', options: ENDPOINT_KINDS },
          { name: 'Endpoint', type: 'nullableText', wide: true },
          { name: 'ApiKey', type: 'secret', nullable: true },
          { name: 'Model', type: 'nullableText' },
          { name: 'MaxConcurrentRequests', type: 'number', min: 1, max: 256 },
          { name: 'TimeoutMs', type: 'number', min: 1000, max: 3600000, unit: 'ms' },
          { name: 'Temperature', type: 'nullableNumber', min: 0, max: 2, step: 0.1 },
          { name: 'MaxOutputTokens', type: 'number', min: 16, max: 1000000 },
          { name: 'Active', type: 'bool' }
        ],
        newItem: () => ({
          Name: '',
          Provider: 'Ollama',
          Kind: 'Completion',
          Endpoint: 'http://127.0.0.1:11434',
          ApiKey: null,
          Model: '',
          MaxConcurrentRequests: 2,
          TimeoutMs: 120000,
          Temperature: null,
          MaxOutputTokens: 2048,
          Active: true
        })
      }
    ]
  },
  {
    id: 'media',
    key: 'Media',
    fields: [
      { path: 'Media.TempDirectory', type: 'text', required: true },
      { path: 'Media.FfmpegPath', type: 'text', required: true },
      { path: 'Media.FfprobePath', type: 'text', required: true },
      { path: 'Media.MaxDownloadBytes', type: 'number', min: 1024, max: 107374182400, unit: 'bytes' },
      { path: 'Media.DownloadTimeoutMs', type: 'number', min: 1000, max: 7200000, unit: 'ms' },
      { path: 'Media.AllowPrivateNetworkUrls', type: 'bool' },
      { path: 'Media.ImageMaxEdge', type: 'number', min: 64, max: 8192 },
      { path: 'Media.ThumbnailMaxEdge', type: 'number', min: 32, max: 2048 },
      { path: 'Media.JpegQuality', type: 'number', min: 1, max: 31 },
      { path: 'Media.MaxParallelFfmpeg', type: 'number', min: 1, max: 64 },
      { path: 'Media.FfmpegTimeoutMs', type: 'number', min: 1000, max: 14400000, unit: 'ms' },
      { path: 'Media.MaxVideoDurationMs', type: 'number', min: 1000, max: 86400000, unit: 'ms' },
      { path: 'Media.TranscriptionChunkSeconds', type: 'number', min: 10, max: 7200 }
    ]
  },
  {
    id: 'jobs',
    key: 'Jobs',
    fields: [
      { path: 'Jobs.MaxConcurrentJobs', type: 'number', min: 1, max: 64 },
      { path: 'Jobs.MaxQueuedJobs', type: 'number', min: 1, max: 100000 },
      { path: 'Jobs.RetentionDays', type: 'number', min: 1, max: 3650 },
      { path: 'Jobs.MaxWaitTimeoutMs', type: 'number', min: 1000, max: 3600000, unit: 'ms' }
    ]
  },
  {
    id: 'requestHistory',
    key: 'RequestHistory',
    fields: [
      { path: 'RequestHistory.Enabled', type: 'bool' },
      { path: 'RequestHistory.MaxRequestBodyBytes', type: 'number', min: 0, max: 1048576, unit: 'bytes' },
      { path: 'RequestHistory.MaxResponseBodyBytes', type: 'number', min: 0, max: 1048576, unit: 'bytes' },
      { path: 'RequestHistory.RetentionDays', type: 'number', min: 1, max: 3650 }
    ]
  },
  {
    id: 'logging',
    key: 'Logging',
    fields: [
      { path: 'Logging.ConsoleLogging', type: 'bool' },
      { path: 'Logging.EnableColors', type: 'bool' },
      { path: 'Logging.LogDirectory', type: 'nullableText' },
      { path: 'Logging.LogFilename', type: 'nullableText' },
      { path: 'Logging.MinimumSeverity', type: 'select', options: [0, 1, 2, 3, 4, 5, 6, 7], optionLabel: 'severity' }
    ],
    arrays: [
      {
        path: 'Logging.Servers',
        itemFields: [
          { name: 'Hostname', type: 'text', required: true },
          { name: 'Port', type: 'number', min: 1, max: 65535 }
        ],
        newItem: () => ({ Hostname: '127.0.0.1', Port: 514 })
      }
    ]
  },
  {
    id: 'telemetry',
    key: 'Telemetry',
    fields: [
      { path: 'Telemetry.Enable', type: 'bool' },
      { path: 'Telemetry.ServiceName', type: 'text', required: true },
      { path: 'Telemetry.OtlpEnable', type: 'bool' },
      { path: 'Telemetry.OtlpEndpoint', type: 'text', required: true },
      { path: 'Telemetry.OtlpProtocol', type: 'select', options: OTLP_PROTOCOLS },
      { path: 'Telemetry.PrometheusEnable', type: 'bool' },
      { path: 'Telemetry.PrometheusHostname', type: 'text' },
      { path: 'Telemetry.PrometheusPort', type: 'number', min: 1, max: 65535 },
      { path: 'Telemetry.PrometheusPath', type: 'text' },
      { path: 'Telemetry.LokiEnable', type: 'bool' },
      { path: 'Telemetry.LokiEndpoint', type: 'text' },
      { path: 'Telemetry.ExportLogs', type: 'bool' },
      { path: 'Telemetry.MetricExportIntervalMs', type: 'number', min: 1000, max: 300000, unit: 'ms' },
      { path: 'Telemetry.TraceSamplingRatio', type: 'number', min: 0, max: 1, step: 0.05, float: true }
    ]
  }
];

export function getPath(obj, path) {
  return path.split('.').reduce((o, k) => (o === null || o === undefined ? undefined : o[k]), obj);
}

export function setPath(obj, path, value) {
  const parts = path.split('.');
  const copy = Array.isArray(obj) ? obj.slice() : { ...obj };
  let cursor = copy;
  for (let i = 0; i < parts.length - 1; i += 1) {
    const k = parts[i];
    cursor[k] = Array.isArray(cursor[k]) ? cursor[k].slice() : { ...(cursor[k] || {}) };
    cursor = cursor[k];
  }
  cursor[parts[parts.length - 1]] = value;
  return copy;
}

export function isMasked(value) {
  return typeof value === 'string' && value.startsWith('****');
}

/** Client-side checks that mirror the server's guard clauses. Returns { path: messageKey }. */
export function validateSettings(settings) {
  const errors = {};
  const need = (path, value) => {
    if (value === null || value === undefined || String(value).trim() === '') errors[path] = 'settings.errors.required';
  };
  for (const section of SETTINGS_SECTIONS) {
    for (const f of section.fields) {
      if (f.showIf && !f.showIf(settings)) continue;
      const v = getPath(settings, f.path);
      if (f.required) need(f.path, v);
      if (f.type === 'number' && (v === '' || v === null || !Number.isFinite(Number(v)))) errors[f.path] = 'settings.errors.number';
    }
    for (const arr of section.arrays || []) {
      const items = getPath(settings, arr.path) || [];
      const names = new Set();
      items.forEach((item, i) => {
        for (const f of arr.itemFields) {
          const p = arr.path + '.' + i + '.' + f.name;
          if (f.required) need(p, item[f.name]);
          if (f.type === 'number' && !Number.isFinite(Number(item[f.name]))) errors[p] = 'settings.errors.number';
        }
        if (item.Name) {
          const lower = String(item.Name).toLowerCase();
          if (names.has(lower)) errors[arr.path + '.' + i + '.Name'] = 'settings.errors.duplicateName';
          names.add(lower);
        }
      });
    }
  }
  return errors;
}

/** Coerce numeric strings back to numbers and clamp to the declared range before sending. */
export function normalizeForSave(settings) {
  let out = settings;
  const clamp = (f, v) => {
    if (v === '' || v === null || v === undefined) return f.type === 'nullableNumber' ? null : v;
    let n = Number(v);
    if (!Number.isFinite(n)) return v;
    if (f.min !== undefined) n = Math.max(f.min, n);
    if (f.max !== undefined) n = Math.min(f.max, n);
    return f.float || f.step ? n : Math.trunc(n);
  };
  for (const section of SETTINGS_SECTIONS) {
    for (const f of section.fields) {
      if (f.type === 'number' || f.type === 'nullableNumber') out = setPath(out, f.path, clamp(f, getPath(out, f.path)));
      if (f.type === 'select' && typeof f.options[0] === 'number') out = setPath(out, f.path, Number(getPath(out, f.path)));
      if (f.type === 'nullableText' || f.type === 'endpointRef') {
        const v = getPath(out, f.path);
        if (v === '') out = setPath(out, f.path, null);
      }
    }
    for (const arr of section.arrays || []) {
      const items = (getPath(out, arr.path) || []).map((item) => {
        const copy = { ...item };
        for (const f of arr.itemFields) {
          if (f.type === 'number' || f.type === 'nullableNumber') copy[f.name] = clamp(f, copy[f.name]);
          if ((f.type === 'nullableText' || (f.type === 'secret' && f.nullable)) && copy[f.name] === '') copy[f.name] = null;
        }
        return copy;
      });
      out = setPath(out, arr.path, items);
    }
  }
  return out;
}
