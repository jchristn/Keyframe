// Keyframe JavaScript SDK. ESM, Node 20+ and modern browsers, built on fetch.
// Wire models are PascalCase JSON exactly as the server sends them; see index.d.ts for types.
// The API key is held in a private field and is never logged or exposed.

/** SDK version. */
export const VERSION = '0.1.0';

/** Default server base URL. */
export const DEFAULT_BASE_URL = 'http://127.0.0.1:8700';

const TERMINAL = new Set(['Succeeded', 'Failed', 'Canceled', 'Interrupted']);

/**
 * Whether a job status is terminal (Succeeded, Failed, Canceled, or Interrupted).
 * @param {string} status
 * @returns {boolean}
 */
export function isTerminalStatus(status) {
  return TERMINAL.has(status);
}

/** Thrown when the server answers with a non-2xx status code. */
export class KeyframeApiError extends Error {
  /**
   * @param {string} method
   * @param {string} path
   * @param {number} statusCode
   * @param {object|null} apiError Parsed ApiErrorResponse, or null.
   * @param {string} responseBody
   * @param {string|null} requestId
   */
  constructor(method, path, statusCode, apiError, responseBody, requestId) {
    let message = `${method} ${path} returned ${statusCode}`;
    if (apiError) {
      message += ` ${apiError.Error}: ${apiError.Message}`;
      if (Array.isArray(apiError.Details) && apiError.Details.length > 0) message += ` (${apiError.Details.join('; ')})`;
    } else {
      message += '.';
    }
    super(message);
    this.name = 'KeyframeApiError';
    this.method = method;
    this.path = path;
    this.statusCode = statusCode;
    this.apiError = apiError;
    this.error = apiError ? apiError.Error : null;
    this.details = apiError && Array.isArray(apiError.Details) ? apiError.Details : [];
    this.requestId = (apiError && apiError.RequestId) || requestId || null;
    this.responseBody = responseBody;
  }
}

/** Thrown by wait helpers when a job ends Failed, Canceled, or Interrupted. */
export class KeyframeJobError extends Error {
  /** @param {object} job JobResponse in its terminal state. */
  constructor(job) {
    super(`Job ${job.Id} finished with status ${job.Status}${job.Error ? ': ' + job.Error : '.'}`);
    this.name = 'KeyframeJobError';
    this.job = job;
  }
}

/** Thrown when a request or a wait exceeds its time limit. */
export class KeyframeTimeoutError extends Error {
  /** @param {string} message */
  constructor(message) {
    super(message);
    this.name = 'KeyframeTimeoutError';
  }
}

/** Thrown when the server cannot be reached (DNS, refused connection, reset). */
export class KeyframeConnectionError extends Error {
  /**
   * @param {string} message
   * @param {unknown} cause
   */
  constructor(message, cause) {
    super(message, { cause });
    this.name = 'KeyframeConnectionError';
  }
}

/**
 * Media referenced by URL.
 * @param {string} url Absolute http or https URL.
 * @param {string} [mediaType]
 * @returns {{Url: string, MediaType?: string}}
 */
export function mediaFromUrl(url, mediaType) {
  let parsed;
  try {
    parsed = new URL(url);
  } catch {
    throw new TypeError('URL must be an absolute http or https URL.');
  }
  if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') throw new TypeError('URL must be an absolute http or https URL.');
  const source = { Url: url };
  if (mediaType) source.MediaType = mediaType;
  return source;
}

/**
 * Media sent inline as base64.
 * @param {Uint8Array|ArrayBuffer} bytes
 * @param {string} [mediaType]
 * @returns {{Data: string, MediaType?: string}}
 */
export function mediaFromBytes(bytes, mediaType) {
  const view = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  if (view.length < 1) throw new TypeError('Media data is empty.');
  const source = { Data: toBase64(view) };
  if (mediaType) source.MediaType = mediaType;
  return source;
}

/**
 * Guess a MIME type from a file name. Returns undefined for unknown extensions.
 * @param {string} name
 * @returns {string|undefined}
 */
