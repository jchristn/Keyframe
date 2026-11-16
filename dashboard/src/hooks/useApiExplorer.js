import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import {
  flattenOpenApiSpec,
  getParameterDefault,
  getRequestBodyTemplate,
  groupByTag,
  headersToObject,
  substitutePathParams
} from '../utils/openApi';
import { STORAGE_KEYS, readStorage, writeStorage } from '../utils/storage';
import { filenameFromDisposition } from '../utils/media';

const HISTORY_LIMIT = 12;
const SENSITIVE_HEADERS = ['authorization', 'x-api-key', 'cookie'];

/** True for content types that should not be decoded as text. */
export function isBinaryType(contentType) {
  const type = String(contentType || '').toLowerCase();
  if (!type) return false;
  if (type.includes('json') || type.startsWith('text/') || type.includes('xml') || type.includes('javascript') || type.includes('event-stream') || type.includes('x-www-form-urlencoded')) return false;
  return /^(image|audio|video|font)\//.test(type) || type.includes('octet-stream') || type.includes('pdf') || type.includes('zip');
}

function stripSensitive(headers) {
  return headers.filter((h) => !SENSITIVE_HEADERS.includes(String(h.name).trim().toLowerCase()));
}

/**
 * State and execution for the OpenAPI-driven API Explorer. Authentication is inherited
 * from the dashboard ApiClient; history (capped at 12) never stores credential headers.
 */
