# keyframe-sdk (JavaScript)

JavaScript client for the Keyframe REST API. Keyframe summarizes images, video, and audio and extracts typed metadata fields.

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
| `audio.analyze(request)` | `POST /v1.0/audio`, resolves `{statusCode, jobId, job, result}` |
| `audio.analyzeAndWait(request, {pollIntervalMs, timeoutMs})` | Submits, polls until terminal, fetches the `AudioAnalysisResponse` |
| `segments.get(request)` | `POST /v1.0/segment` as binary, resolves `{data, mediaType, filename, startMs, endMs, durationMs, sizeBytes, requestId}` |
| `segments.getJson(request)` | `POST /v1.0/segment` as JSON, resolves a `SegmentResponse` (`Data` is base64) |
| `probe.probe(requestOrMedia)` | `POST /v1.0/probe` |
| `jobs.list(filter)`, `get(id)`, `getResult(id)`, `cancel(id)`, `delete(id)`, `wait(id, options)` | `/v1.0/jobs` |
| `endpoints.list()`, `endpoints.test(name)` (admin) | `/v1.0/endpoints` |
| `requestHistory.list(filter)`, `summary(filter)`, `get(id)`, `delete(id)` (admin), `deleteMany(filter)` (admin) | `/v1.0/api/request-history` |
| `settings.get()`, `update(settings)`, `restart()` (all admin) | `/v1.0/settings`, `/v1.0/restart` |
| `analytics.summary(filter)` (admin) | `GET /v1.0/analytics/summary` |
| `analytics.listAnalyses(filter)`, `getAnalysis(id)`, `deleteAnalysis(id)`, `deleteAnalyses({olderThanUtc})` (all admin) | `/v1.0/analytics/analyses` |
| `sendRaw(method, path, body)` | Any route, no exception on error status |
| `sendRawBytes(method, path, body)` | Same, with the body read as a `Uint8Array` |

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

## Audio

`audio.analyze` and `audio.analyzeAndWait` work like their video counterparts. Audio files (mp3, wav, flac, m4a or aac, ogg or opus, webm audio) and video files (audio track only) are accepted. `Transcribe`, `Summarize`, and `Measure` are on by default, and `Extract` and `Sections` are off; when you send a `Functions` object, set `Enable: true` on each function you want (every omitted function keeps its default, so `Transcribe`, `Summarize`, and `Measure` stay on). Summarize and Extract hear the audio through the listening model when one is usable (`Listen.Enable` is true by default), and use the transcript as well when transcription is on.

```js
const result = await client.audio.analyzeAndWait({
  Audio: await mediaFromFile('standup.mp3'),
  TranscriptionModel: { Endpoint: 'whisper' },
  TextModel: { Endpoint: 'ollama-gpt-oss-20b' },
  Functions: {
    Transcribe: { Enable: true, Language: 'en' },
    Summarize: { Enable: true, Style: 'Bullets' },
    Extract: { Enable: true, Fields: [{ Key: 'blockers', Type: 'Boolean', Prompt: 'Does anyone report a blocker?' }] }
  }
});

console.log(result.Transcript?.Text, result.Results.Summary, result.Telemetry.QueueWaitMs);
```

`jobs.getResult(id)` returns the `AudioAnalysisResponse` for audio jobs (in TypeScript, `jobs.getResult<AudioAnalysisResponse>(id)`).

### Listening

`Listening` is always present on an audio result and says whether the model heard the audio. `Listen.Model` names the listening endpoint; omitted, the server uses `Inference.DefaultListening`. `Listen.Rendition` sets the audio quality sent to it, and omitted members use the `Media.Listen*` settings.

```js
const result = await client.audio.analyzeAndWait({
  Audio: await mediaFromFile('track07.flac'),
  Listen: {
    Model: { Endpoint: 'llama-listen' },
    Rendition: { Format: 'Mp3', Channels: 2 },
    SampleClips: 4,
    ClipSeconds: 30
  }
});

const { Listened, Mode, Reason, Endpoint, HeardMs, DurationMs, Clips } = result.Listening;
if (Listened) console.log(`${Mode} via ${Endpoint}, heard ${HeardMs} of ${DurationMs} ms in ${Clips.length} clips`);
else console.log(`transcript only: ${Reason}`);
```

`Mode` is `Full`, `Sections`, `Excerpts`, or `None` on audio (`Scenes` appears only on video). `Reason` is null when the audio was heard, and otherwise `NotRequested` (`Listen.Enable` is false), `NoModelFunctions`, `NoEndpointConfigured`, `EndpointLacksAudioInput`, `EndpointUnhealthy`, or `NoAudioTrack`. `SupportsAudioInput` on each `endpoints.list()` entry shows which endpoints can listen. On video, `Functions.Scenes.Audio` (on by default) attaches each scene's audio to its scene calls, and the result's `Listening` reports the outcome.

### Measurements

`Functions.Measure` is on by default and makes no model calls. With every other function off, the request is a measure-only job.

```js
const result = await client.audio.analyzeAndWait({
  Audio: await mediaFromFile('master.wav'),
  Functions: { Transcribe: { Enable: false }, Summarize: { Enable: false } }
});

const m = result.Measurements;
console.log(m.IntegratedLufs, m.TruePeakDbtp, m.ClippingLikely, m.SilenceRatio, m.EffectivelyMono);
```