export function guessMediaType(name) {
  const match = /\.([a-z0-9]+)$/i.exec(name || '');
  if (!match) return undefined;
  const types = {
    jpg: 'image/jpeg', jpeg: 'image/jpeg', png: 'image/png', gif: 'image/gif', webp: 'image/webp', bmp: 'image/bmp',
    tif: 'image/tiff', tiff: 'image/tiff', mp4: 'video/mp4', m4v: 'video/mp4', mov: 'video/quicktime',
    webm: 'video/webm', mkv: 'video/x-matroska', avi: 'video/x-msvideo'
  };
  return types[match[1].toLowerCase()];
}

/**
 * Read a local file (Node only) into an inline media source.
 * @param {string} path
 * @param {string} [mediaType] Defaults to a guess from the extension.
 * @returns {Promise<{Data: string, MediaType?: string}>}
 */
export async function mediaFromFile(path, mediaType) {
  const specifier = 'node:fs/promises';
  const fs = await import(/* @vite-ignore */ /* webpackIgnore: true */ specifier);
  const bytes = await fs.readFile(path);
  return mediaFromBytes(new Uint8Array(bytes.buffer, bytes.byteOffset, bytes.byteLength), mediaType || guessMediaType(path));
}

function toBase64(view) {
  if (typeof Buffer !== 'undefined') return Buffer.from(view.buffer, view.byteOffset, view.byteLength).toString('base64');
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < view.length; i += chunk) binary += String.fromCharCode.apply(null, view.subarray(i, i + chunk));
  return btoa(binary);
}

function buildQuery(values) {
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(values)) {
    if (value === undefined || value === null || value === '') continue;
    params.append(key, value instanceof Date ? value.toISOString() : String(value));
  }
  const text = params.toString();
  return text ? '?' + text : '';
}

function segment(value, name) {
  if (typeof value !== 'string' || value.trim() === '') throw new TypeError(`${name} is required.`);
  return encodeURIComponent(value);
}

function sleep(ms, signal) {
  return new Promise((resolve, reject) => {
    if (signal && signal.aborted) {
      reject(signal.reason);
      return;
    }
    const timer = setTimeout(() => {
      if (signal) signal.removeEventListener('abort', onAbort);
      resolve();
    }, ms);
    function onAbort() {
      clearTimeout(timer);
      reject(signal.reason);
    }
    if (signal) signal.addEventListener('abort', onAbort, { once: true });
  });
}

function anySignal(signals) {
  if (typeof AbortSignal.any === 'function') return AbortSignal.any(signals);
  const controller = new AbortController();
  for (const s of signals) {
    if (s.aborted) {
      controller.abort(s.reason);
      break;
    }
    s.addEventListener('abort', () => controller.abort(s.reason), { once: true });
  }
  return controller.signal;
}

function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

/**
 * Client for the Keyframe REST API.
 */
export class KeyframeClient {
  #apiKey;

  /**
   * @param {object} [options]
   * @param {string} [options.baseUrl] Default http://127.0.0.1:8700.
   * @param {string} [options.apiKey] Access key or admin key.
   * @param {'bearer'|'x-api-key'} [options.authHeader] Default 'bearer'.
   * @param {number} [options.timeoutMs] Per-request timeout. Default 300000, clamped 1000 to 3600000.
   * @param {typeof fetch} [options.fetch] Custom fetch implementation.
   */
  constructor(options = {}) {
    const baseUrl = (options.baseUrl || DEFAULT_BASE_URL).trim();
    let parsed;
    try {
      parsed = new URL(baseUrl);
    } catch {
      throw new TypeError('baseUrl must be an absolute http or https URL.');
    }
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') throw new TypeError('baseUrl must be an absolute http or https URL.');

    this.baseUrl = baseUrl.replace(/\/+$/, '');
    this.#apiKey = options.apiKey || null;
    this.authHeader = options.authHeader === 'x-api-key' ? 'x-api-key' : 'bearer';
    this.timeoutMs = clamp(options.timeoutMs ?? 300000, 1000, 3600000);
    this._fetch = options.fetch || globalThis.fetch.bind(globalThis);

    this.health = new HealthClient(this);
    this.whoami = new WhoAmIClient(this);
    this.image = new ImageClient(this);
    this.video = new VideoClient(this);
    this.probe = new ProbeClient(this);
    this.jobs = new JobsClient(this);
    this.endpoints = new EndpointsClient(this);
    this.requestHistory = new RequestHistoryClient(this);
    this.settings = new SettingsClient(this);
  }

