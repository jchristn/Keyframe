import { describe, expect, it, vi } from 'vitest';
import ApiClient, { ApiError, buildQuery, normalizeBaseUrl, normalizePage } from './api';

function jsonResponse(status, body, headers = {}) {
  return new Response(body === undefined ? '' : JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json', ...headers }
  });
}

describe('ApiClient', () => {
  it('normalizes the base URL and builds queries without empty values', () => {
    expect(normalizeBaseUrl(' http://127.0.0.1:8700/ ')).toBe('http://127.0.0.1:8700');
    expect(buildQuery({ a: 1, b: '', c: null, d: undefined, e: 'x y' })).toBe('?a=1&e=x+y');
    expect(buildQuery({})).toBe('');
  });

  it('sends the key as a bearer token and parses JSON', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse(200, { IsAuthenticated: true, IsAdmin: false }));
    const client = new ApiClient('http://127.0.0.1:8700/', 'secret-key', { fetchImpl });
    const who = await client.whoAmI();
    expect(who.IsAuthenticated).toBe(true);
    const [url, init] = fetchImpl.mock.calls[0];
    expect(url).toBe('http://127.0.0.1:8700/v1.0/whoami');
    expect(init.method).toBe('GET');
    expect(init.headers.Authorization).toBe('Bearer secret-key');
    expect(init.headers['Content-Type']).toBeUndefined();
  });

  it('omits the key on anonymous routes and sets JSON content type on bodies', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse(200, { Status: 'Healthy' }));
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    await client.getHealth();
    expect(fetchImpl.mock.calls[0][1].headers.Authorization).toBeUndefined();

    fetchImpl.mockResolvedValue(jsonResponse(200, { Success: true }));
    await client.analyzeImage({ Image: { Url: 'https://example.com/a.jpg' } });
    const init = fetchImpl.mock.calls[1][1];
    expect(init.method).toBe('POST');
    expect(init.headers['Content-Type']).toBe('application/json');
    expect(JSON.parse(init.body).Image.Url).toBe('https://example.com/a.jpg');
  });

  it('normalizes paged results and passes filters as query parameters', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse(200, { Items: [{ Id: 'kfj_1' }], PageNumber: 2, PageSize: 10, TotalCount: 11, TotalPages: 2 }));
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    const page = await client.listJobs({ status: 'Failed', pageNumber: 2, pageSize: 10, principalName: '' });
    expect(page).toEqual({ items: [{ Id: 'kfj_1' }], pageNumber: 2, pageSize: 10, totalCount: 11, totalPages: 2 });
    expect(fetchImpl.mock.calls[0][0]).toBe('http://127.0.0.1:8700/v1.0/jobs?status=Failed&pageNumber=2&pageSize=10');
    expect(normalizePage(null).items).toEqual([]);
  });

  it('turns ApiErrorResponse bodies into ApiError with code, details, and request id', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(
      jsonResponse(400, { Error: 'BadRequest', StatusCode: 400, Message: 'The request is invalid.', Details: ['Image must specify Url or Data.'], RequestId: 'rqi_1' })
    );
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    const err = await client.analyzeImage({}).catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.status).toBe(400);
    expect(err.code).toBe('BadRequest');
    expect(err.message).toBe('The request is invalid.');
    expect(err.details).toEqual(['Image must specify Url or Data.']);
    expect(err.requestId).toBe('rqi_1');
    expect(err.network).toBe(false);
  });

  it('reports network failures as ApiError with network set', async () => {
    const fetchImpl = vi.fn().mockRejectedValue(new TypeError('Failed to fetch'));
    const client = new ApiClient('http://127.0.0.1:1', 'k', { fetchImpl });
    const err = await client.whoAmI().catch((e) => e);
    expect(err).toBeInstanceOf(ApiError);
    expect(err.network).toBe(true);
    expect(err.status).toBe(0);
  });

  it('dispatches keyframe:unauthorized on a 401', async () => {
    const listener = vi.fn();
    window.addEventListener('keyframe:unauthorized', listener);
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse(401, { Error: 'AuthenticationFailed', Message: 'The key is not valid.' }));
    const client = new ApiClient('http://127.0.0.1:8700', 'bad', { fetchImpl });
    await client.listEndpoints().catch(() => {});
    expect(listener).toHaveBeenCalledTimes(1);
    window.removeEventListener('keyframe:unauthorized', listener);
  });

  it('returns the status for video submissions and null for 204 deletes', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse(202, { Id: 'kfj_2', Status: 'Queued' }));
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    const r = await client.analyzeVideo({ Video: { Url: 'https://example.com/v.mp4' } });
    expect(r).toEqual({ status: 202, data: { Id: 'kfj_2', Status: 'Queued' } });

    fetchImpl.mockResolvedValue(new Response(null, { status: 204 }));
    expect(await client.deleteJob('kfj_2')).toBeNull();
    expect(fetchImpl.mock.calls[1][0]).toBe('http://127.0.0.1:8700/v1.0/jobs/kfj_2');
    expect(fetchImpl.mock.calls[1][1].method).toBe('DELETE');
  });

  it('encodes path segments', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(jsonResponse(200, { Name: 'a b', Success: true }));
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    await client.testEndpoint('a b/c');
    expect(fetchImpl.mock.calls[0][0]).toBe('http://127.0.0.1:8700/v1.0/endpoints/a%20b%2Fc/test');
  });

  it('executeRaw returns the raw Response with the bearer header', async () => {
    const fetchImpl = vi.fn().mockResolvedValue(new Response('hello', { status: 200 }));
    const client = new ApiClient('http://127.0.0.1:8700', 'k', { fetchImpl });
    const res = await client.executeRaw({ method: 'GET', path: '/v1.0/health', query: { x: '1' } });
    expect(await res.text()).toBe('hello');
    expect(fetchImpl.mock.calls[0][0]).toBe('http://127.0.0.1:8700/v1.0/health?x=1');
    expect(fetchImpl.mock.calls[0][1].headers.Authorization).toBe('Bearer k');
  });
});