Level members are null when ffmpeg could not measure them, and `StereoWidthDb` is null for sources that are not stereo. The thresholds behind `ClippingLikely`, `EffectivelyMono`, and the silence figures are the `Media.Clipping*`, `Media.MonoMaxSideDb`, and `Media.MeasureSilence*` settings.

### Sections

`Functions.Sections` splits the recording at silences and runs its own `Summarize` and `Extract` on each section.

```js
const result = await client.audio.analyzeAndWait({
  Audio: await mediaFromFile('concert.mp3'),
  Functions: {
    Sections: {
      Enable: true,
      Detection: { SilenceDb: -40, MinSilenceMs: 1500, MinSectionMs: 20000 },
      Summarize: { Enable: true, MaxWords: 40 }
    }
  }
});

for (const section of result.Sections ?? []) {
  console.log(section.Index, section.StartMs, section.EndMs, section.Results.Summary);
}
```

Each section's `StartMs` and `EndMs` can be passed to `segments.get` to cut it back out. Per-section model calls have the purposes `SectionSummarize` and `SectionExtract`.

## Segments

`segments.get` cuts a time range out of a video or audio source and returns the bytes. Use `StartMs` and `EndMs` from `Scenes` or `Transcript.Segments` of an analysis result, and send the same `Source` you analyzed (the server does not keep media). `Output` is `Video` (mp4 or webm), `Audio` (mp3, wav, flac, m4a, or ogg), or `Frame` (jpg or png, one still at `StartMs`).

```js
import { writeFile } from 'node:fs/promises';

const clip = await client.segments.get({
  Source: mediaFromUrl('https://example.com/talks/keynote.mp4'),
  StartMs: 12000,
  EndMs: 18500,
  Output: 'Audio',
  Format: 'mp3'
});
await writeFile(clip.filename, clip.data); // segment-12000-18500.mp3, clip.mediaType is audio/mpeg
```

`filename` comes from `Content-Disposition`; `startMs`, `endMs`, and `durationMs` come from the `x-keyframe-segment-*` headers and reflect clamping to the media duration. `segments.getJson` returns the same segment as a `SegmentResponse` with base64 `Data`. An invalid range (for example `EndMs` not greater than `StartMs`, or longer than `Media.MaxSegmentMs`) is a `400` `KeyframeApiError`.

## Analytics

Every finished analysis is stored with its model call attempts. The analytics routes need the admin key; an access key gets `403`.

```js
const summary = await admin.analytics.summary({ bucket: 'hour', mediaKind: 'Audio', fromUtc: new Date(Date.now() - 86400000) });
console.log(summary.Totals.Count, summary.Totals.AnalysisMs?.P95, summary.ByMediaKind.Audio.Count);

const page = await admin.analytics.listAnalyses({ mediaKind: 'Audio', success: true, maxResults: 50, skip: 0 });
const record = await admin.analytics.getAnalysis(page.Items[0].Id); // record.Calls holds every model call attempt
await admin.analytics.deleteAnalysis(record.Id);                    // { DeletedCount: 1 }
await admin.analytics.deleteAnalyses({ olderThanUtc: new Date('2026-01-01') });
```

`summary` filters: `fromUtc`, `toUtc`, `bucket` (`minute`, `hour`, or `day`), `mediaKind`, `endpoint`, `model`. Every bucket in the range is present, and `ByMediaKind` always has `Image`, `Video`, and `Audio`. Listed records have `Calls: null`; `getAnalysis` fills it. `deleteAnalyses()` without `olderThanUtc` deletes every analysis.

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

`settings.get()` returns secrets masked as `****` plus the last four characters. Send the object back through `settings.update()` with masked values unchanged and the server keeps the stored secrets. `update` replaces the whole object, so always start from a fresh `get()`. `Analytics` (`Enable`, `RetentionDays`) and `Media.MaxSegmentMs` and `Media.MaxAudioDurationMs` apply to the routes above.

## Browser use

The package has no Node imports at module load. `segments.get` resolves `data` as a `Uint8Array` (wrap it in a `Blob` to download it). `mediaFromFile` and `analyzeImageFile` load `node:fs/promises` lazily and only work in Node; in the browser use `mediaFromBytes` with a `File` or `Blob` (`new Uint8Array(await file.arrayBuffer())`). The server sends permissive CORS headers.

## Test harness

`test/sdk.test.js` uses `node:test` and runs end to end against a live server using the mock provider. It generates its media fixtures with ffmpeg at run time.

```bash
cd sdk/js
KEYFRAME_URL=http://127.0.0.1:8710 KEYFRAME_ADMIN_KEY=keyframeadmin KEYFRAME_ACCESS_KEY=default npm test
```

The defaults are `http://127.0.0.1:8710`, `keyframeadmin`, and `default`. The harness performs one live settings write (it bumps `RequestHistory.RetentionDays` and restores it) and never calls `/v1.0/restart` with the admin key.

## License

MIT. See `LICENSE.md` in the repository root.