  /** Whether an API key is configured. The key itself is not readable. */
  get hasApiKey() {
    return !!this.#apiKey;
  }

  /**
   * Replace the API key. Empty or null clears it.
   * @param {string|null} apiKey
   */
  setApiKey(apiKey) {
    this.#apiKey = apiKey || null;
  }

  /**
   * Analyze a local image file (Node only).
   * @param {string} path File path.
   * @param {object} [request] Rest of the ImageAnalysisRequest (Context, Functions, Model, IncludeProperties).
   * @param {{signal?: AbortSignal}} [options]
   * @returns {Promise<object>} ImageAnalysisResponse.
   */
  async analyzeImageFile(path, request = {}, options = {}) {
    const image = await mediaFromFile(path);
    return this.image.analyze({ ...request, Image: image }, options);
  }

  /**
   * Send a request and return the raw outcome without throwing on error status codes.
   * @param {string} method
   * @param {string} path Path relative to the base URL, optionally with a query string.
   * @param {object} [body] Serialized as JSON.
   * @param {{signal?: AbortSignal}} [options]
   * @returns {Promise<{statusCode: number, body: string, headers: Headers, requestId: string|null}>}
   */
  async sendRaw(method, path, body, options = {}) {
    const url = this.baseUrl + (path.startsWith('/') ? path : '/' + path);
    const headers = { Accept: 'application/json' };
    if (this.#apiKey) {
      if (this.authHeader === 'x-api-key') headers['x-api-key'] = this.#apiKey;
      else headers.Authorization = 'Bearer ' + this.#apiKey;
    }
    let payload;
    if (body !== undefined && body !== null) {
      headers['Content-Type'] = 'application/json';
      payload = JSON.stringify(body);
    }

    const timeout = AbortSignal.timeout(this.timeoutMs);
    const signal = options.signal ? anySignal([options.signal, timeout]) : timeout;
    let response;
    try {
      response = await this._fetch(url, { method, headers, body: payload, signal });
    } catch (err) {
      if (options.signal && options.signal.aborted) throw err;
      if (timeout.aborted) throw new KeyframeTimeoutError(`${method} ${path} did not complete within ${this.timeoutMs}ms.`);
      throw new KeyframeConnectionError(`${method} ${path} failed: unable to reach ${this.baseUrl}.`, err);
    }

    let text;
    try {
      text = await response.text();
    } catch (err) {
      if (options.signal && options.signal.aborted) throw err;
      if (timeout.aborted) throw new KeyframeTimeoutError(`${method} ${path} did not complete within ${this.timeoutMs}ms.`);
      throw new KeyframeConnectionError(`${method} ${path} failed while reading the response.`, err);
    }
    return { statusCode: response.status, body: text, headers: response.headers, requestId: response.headers.get('x-request-id') };
  }

  /** @internal */
  async _checked(method, path, body, options) {
    const raw = await this.sendRaw(method, path, body, options);
    if (raw.statusCode < 200 || raw.statusCode > 299) {
      let apiError = null;
      if (raw.body) {
        try {
          apiError = JSON.parse(raw.body);
        } catch {
          apiError = null;
        }
      }
      throw new KeyframeApiError(method, path, raw.statusCode, apiError, raw.body, raw.requestId);
    }
    return raw;
  }

  /** @internal */
  async _json(method, path, body, options) {
    const raw = await this._checked(method, path, body, options);
    if (!raw.body) throw new KeyframeApiError(method, path, raw.statusCode, null, raw.body, raw.requestId);
    return JSON.parse(raw.body);
  }

  /** Returns the base URL and whether a key is set. Never includes the key. */
  toString() {
    return `KeyframeClient ${this.baseUrl} (${this.hasApiKey ? 'key set' : 'no key'})`;
  }

  /** Keeps the key out of console.log and util.inspect output in Node. */
  [Symbol.for('nodejs.util.inspect.custom')]() {
    return this.toString();
  }

  /** Keeps the key out of JSON.stringify output. */
  toJSON() {
    return { baseUrl: this.baseUrl, hasApiKey: this.hasApiKey, authHeader: this.authHeader, timeoutMs: this.timeoutMs };
  }
}

