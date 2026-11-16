/**
 * Shared Keyframe API client. Every dashboard call goes through this class; views call
 * named methods rather than building URLs. The fetch core normalizes errors into ApiError
 * and paged results into { items, pageNumber, pageSize, totalCount, totalPages }.
 *
 * The API key is held only in memory on the instance. It is never logged.
 */

export const DEFAULT_SERVER_URL = 'http://127.0.0.1:8700';

/** Normalized API error with status, server error code, message, details, and request id. */
export class ApiError extends Error {
  constructor({ status = 0, code = null, message = '', details = [], requestId = null, body = null, network = false }) {
    super(message || (network ? 'Network error' : 'HTTP ' + status));
    this.name = 'ApiError';
    this.status = status;
    this.code = code;
    this.details = Array.isArray(details) ? details : [];
    this.requestId = requestId;
    this.body = body;
    this.network = network;
  }

  /** True when the server could not be reached at all (DNS, refused, CORS, offline). */
  get isNetwork() {
    return this.network;
  }
}

/** Remove a trailing slash and surrounding whitespace from a base URL. */
export function normalizeBaseUrl(url) {
  return String(url || '').trim().replace(/\/+$/, '');
}

/** Build a query string from an object, skipping null, undefined, and empty values. */
export function buildQuery(params) {
  const search = new URLSearchParams();
  if (params) {
    for (const [key, value] of Object.entries(params)) {
      if (value === undefined || value === null || value === '') continue;
      search.append(key, value instanceof Date ? value.toISOString() : String(value));
    }
  }
  const text = search.toString();
  return text ? '?' + text : '';
}

/** Normalize a server EnumerationResult into the dashboard's paged shape. */
export function normalizePage(result) {
  const r = result || {};
  return {
    items: Array.isArray(r.Items) ? r.Items : [],
    pageNumber: r.PageNumber || 1,
    pageSize: r.PageSize || 25,
    totalCount: Number(r.TotalCount || 0),
    totalPages: r.TotalPages || 0
  };
}

class ApiClient {
  /**
   * @param {string} baseUrl Server URL, for example http://127.0.0.1:8700
   * @param {string|null} apiKey Access key or admin key, sent as a bearer token
   * @param {{ fetchImpl?: Function, locale?: string }} options
   */
  constructor(baseUrl, apiKey = null, options = {}) {
    this.baseUrl = normalizeBaseUrl(baseUrl);
    this.apiKey = apiKey || null;
    this.fetchImpl = options.fetchImpl || ((...args) => globalThis.fetch(...args));
    this.locale = options.locale || null;
  }

  url(path, query) {
    return this.baseUrl + path + buildQuery(query);
  }

  headers(extra = {}, withBody = false) {
    const headers = { Accept: 'application/json' };
    if (withBody) headers['Content-Type'] = 'application/json';
    if (this.apiKey) headers.Authorization = 'Bearer ' + this.apiKey;
    if (this.locale) headers['Accept-Language'] = this.locale;
    return { ...headers, ...extra };
  }

  /**
   * Core request. Returns { status, headers, data } where data is parsed JSON (or null).
   * Throws ApiError for network failures and non-2xx responses.
   */
  async send(method, path, { query = null, body = undefined, headers = {}, signal = undefined, auth = true } = {}) {
    const hasBody = body !== undefined && body !== null;
    const init = { method, headers: this.headers(headers, hasBody), signal };
    if (!auth) delete init.headers.Authorization;
    if (hasBody) init.body = typeof body === 'string' ? body : JSON.stringify(body);

    let response;
    try {
      response = await this.fetchImpl(this.url(path, query), init);
    } catch (err) {
      if (err && err.name === 'AbortError') throw err;
      throw new ApiError({ network: true, message: (err && err.message) || 'Network error' });
    }

    const text = await response.text().catch(() => '');
    let data = null;
    if (text) {
      try {
        data = JSON.parse(text);
      } catch {
        data = text;
      }
    }

    if (!response.ok) {
      if (response.status === 401 && auth && typeof window !== 'undefined') {
        window.dispatchEvent(new CustomEvent('keyframe:unauthorized'));
      }
      const err = data && typeof data === 'object' ? data : {};
      throw new ApiError({
        status: response.status,
        code: err.Error || null,
        message: err.Message || (typeof data === 'string' && data) || response.statusText || 'HTTP ' + response.status,
        details: err.Details || [],
        requestId: err.RequestId || response.headers.get('x-request-id'),
        body: data
      });
    }

    return { status: response.status, headers: response.headers, data };
  }