export function useApiExplorer(apiClient) {
  const [spec, setSpec] = useState(null);
  const [specError, setSpecError] = useState(null);
  const [specLoading, setSpecLoading] = useState(true);
  const [specViaProxy, setSpecViaProxy] = useState(false);
  const [operationId, setOperationIdState] = useState(() => readStorage(STORAGE_KEYS.explorerSelected, null));
  const [pathParams, setPathParams] = useState({});
  const [queryParams, setQueryParams] = useState({});
  const [headers, setHeaders] = useState([]);
  // Authorization: pass the signed-in key through (default), or send a token typed here. The token is never stored.
  const [useSessionAuth, setUseSessionAuth] = useState(true);
  const [authToken, setAuthToken] = useState('');
  const [body, setBody] = useState('');
  const [response, setResponse] = useState(null);
  const [running, setRunning] = useState(false);
  const [history, setHistory] = useState(() => readStorage(STORAGE_KEYS.explorerHistory, []) || []);
  const abortRef = useRef(null);

  const loadSpec = useCallback(async () => {
    setSpecLoading(true);
    setSpecError(null);
    setSpecViaProxy(false);
    try {
      setSpec(await apiClient.getOpenApiSpec());
    } catch (err) {
      // A cross-origin failure (network or CORS) may still be served by the dashboard proxy.
      if (err && err.network) {
        try {
          setSpec(await apiClient.getOpenApiSpecViaProxy());
          setSpecViaProxy(true);
          return;
        } catch {
          // Fall through and report the original error.
        }
      }
      setSpecError(err);
    } finally {
      setSpecLoading(false);
    }
  }, [apiClient]);

  useEffect(() => {
    loadSpec();
  }, [loadSpec]);

  const operations = useMemo(() => flattenOpenApiSpec(spec), [spec]);
  const operation = operations.find((o) => o.id === operationId) || null;

  const resetFor = useCallback(
    (op) => {
      if (!op) return;
      const p = {};
      const q = {};
      for (const param of op.parameters) {
        if (param.in === 'path') p[param.name] = getParameterDefault(param);
        if (param.in === 'query') q[param.name] = getParameterDefault(param);
      }
      setPathParams(p);
      setQueryParams(q);
      setHeaders([]);
      setBody(op.requestBody ? getRequestBodyTemplate(op.requestBody, spec) : '');
      setResponse(null);
    },
    [spec]
  );

  const setOperationId = useCallback(
    (id) => {
      setOperationIdState(id);
      writeStorage(STORAGE_KEYS.explorerSelected, id);
      resetFor(operations.find((o) => o.id === id));
    },
    [operations, resetFor]
  );

  // Initialize the form once the spec arrives for a remembered selection.
  const initialized = useRef(false);
  useEffect(() => {
    if (initialized.current || !operations.length) return;
    initialized.current = true;
    const groups = groupByTag(operations, spec);
    const op = operations.find((o) => o.id === operationId) || (groups[0] && groups[0].items[0]) || operations[0];
    setOperationIdState(op.id);
    resetFor(op);
  }, [operations, operationId, resetFor, spec]);

  const resolvedPath = operation ? substitutePathParams(operation.path, pathParams) : '';
  const query = useMemo(() => {
    const out = {};
    for (const [k, v] of Object.entries(queryParams)) if (v !== '' && v !== null && v !== undefined) out[k] = v;
    return out;
  }, [queryParams]);
  const resolvedUrl = operation ? apiClient.url(resolvedPath, query) : '';
  const missingPath = operation ? operation.parameters.filter((p) => p.in === 'path' && !String(pathParams[p.name] || '').trim()).map((p) => p.name) : [];

  const bodyError = useMemo(() => {
    if (!operation || !operation.requestBody || !body.trim()) return null;
    try {
      JSON.parse(body);
      return null;
    } catch (err) {
      return err.message;
    }
  }, [operation, body]);

  const execute = useCallback(async () => {
    if (!operation) return;
    const controller = new AbortController();
    abortRef.current = controller;
    setRunning(true);
    setResponse(null);
    const extra = {};
    for (const h of headers) if (h.name && h.name.trim()) extra[h.name.trim()] = h.value;
    const customAuth = Object.keys(extra).some((k) => k.toLowerCase() === 'authorization');
    if (!useSessionAuth && !customAuth && authToken.trim()) extra.Authorization = 'Bearer ' + authToken.trim();
    const hasBody = operation.requestBody && body.trim();
    if (hasBody && !Object.keys(extra).some((k) => k.toLowerCase() === 'content-type')) extra['Content-Type'] = 'application/json';
    const start = performance.now();
    const addHistory = (status) => {
      const entry = {
        id: Date.now(),
        operationId: operation.id,
        method: operation.method,
        path: resolvedPath,
        pathParams,
        queryParams,
        headers: stripSensitive(headers),
        body: hasBody ? body : '',
        status,
        atUtc: new Date().toISOString()
      };
      setHistory((h) => {
        const next = [entry, ...h].slice(0, HISTORY_LIMIT);
        writeStorage(STORAGE_KEYS.explorerHistory, next);
        return next;
      });
    };
    try {
      const raw = await apiClient.executeRaw({ method: operation.method, path: resolvedPath, query, headers: extra, body: hasBody ? body : null, signal: controller.signal, auth: useSessionAuth });
      const headerObj = headersToObject(raw.headers);
      const contentType = headerObj['content-type'] || '';
      let text = '';
      const streaming = raw.body && (contentType.includes('text/event-stream') || headerObj['transfer-encoding'] === 'chunked');
      if (streaming && raw.body.getReader) {
        const reader = raw.body.getReader();
        const decoder = new TextDecoder();
        for (;;) {
          const { done, value } = await reader.read();
          if (done) break;
          text += decoder.decode(value, { stream: true });
          setResponse({ status: raw.status, statusText: raw.statusText, headers: headerObj, body: text, durationMs: performance.now() - start, bytes: text.length, streaming: true });
        }
      } else if (isBinaryType(contentType)) {
        // Media and other binary bodies (for example POST /v1.0/segment) are kept as a Blob
        // and offered as a download instead of being decoded as text.
        const blob = await raw.blob();
        const result = {
          status: raw.status,
          statusText: raw.statusText,
          headers: headerObj,
          body: '',
          blob,
          contentType,
          filename: filenameFromDisposition(headerObj['content-disposition']),
          durationMs: performance.now() - start,
          bytes: blob.size,
          streaming: false,
          binary: true
        };
        setResponse(result);
        addHistory(raw.status);
        return;
      } else {
        text = await raw.text();
      }
      const result = {
        status: raw.status,
        statusText: raw.statusText,
        headers: headerObj,
        body: text,
        durationMs: performance.now() - start,
        bytes: new Blob([text]).size,
        streaming: false
      };
      setResponse(result);
      addHistory(raw.status);
    } catch (err) {
      if (err && err.name === 'AbortError') setResponse({ aborted: true, durationMs: performance.now() - start });
      else setResponse({ error: err, durationMs: performance.now() - start });
    } finally {
      setRunning(false);
      abortRef.current = null;
    }
  }, [apiClient, operation, resolvedPath, query, headers, body, pathParams, queryParams, useSessionAuth, authToken]);

  const stop = useCallback(() => abortRef.current && abortRef.current.abort(), []);

  const loadHistory = useCallback(
    (entry) => {
      setOperationIdState(entry.operationId);
      setPathParams(entry.pathParams || {});
      setQueryParams(entry.queryParams || {});
      setHeaders(entry.headers || []);
      setBody(entry.body || '');
      setResponse(null);
    },
    []
  );

  const deleteHistory = useCallback((id) => {
    setHistory((h) => {
      const next = h.filter((e) => e.id !== id);
      writeStorage(STORAGE_KEYS.explorerHistory, next);
      return next;
    });
  }, []);

  return {
    spec,
    specError,
    specLoading,
    specViaProxy,
    loadSpec,
    operations,
    operation,
    operationId,
    setOperationId,
    pathParams,
    setPathParams,
    queryParams,
    setQueryParams,
    headers,
    setHeaders,
    useSessionAuth,
    setUseSessionAuth,
    authToken,
    setAuthToken,
    body,
    setBody,
    bodyError,
    resolvedPath,
    resolvedUrl,
    missingPath,
    response,
    running,
    execute,
    stop,
    history,
    loadHistory,
    deleteHistory,
    resetBody: () => operation && setBody(operation.requestBody ? getRequestBodyTemplate(operation.requestBody, spec) : '')
  };
}

export default useApiExplorer;