class HealthClient {
  constructor(client) { this._c = client; }

  /** GET /v1.0/health (anonymous). */
  get(options) {
    return this._c._json('GET', '/v1.0/health', undefined, options);
  }

  /** HEAD /. Resolves true when the server answers 200; never throws for HTTP or connection errors. */
  async isAlive(options) {
    try {
      const raw = await this._c.sendRaw('HEAD', '/', undefined, options);
      return raw.statusCode === 200;
    } catch (err) {
      if (err instanceof KeyframeConnectionError || err instanceof KeyframeTimeoutError) return false;
      throw err;
    }
  }
}

class WhoAmIClient {
  constructor(client) { this._c = client; }

  /** GET /v1.0/whoami. */
  get(options) {
    return this._c._json('GET', '/v1.0/whoami', undefined, options);
  }
}

class ImageClient {
  constructor(client) { this._c = client; }

  /** POST /v1.0/image. */
  analyze(request, options) {
    if (!request || typeof request !== 'object') throw new TypeError('request is required.');
    return this._c._json('POST', '/v1.0/image', request, options);
  }
}

class ProbeClient {
  constructor(client) { this._c = client; }

  /**
   * POST /v1.0/probe. Accepts a ProbeRequest ({Media}) or a bare MediaSource ({Url} or {Data}).
   */
  probe(requestOrMedia, options) {
    if (!requestOrMedia || typeof requestOrMedia !== 'object') throw new TypeError('request is required.');
    const body = 'Media' in requestOrMedia ? requestOrMedia : { Media: requestOrMedia };
    return this._c._json('POST', '/v1.0/probe', body, options);
  }
}

class VideoClient {
  constructor(client) { this._c = client; }

  /**
   * POST /v1.0/video. Resolves {statusCode, jobId, job, result}: exactly one of job or result is set.
   */
  async analyze(request, options) {
    if (!request || typeof request !== 'object') throw new TypeError('request is required.');
    const raw = await this._c._checked('POST', '/v1.0/video', request, options);
    const body = JSON.parse(raw.body);
    if (raw.statusCode === 202 || typeof body.Status === 'string') {
      return { statusCode: raw.statusCode, jobId: body.Id, job: body, result: null };
    }
    return { statusCode: raw.statusCode, jobId: raw.headers.get('x-keyframe-job-id') || '', job: null, result: body };
  }

  /**
   * Submit, poll until terminal, then fetch the result. Rejects with KeyframeJobError when the job does not succeed.
   * @param {object} request
   * @param {{pollIntervalMs?: number, timeoutMs?: number, signal?: AbortSignal}} [options]
   */
  async analyzeAndWait(request, options = {}) {
    const submitted = await this.analyze(request, options);
    if (submitted.result) return submitted.result;
    const job = isTerminalStatus(submitted.job.Status) ? submitted.job : await this._c.jobs.wait(submitted.jobId, options);
    if (job.Status !== 'Succeeded') throw new KeyframeJobError(job);
    return this._c.jobs.getResult(job.Id, options);
  }
}

class JobsClient {
  constructor(client) {
    this._c = client;
    /** Default polling interval for wait(). Default 1000, clamped 100 to 60000. */
    this.defaultPollIntervalMs = 1000;
    /** Default overall wait limit for wait(). Default 600000, clamped 1000 to 86400000. */
    this.defaultWaitTimeoutMs = 600000;
  }

  /** GET /v1.0/jobs with optional {status, mediaKind, principalName, fromUtc, toUtc, pageNumber, pageSize}. */
  list(filter = {}, options) {
    const query = buildQuery({
      status: filter.status, mediaKind: filter.mediaKind, principalName: filter.principalName,
      fromUtc: filter.fromUtc, toUtc: filter.toUtc, pageNumber: filter.pageNumber, pageSize: filter.pageSize
    });
    return this._c._json('GET', '/v1.0/jobs' + query, undefined, options);
  }

  /** GET /v1.0/jobs/{id}. */
  get(id, options) {
    return this._c._json('GET', '/v1.0/jobs/' + segment(id, 'id'), undefined, options);
  }