  async request(method, path, options) {
    const result = await this.send(method, path, options);
    return result.data;
  }

  // System
  whoAmI() {
    return this.request('GET', '/v1.0/whoami');
  }

  getHealth() {
    return this.request('GET', '/v1.0/health', { auth: false });
  }

  getOpenApiSpec() {
    return this.request('GET', '/openapi.json', { auth: false });
  }

  /**
   * Same-origin fallback for the OpenAPI document, served through the dashboard's own
   * proxy (Vite dev server or nginx). Used when the direct cross-origin fetch fails.
   */
  async getOpenApiSpecViaProxy() {
    let response;
    try {
      response = await this.fetchImpl('/openapi.json', { headers: { Accept: 'application/json' } });
    } catch (err) {
      throw new ApiError({ network: true, message: (err && err.message) || 'Network error' });
    }
    const type = response.headers.get('content-type') || '';
    if (!response.ok || !type.includes('json')) throw new ApiError({ status: response.status, message: 'Proxy did not return the OpenAPI document' });
    return response.json();
  }

  // Analysis
  analyzeImage(request, signal) {
    return this.request('POST', '/v1.0/image', { body: request, signal });
  }

  /** Returns { status, data }: 202 with a JobResponse, or 200 with a result when Wait is set. */
  async analyzeVideo(request, signal) {
    const result = await this.send('POST', '/v1.0/video', { body: request, signal });
    return { status: result.status, data: result.data };
  }

  probe(media) {
    return this.request('POST', '/v1.0/probe', { body: { Media: media } });
  }

  // Jobs
  async listJobs(filter = {}) {
    return normalizePage(await this.request('GET', '/v1.0/jobs', { query: filter }));
  }

  getJob(id) {
    return this.request('GET', '/v1.0/jobs/' + encodeURIComponent(id));
  }

  getJobResult(id) {
    return this.request('GET', '/v1.0/jobs/' + encodeURIComponent(id) + '/result');
  }

  cancelJob(id) {
    return this.request('POST', '/v1.0/jobs/' + encodeURIComponent(id) + '/cancel');
  }

  deleteJob(id) {
    return this.request('DELETE', '/v1.0/jobs/' + encodeURIComponent(id));
  }

  // Endpoints
  listEndpoints() {
    return this.request('GET', '/v1.0/endpoints');
  }

  testEndpoint(name) {
    return this.request('POST', '/v1.0/endpoints/' + encodeURIComponent(name) + '/test');
  }

  // Settings (admin)
  getSettings() {
    return this.request('GET', '/v1.0/settings');
  }

  updateSettings(settings) {
    return this.request('PUT', '/v1.0/settings', { body: settings });
  }

  restartServer() {
    return this.request('POST', '/v1.0/restart');
  }

  // Request history
  async getRequestHistory(filter = {}) {
    return normalizePage(await this.request('GET', '/v1.0/api/request-history', { query: filter }));
  }

  getRequestHistorySummary(filter = {}) {
    return this.request('GET', '/v1.0/api/request-history/summary', { query: filter });
  }

  getRequestHistoryEntry(id) {
    return this.request('GET', '/v1.0/api/request-history/' + encodeURIComponent(id));
  }

  deleteRequestHistoryEntry(id) {
    return this.request('DELETE', '/v1.0/api/request-history/' + encodeURIComponent(id));
  }

  deleteRequestHistory(filter = {}) {
    return this.request('DELETE', '/v1.0/api/request-history', { query: filter });
  }

  /**
   * Raw execution for the API Explorer. Returns the fetch Response so the caller can read
   * status, headers, and streaming bodies. Network failures still throw ApiError.
   */
  async executeRaw({ method, path, query = null, headers = {}, body = null, signal = undefined }) {
    const init = { method, headers: { ...this.headers({}, false), ...headers }, signal };
    if (body !== null && body !== undefined && body !== '') init.body = body;
    try {
      return await this.fetchImpl(this.url(path, query), init);
    } catch (err) {
      if (err && err.name === 'AbortError') throw err;
      throw new ApiError({ network: true, message: (err && err.message) || 'Network error' });
    }
  }
}

export default ApiClient;
