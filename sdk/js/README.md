# keyframe-sdk (JavaScript)

JavaScript client for the Keyframe REST API. Keyframe summarizes images and video and extracts typed metadata fields.

- ESM only, no runtime dependencies, built on `fetch`.
- Node 20 or later, and modern browsers.
- Types ship in `src/index.d.ts`.

Version 0.1.0 (ALPHA).

## Install

```bash
npm install keyframe-sdk
```

Or import `sdk/js/src/index.js` directly.

## Quick start

```js
import { KeyframeClient } from 'keyframe-sdk';

const client = new KeyframeClient({ baseUrl: 'http://127.0.0.1:8700', apiKey: 'default' });

// Node helper: reads the file, sends it as base64, and analyzes it.
const result = await client.analyzeImageFile('backyard.jpg', {
  Context: [
    { Label: 'Source', Text: 'Photos submitted by pet adoption volunteers.' },
    'Animals wearing orange collars are already adopted.'
  ],
  Functions: {
    Summarize: { Enable: true, MaxWords: 60 },
    Extract: {
      Enable: true,
      Fields: [
        { Key: 'hasCats', Type: 'Boolean', Prompt: 'Does this image have cats?' },
        { Key: 'dogCount', Type: 'Integer', Prompt: 'How many dogs?', Minimum: 0 }
      ]
    }
  }
});

console.log(result.Results.Summary, result.Results.hasCats, result.Results.dogCount);
```

Request and response objects use the server's PascalCase JSON names exactly (`Functions`, `Results`, `Telemetry`). Enums are strings. `Context` accepts a string, an array of strings, `{Label, Text}` items, or a mix.

Base URLs use the IPv4 loopback `127.0.0.1` so Windows clients do not stall on an IPv6 fallback.

## Surface

| Member | Route |
|---|---|
| `health.get()`, `health.isAlive()` | `GET /v1.0/health`, `HEAD /` (anonymous) |
| `whoami.get()` | `GET /v1.0/whoami` |
| `image.analyze(request)` | `POST /v1.0/image` |
| `analyzeImageFile(path, request)` | Node only: reads the file, then `image.analyze` |
| `video.analyze(request)` | `POST /v1.0/video`, resolves `{statusCode, jobId, job, result}` |
| `video.analyzeAndWait(request, {pollIntervalMs, timeoutMs})` | Submits, polls until terminal, fetches the result |
| `probe.probe(requestOrMedia)` | `POST /v1.0/probe` |
| `jobs.list(filter)`, `get(id)`, `getResult(id)`, `cancel(id)`, `delete(id)`, `wait(id, options)` | `/v1.0/jobs` |
| `endpoints.list()`, `endpoints.test(name)` (admin) | `/v1.0/endpoints` |
| `requestHistory.list(filter)`, `summary(filter)`, `get(id)`, `delete(id)` (admin), `deleteMany(filter)` (admin) | `/v1.0/api/request-history` |
| `settings.get()`, `update(settings)`, `restart()` (all admin) | `/v1.0/settings`, `/v1.0/restart` |
| `sendRaw(method, path, body)` | Any route, no exception on error status |

Every method takes an optional last argument `{ signal }` (an `AbortSignal`).

Helpers: `mediaFromUrl(url)`, `mediaFromBytes(bytes, mediaType)`, `mediaFromFile(path)` (Node only), `guessMediaType(name)`, `isTerminalStatus(status)`.

Filters use camelCase query names: `jobs.list({ status: 'Succeeded', mediaKind: 'Video', fromUtc: new Date(...), pageSize: 50 })`.

## Video

```js
import { KeyframeClient, mediaFromUrl, KeyframeJobError } from 'keyframe-sdk';

const request = {
  Video: mediaFromUrl('https://example.com/talks/keynote.mp4'),
  Functions: {
    Summarize: { Enable: true },
    Scenes: { Enable: true, Summarize: { Enable: true } },
    Transcribe: { Enable: true }
  }
};

try {
  const result = await client.video.analyzeAndWait(request, { pollIntervalMs: 1000, timeoutMs: 600000 });
  console.log(result.Scenes.length, result.Transcript?.Text);
} catch (err) {
  if (err instanceof KeyframeJobError) console.error('job ended', err.job.Status, err.job.Error);
  else throw err;
}
```

With `Wait: true`, `video.analyze` may resolve with `result` set directly (the job id comes from the `x-keyframe-job-id` header). A job that finished without succeeding comes back as `job`.

## Authentication

Pass `apiKey` (an access key or the admin key). The client sends `Authorization: Bearer <key>` by default, or `x-api-key` with `authHeader: 'x-api-key'`. The key lives in a private class field: it is not readable, and it never appears in `toString()`, `JSON.stringify`, or `console.log` output. Use `setApiKey()` to change it.

## Errors

| Class | When |
|---|---|
| `KeyframeApiError` | Non-2xx response. `statusCode`, `error` (for example `'NotFound'`), `apiError` (the full `ApiErrorResponse`), `details`, `requestId`, `responseBody`, `method`, `path`. |
| `KeyframeJobError` | `analyzeAndWait` saw the job end `Failed`, `Canceled`, or `Interrupted`. `job` holds the final state. |
| `KeyframeTimeoutError` | A request exceeded `timeoutMs` (default 300000), or a wait exceeded its limit. |
| `KeyframeConnectionError` | The server could not be reached. `cause` holds the fetch error. |

An `AbortSignal` you pass rejects with the signal's own reason (usually an `AbortError`).

## Settings

`settings.get()` returns secrets masked as `****` plus the last four characters. Send the object back through `settings.update()` with masked values unchanged and the server keeps the stored secrets. `update` replaces the whole object, so always start from a fresh `get()`.

## Browser use

The package has no Node imports at module load. `mediaFromFile` and `analyzeImageFile` load `node:fs/promises` lazily and only work in Node; in the browser use `mediaFromBytes` with a `File` or `Blob` (`new Uint8Array(await file.arrayBuffer())`). The server sends permissive CORS headers.

## Test harness

`test/sdk.test.js` uses `node:test` and runs end to end against a live server using the mock provider. It generates its media fixtures with ffmpeg at run time.

```bash
cd sdk/js
KEYFRAME_URL=http://127.0.0.1:8710 KEYFRAME_ADMIN_KEY=keyframeadmin KEYFRAME_ACCESS_KEY=default npm test
```

The defaults are `http://127.0.0.1:8710`, `keyframeadmin`, and `default`. The harness performs one live settings write (it bumps `RequestHistory.RetentionDays` and restores it) and never calls `/v1.0/restart` with the admin key.

## License

MIT. See `LICENSE.md` in the repository root.