  /** GET /v1.0/jobs/{id}/result. 409 until the job succeeded. */
  getResult(id, options) {
    return this._c._json('GET', '/v1.0/jobs/' + segment(id, 'id') + '/result', undefined, options);
  }

  /** POST /v1.0/jobs/{id}/cancel. 409 when already finished. */
  cancel(id, options) {
    return this._c._json('POST', '/v1.0/jobs/' + segment(id, 'id') + '/cancel', undefined, options);
  }

  /** DELETE /v1.0/jobs/{id}. 409 while active. */
  async delete(id, options) {
    await this._c._checked('DELETE', '/v1.0/jobs/' + segment(id, 'id'), undefined, options);
  }

  /**
   * Poll until the job is terminal and resolve with it.
   * @param {string} id
   * @param {{pollIntervalMs?: number, timeoutMs?: number, signal?: AbortSignal}} [options]
   */
  async wait(id, options = {}) {
    segment(id, 'id');
    const interval = clamp(options.pollIntervalMs ?? this.defaultPollIntervalMs, 100, 60000);
    const limit = clamp(options.timeoutMs ?? this.defaultWaitTimeoutMs, 1000, 86400000);
    const deadline = Date.now() + limit;
    for (;;) {
      const job = await this.get(id, options);
      if (isTerminalStatus(job.Status)) return job;
      if (Date.now() >= deadline) throw new KeyframeTimeoutError(`Job ${id} is still ${job.Status} after ${limit}ms.`);
      await sleep(interval, options.signal);
    }
  }
}

class EndpointsClient {
  constructor(client) { this._c = client; }

  /** GET /v1.0/endpoints. */
  list(options) {
    return this._c._json('GET', '/v1.0/endpoints', undefined, options);
  }

  /** POST /v1.0/endpoints/{name}/test (admin). */
  test(name, options) {
    return this._c._json('POST', '/v1.0/endpoints/' + segment(name, 'name') + '/test', undefined, options);
  }
}

const HISTORY = '/v1.0/api/request-history';

function historyFilters(filter) {
  return {
    method: filter.method, statusCode: filter.statusCode, pathContains: filter.pathContains,
    principalName: filter.principalName, fromUtc: filter.fromUtc, toUtc: filter.toUtc
  };
}

class RequestHistoryClient {
  constructor(client) { this._c = client; }

  /** GET /v1.0/api/request-history (bodies omitted). */
  list(filter = {}, options) {
    const query = buildQuery({ ...historyFilters(filter), pageNumber: filter.pageNumber, pageSize: filter.pageSize });
    return this._c._json('GET', HISTORY + query, undefined, options);
  }

  /** GET /v1.0/api/request-history/summary. */
  summary(filter = {}, options) {
    const query = buildQuery({ ...historyFilters(filter), bucketMinutes: filter.bucketMinutes });
    return this._c._json('GET', HISTORY + '/summary' + query, undefined, options);
  }

  /** GET /v1.0/api/request-history/{id}. */
  get(id, options) {
    return this._c._json('GET', HISTORY + '/' + segment(id, 'id'), undefined, options);
  }

  /** DELETE /v1.0/api/request-history/{id} (admin). */
  async delete(id, options) {
    await this._c._checked('DELETE', HISTORY + '/' + segment(id, 'id'), undefined, options);
  }

  /** DELETE /v1.0/api/request-history with a filter (admin). An empty filter deletes everything. */
  deleteMany(filter = {}, options) {
    return this._c._json('DELETE', HISTORY + buildQuery(historyFilters(filter)), undefined, options);
  }
}

class SettingsClient {
  constructor(client) { this._c = client; }

  /** GET /v1.0/settings (admin). Secrets are masked. */
  get(options) {
    return this._c._json('GET', '/v1.0/settings', undefined, options);
  }

  /** PUT /v1.0/settings (admin). Send the complete object; masked secrets sent back unchanged are kept. */
  update(settings, options) {
    if (!settings || typeof settings !== 'object') throw new TypeError('settings is required.');
    return this._c._json('PUT', '/v1.0/settings', settings, options);
  }

  /** POST /v1.0/restart (admin). */
  restart(options) {
    return this._c._json('POST', '/v1.0/restart', undefined, options);
  }
}

export default KeyframeClient;
