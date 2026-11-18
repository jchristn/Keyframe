// keyframe-sdk end-to-end harness. Runs against a live Keyframe server using the mock provider.
// Environment: KEYFRAME_URL (default http://127.0.0.1:8710), KEYFRAME_ADMIN_KEY (default keyframeadmin),
// KEYFRAME_ACCESS_KEY (default default). Media fixtures are generated with ffmpeg (or $FFMPEG) at run time.

import { after, before, describe, it } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { inspect } from 'node:util';

import {
  KeyframeClient,
  KeyframeApiError,
  KeyframeConnectionError,
  KeyframeJobError,
  mediaFromBytes,
  mediaFromFile,
  isTerminalStatus
} from '../src/index.js';

const URL_BASE = process.env.KEYFRAME_URL || 'http://127.0.0.1:8710';
const ADMIN_KEY = process.env.KEYFRAME_ADMIN_KEY || 'keyframeadmin';
const ACCESS_KEY = process.env.KEYFRAME_ACCESS_KEY || 'default';
const FFMPEG = process.env.FFMPEG || 'ffmpeg';

const admin = new KeyframeClient({ baseUrl: URL_BASE, apiKey: ADMIN_KEY });
const user = new KeyframeClient({ baseUrl: URL_BASE, apiKey: ACCESS_KEY });
const anonymous = new KeyframeClient({ baseUrl: URL_BASE });

const fx = { dir: null, image: null, video: null, longVideo: null, audio: null, available: false, reason: null };

async function expectApiError(status, fn) {
  try {
    await fn();
  } catch (err) {
    assert.ok(err instanceof KeyframeApiError, `expected KeyframeApiError, got ${err && err.name}: ${err && err.message}`);
    assert.equal(err.statusCode, status, err.message);
    return err;
  }
  assert.fail(`expected HTTP ${status}, but the call succeeded`);
}

function requireFixtures(t) {
  if (!fx.available) {
    t.skip(fx.reason || 'fixtures unavailable');
    return false;
  }
  return true;
}

async function waitForAnalysis(id, timeoutMs = 15000) {
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    try {
      return await admin.analytics.getAnalysis(id);
    } catch (err) {
      if (!(err instanceof KeyframeApiError) || err.statusCode !== 404 || Date.now() >= deadline) throw err;
      await new Promise((resolve) => setTimeout(resolve, 200));
    }
  }
}

function videoArgs(seconds, path) {
  return ['-y', '-loglevel', 'error', '-f', 'lavfi', '-i', `testsrc=duration=${seconds}:size=320x240:rate=25`,
    '-f', 'lavfi', '-i', `sine=frequency=440:duration=${seconds}`, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-shortest', path];
}

const FIELDS = [
  { Key: 'hasCats', Type: 'Boolean', Prompt: 'Does this image have cats?' },
  { Key: 'dogCount', Type: 'Integer', Prompt: 'How many dogs?', Minimum: 0, Maximum: 20 },
  { Key: 'setting', Type: 'Enum', Prompt: 'Where was this taken?', Options: ['Indoors', 'Outdoors', 'Unknown'] },
  { Key: 'colors', Type: 'StringList', Prompt: 'List the dominant colors.', MaxItems: 5 },
  { Key: 'brightness', Type: 'Number', Prompt: 'Brightness 0 to 100.', Minimum: 0, Maximum: 100 },
  { Key: 'caption', Type: 'String', Prompt: 'Short caption.', MaxLength: 80 },
  { Key: 'details', Type: 'Object', Prompt: 'Any structured details.' }
];

async function videoRequest(path, wait) {
  return {
    Video: await mediaFromFile(path),
    Context: [{ Label: 'Event', Text: 'Synthetic test video.' }],
    Wait: wait,
    WaitTimeoutMs: 60000,
    Functions: {
      Summarize: { Enable: true },
      Extract: { Enable: true, Fields: [{ Key: 'topics', Type: 'StringList', Prompt: 'Main topics.', MaxItems: 4 }] },
      Scenes: {
        Enable: true,
        FramesPerScene: 2,
        Summarize: { Enable: true, MaxWords: 20 },
        Extract: { Enable: true, Fields: [{ Key: 'showsSlide', Type: 'Boolean', Prompt: 'Is a slide visible?' }] }
      },
      Transcribe: { Enable: true, Language: 'en' }
    }
  };
}

async function audioRequest(path, wait) {
  return {
    Audio: await mediaFromFile(path),
    Context: ['Synthetic sine tone.'],
    Wait: wait,
    WaitTimeoutMs: 60000,
    Functions: {
      Transcribe: { Enable: true, Language: 'en' },
      Summarize: { Enable: true, MaxWords: 30 },
      Extract: { Enable: true, Fields: [{ Key: 'speakerCount', Type: 'Integer', Prompt: 'How many speakers?', Minimum: 0, Maximum: 10 }] }
    }
  };
}

function checkAudioResult(result) {
  assert.equal(result.Success, true, (result.Errors || []).join('; '));
  assert.equal(result.MediaKind, 'Audio');
  assert.ok(result.Id.startsWith('kfa_'));
  assert.ok(result.RequestId, 'RequestId missing');
  assert.ok(result.Transcript && result.Transcript.Text.length > 0, 'transcript missing');
  assert.ok(result.Transcript.Segments.length > 0, 'transcript segments missing');
  assert.equal(typeof result.Results.Summary, 'string');
  assert.ok('speakerCount' in result.Results, 'field missing');
  assert.equal(result.Properties.MediaKind, 'Audio');
  assert.equal(result.Properties.HasAudio, true);
  assert.equal(typeof result.Telemetry.QueueWaitMs, 'number');
  assert.ok(result.Telemetry.ModelCalls.some((c) => c.Purpose === 'Transcribe'), 'no Transcribe call');
  const completion = result.Telemetry.ModelCalls.find((c) => c.Purpose === 'Summarize');
  assert.ok(completion, 'no Summarize call');
  assert.equal(typeof completion.QueueMs, 'number');
  assert.equal(typeof completion.GenerationMs, 'number', 'GenerationMs missing on the completion call');
  assert.equal(typeof completion.TokensPerSecond, 'number', 'TokensPerSecond missing on the completion call');
}

function checkVideoResult(result) {
  assert.equal(result.Success, true, (result.Errors || []).join('; '));
  assert.equal(result.MediaKind, 'Video');
  assert.ok(result.Scenes.length > 0, 'no scenes');
  assert.ok(result.Scenes.every((s) => s.Keyframes.length > 0), 'scene without keyframes');
  assert.ok('showsSlide' in result.Scenes[0].Results, 'scene field missing');
  assert.ok(result.Transcript && result.Transcript.Segments.length > 0, 'transcript missing');
  assert.ok(Array.isArray(result.Results.topics), 'topics missing');
  assert.equal(result.Properties.HasAudio, true);
}

before(async () => {
  const alive = await anonymous.health.isAlive();
  if (!alive) throw new Error(`Server at ${URL_BASE} is not reachable. Start it or set KEYFRAME_URL.`);
  fx.dir = mkdtempSync(join(tmpdir(), 'keyframe-sdk-js-'));
  fx.image = join(fx.dir, 'image.jpg');
  fx.video = join(fx.dir, 'video.mp4');
  fx.longVideo = join(fx.dir, 'long.mp4');
  fx.audio = join(fx.dir, 'tone.mp3');
  try {
    execFileSync(FFMPEG, ['-y', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360', '-frames:v', '1', fx.image]);
    execFileSync(FFMPEG, videoArgs(6, fx.video));
    execFileSync(FFMPEG, videoArgs(30, fx.longVideo));
    execFileSync(FFMPEG, ['-y', '-loglevel', 'error', '-f', 'lavfi', '-i', 'sine=frequency=440:duration=5', '-c:a', 'libmp3lame', fx.audio]);
    fx.available = existsSync(fx.image) && existsSync(fx.video) && existsSync(fx.longVideo) && existsSync(fx.audio);
    if (!fx.available) fx.reason = 'ffmpeg did not produce every fixture';
  } catch (err) {
    fx.reason = 'ffmpeg unavailable: ' + err.message;
  }
});

after(() => {
  if (fx.dir) rmSync(fx.dir, { recursive: true, force: true });
});

describe('system', () => {
  it('health is anonymous', async () => {
    const health = await anonymous.health.get();
    assert.equal(health.Product, 'Keyframe');
    assert.ok(['Healthy', 'Degraded'].includes(health.Status));
    assert.ok(health.Version);
    assert.ok('mock' in health.Endpoints);
  });

  it('HEAD / liveness', async () => {
    assert.equal(await anonymous.health.isAlive(), true);
  });

  it('raw request for /openapi.json', async () => {
    const raw = await anonymous.sendRaw('GET', '/openapi.json');
    assert.equal(raw.statusCode, 200);
    assert.ok(raw.body.includes('/v1.0/image'));
  });

  it('unreachable server raises KeyframeConnectionError', async () => {
    const dead = new KeyframeClient({ baseUrl: 'http://127.0.0.1:1', apiKey: ACCESS_KEY, timeoutMs: 5000 });
    assert.equal(await dead.health.isAlive(), false);
    await assert.rejects(() => dead.health.get(), KeyframeConnectionError);
  });

  it('aborted signal rejects', async () => {
    const controller = new AbortController();
    controller.abort();
    await assert.rejects(() => user.health.get({ signal: controller.signal }), (err) => err.name === 'AbortError');
  });

  it('key never appears in toString, inspect, or JSON', () => {
    assert.ok(!admin.toString().includes(ADMIN_KEY));
    assert.ok(!inspect(admin, { depth: 5, showHidden: true }).includes(ADMIN_KEY));
    assert.ok(!JSON.stringify(admin).includes(ADMIN_KEY));
    assert.equal(admin.hasApiKey, true);
    assert.equal(anonymous.hasApiKey, false);
  });

  it('constructor rejects a non-http base URL', () => {
    assert.throws(() => new KeyframeClient({ baseUrl: 'ftp://127.0.0.1' }), TypeError);
  });
});

describe('auth', () => {
  it('whoami without a key is 401', async () => {
    const err = await expectApiError(401, () => anonymous.whoami.get());
    assert.equal(err.error, 'AuthenticationFailed');
    assert.equal(err.apiError.StatusCode, 401);
    assert.ok(err.requestId);
    assert.equal(err.method, 'GET');
    assert.equal(err.path, '/v1.0/whoami');
  });

  it('whoami with a wrong key is 401', async () => {
    const bad = new KeyframeClient({ baseUrl: URL_BASE, apiKey: 'not-a-real-key-' + Date.now() });
    await expectApiError(401, () => bad.whoami.get());
  });

  it('whoami access key (Bearer)', async () => {
    const me = await user.whoami.get();
    assert.equal(me.IsAuthenticated, true);
    assert.equal(me.IsAdmin, false);
    assert.ok(me.PrincipalName);
  });

  it('whoami access key (x-api-key)', async () => {
    const client = new KeyframeClient({ baseUrl: URL_BASE, apiKey: ACCESS_KEY, authHeader: 'x-api-key' });
    const me = await client.whoami.get();
    assert.equal(me.IsAuthenticated, true);
    assert.equal(me.IsAdmin, false);
  });

  it('whoami admin key', async () => {
    const me = await admin.whoami.get();
    assert.equal(me.IsAdmin, true);
  });

  it('admin-only routes return 403 for an access key', async () => {
    const err = await expectApiError(403, () => user.settings.get());
    assert.equal(err.error, 'AuthorizationFailed');
    await expectApiError(403, () => user.settings.update({}));
    await expectApiError(403, () => user.settings.restart());
    await expectApiError(403, () => user.endpoints.test('mock'));
    await expectApiError(403, () => user.requestHistory.delete('req_doesnotexist'));
    await expectApiError(403, () => user.requestHistory.deleteMany({ pathContains: '/nothing' }));
  });
});

describe('image', () => {
  it('analysis with a mixed Context list and typed fields', async (t) => {
    if (!requireFixtures(t)) return;
    const response = await user.analyzeImageFile(fx.image, {
      Context: ['There are no animals in test patterns.', { Label: 'Source', Text: 'Synthetic ffmpeg test pattern.' }],
      Functions: {
        Summarize: { Enable: true, MaxWords: 40, Style: 'Caption' },
        Extract: { Enable: true, Fields: FIELDS }
      }
    });
    assert.equal(response.Success, true, response.Errors.join('; '));
    assert.ok(response.Id.startsWith('kfa_'));
    assert.equal(response.MediaKind, 'Image');
    assert.equal(response.Properties.Width, 640);
    assert.equal(response.Properties.Height, 360);
    assert.equal(typeof response.Results.Summary, 'string');
    assert.equal(response.Fields.length, 7);
    assert.ok(response.Fields.every((f) => f.Valid), 'a field was invalid');
    const r = response.Results;
    assert.equal(typeof r.hasCats, 'boolean');
    assert.ok(Number.isInteger(r.dogCount) && r.dogCount >= 0 && r.dogCount <= 20);
    assert.ok(['Indoors', 'Outdoors', 'Unknown'].includes(r.setting));
    assert.ok(Array.isArray(r.colors) && r.colors.length <= 5);
    assert.ok(typeof r.brightness === 'number' && r.brightness >= 0 && r.brightness <= 100);
    assert.equal(typeof r.caption, 'string');
    assert.equal(typeof r.details, 'object');
    assert.ok(response.Telemetry.ModelCalls.length > 0);
    assert.ok(response.Telemetry.Stages.some((s) => s.Name === 'Extract'));
  });

  it('PerField mode makes one Extract call per field', async (t) => {
    if (!requireFixtures(t)) return;
    const response = await user.image.analyze({
      Image: await mediaFromFile(fx.image),
      Functions: { Extract: { Enable: true, Mode: 'PerField', Fields: FIELDS } }
    });
    const calls = response.Telemetry.ModelCalls.filter((c) => c.Purpose === 'Extract');
    assert.equal(calls.length, 7);
    assert.ok(calls.every((c) => c.Target));
  });

  it('validation error maps to 400 with details', async () => {
    const err = await expectApiError(400, () => user.image.analyze({
      Image: { Url: 'https://example.com/a.jpg', Data: 'AAAA' },
      Functions: { Extract: { Enable: true, Fields: [{ Key: 'bad key!', Type: 'Enum', Prompt: '' }] } }
    }));
    assert.equal(err.error, 'BadRequest');
    assert.ok(err.details.length >= 3, err.responseBody);
  });

  it('undecodable image maps to 422', async () => {
    const err = await expectApiError(422, () => user.image.analyze({
      Image: mediaFromBytes(new TextEncoder().encode('this is not an image at all, just text bytes'), 'image/jpeg'),
      Functions: { Summarize: { Enable: true } }
    }));
    assert.equal(err.error, 'UnprocessableMedia');
  });

  it('unknown endpoint maps to 400', async (t) => {
    if (!requireFixtures(t)) return;
    await expectApiError(400, () => user.analyzeImageFile(fx.image, {
      Model: { Endpoint: 'no-such-endpoint' },
      Functions: { Summarize: { Enable: true } }
    }));
  });
});

describe('probe', () => {
  it('probe image', async (t) => {
    if (!requireFixtures(t)) return;
    const props = await user.probe.probe(await mediaFromFile(fx.image));
    assert.equal(props.MediaKind, 'Image');
    assert.equal(props.Width, 640);
    assert.ok(props.Sha256);
  });

  it('probe video', async (t) => {
    if (!requireFixtures(t)) return;
    const props = await user.probe.probe({ Media: await mediaFromFile(fx.video) });
    assert.equal(props.MediaKind, 'Video');
    assert.ok(Math.abs(props.DurationMs - 6000) < 500);
    assert.equal(props.HasAudio, true);
    assert.equal(props.VideoCodec, 'h264');
  });

  it('probe without media is 400', async () => {
    await expectApiError(400, () => user.probe.probe({ Media: null }));
  });
});

describe('video and jobs', () => {
  it('lifecycle: 202, poll, result, list, delete', async (t) => {
    if (!requireFixtures(t)) return;
    const submitted = await user.video.analyze(await videoRequest(fx.video, false));
    assert.equal(submitted.statusCode, 202);
    assert.ok(submitted.job && !submitted.result);
    assert.ok(submitted.jobId.startsWith('kfj_'));
    assert.ok(['Queued', 'Running'].includes(submitted.job.Status));

    const done = await user.jobs.wait(submitted.jobId, { pollIntervalMs: 200, timeoutMs: 120000 });
    assert.equal(done.Status, 'Succeeded');
    assert.ok(done.ResultUrl && done.CompletedUtc);
    assert.equal(done.Progress.Percent, 100);

    checkVideoResult(await user.jobs.getResult(submitted.jobId));

    const page = await user.jobs.list({ status: 'Succeeded', mediaKind: 'Video', pageSize: 100 });
    assert.ok(page.Items.some((j) => j.Id === submitted.jobId));
    assert.ok(page.Items.every((j) => j.Status === 'Succeeded'));

    await user.jobs.delete(submitted.jobId);
    await expectApiError(404, () => user.jobs.get(submitted.jobId));
  });

  it('Wait returns the result directly', async (t) => {
    if (!requireFixtures(t)) return;
    const submitted = await user.video.analyze(await videoRequest(fx.video, true));
    assert.equal(submitted.statusCode, 200);
    assert.ok(submitted.result && !submitted.job);
    assert.ok(submitted.jobId.startsWith('kfj_'), 'x-keyframe-job-id header missing');
    checkVideoResult(submitted.result);
    await user.jobs.delete(submitted.jobId);
  });

  it('analyzeAndWait', async (t) => {
    if (!requireFixtures(t)) return;
    checkVideoResult(await user.video.analyzeAndWait(await videoRequest(fx.video, false), { pollIntervalMs: 200, timeoutMs: 120000 }));
  });

  it('queued job: result 409, delete 409, cancel, cancel again 409', async (t) => {
    if (!requireFixtures(t)) return;
    const settings = await admin.settings.get();
    const workers = settings.Settings.Jobs.MaxConcurrentJobs;
    const blocker = await videoRequest(fx.longVideo, false);
    blocker.Functions.Scenes.Detection = { MinSceneMs: 1000, MaxSceneMs: 2000 };
    const blockers = [];
    for (let i = 0; i < workers; i++) blockers.push((await user.video.analyze(blocker)).jobId);
    const target = await user.video.analyze(await videoRequest(fx.video, false));
    try {
      const before = await user.jobs.get(target.jobId);
      if (before.Status !== 'Queued') {
        t.skip(`target job was ${before.Status} rather than Queued`);
        return;
      }
      const conflict = await expectApiError(409, () => user.jobs.getResult(target.jobId));
      assert.equal(conflict.error, 'Conflict');
      await expectApiError(409, () => user.jobs.delete(target.jobId));
      const canceled = await user.jobs.cancel(target.jobId);
      assert.equal(canceled.Status, 'Canceled');
      assert.ok(isTerminalStatus(canceled.Status));
      await expectApiError(409, () => user.jobs.cancel(target.jobId));
      await expectApiError(409, () => user.jobs.getResult(target.jobId));
      await user.jobs.delete(target.jobId);
    } finally {
      for (const id of blockers) {
        await user.jobs.wait(id, { pollIntervalMs: 250, timeoutMs: 180000 });
        await user.jobs.delete(id);
      }
    }
  });

  it('failed job rejects with KeyframeJobError', async (t) => {
    if (!requireFixtures(t)) return;
    const request = await videoRequest(fx.video, false);
    request.Video = await mediaFromFile(fx.image, 'video/mp4');
    try {
      await user.video.analyzeAndWait(request, { pollIntervalMs: 200, timeoutMs: 60000 });
      assert.fail('an image submitted as video unexpectedly succeeded');
    } catch (err) {
      if (err instanceof KeyframeApiError && (err.statusCode === 400 || err.statusCode === 422)) {
        t.skip(`server rejected the mismatched media at submit time (${err.statusCode})`);
        return;
      }
      assert.ok(err instanceof KeyframeJobError, `expected KeyframeJobError, got ${err.name}: ${err.message}`);
      assert.equal(err.job.Status, 'Failed');
      assert.ok(err.job.Error);
      await user.jobs.delete(err.job.Id);
    }
  });

  it('list filters and paging', async () => {
    const one = await user.jobs.list({ pageSize: 1 });
    assert.equal(one.PageSize, 1);
    assert.ok(one.Items.length <= 1);
    const none = await user.jobs.list({ fromUtc: new Date(Date.now() + 86400000) });
    assert.equal(none.Items.length, 0);
    await expectApiError(400, () => user.jobs.list({ status: 'Bogus' }));
  });

  it('unknown job is 404', async () => {
    const err = await expectApiError(404, () => user.jobs.get('kfj_doesnotexist'));
    assert.equal(err.error, 'NotFound');
    await expectApiError(404, () => user.jobs.cancel('kfj_doesnotexist'));
    await expectApiError(404, () => user.jobs.delete('kfj_doesnotexist'));
  });
});

describe('audio', () => {
  it('queue then wait for the job', async (t) => {
    if (!requireFixtures(t)) return;
    const submitted = await user.audio.analyze(await audioRequest(fx.audio, false));
    assert.equal(submitted.statusCode, 202);
    assert.ok(submitted.job && !submitted.result);
    assert.ok(submitted.jobId.startsWith('kfj_'));
    assert.equal(submitted.job.MediaKind, 'Audio');
    const done = await user.jobs.wait(submitted.jobId, { pollIntervalMs: 200, timeoutMs: 120000 });
    assert.equal(done.Status, 'Succeeded', done.Error || '');
    checkAudioResult(await user.jobs.getResult(submitted.jobId));
    await user.jobs.delete(submitted.jobId);
  });

  it('Wait returns the result directly', async (t) => {
    if (!requireFixtures(t)) return;
    const submitted = await user.audio.analyze(await audioRequest(fx.audio, true));
    assert.equal(submitted.statusCode, 200);
    assert.ok(submitted.result && !submitted.job);
    assert.ok(submitted.jobId.startsWith('kfj_'), 'x-keyframe-job-id header missing');
    checkAudioResult(submitted.result);
    await user.jobs.delete(submitted.jobId);
  });

  it('analyzeAndWait', async (t) => {
    if (!requireFixtures(t)) return;
    checkAudioResult(await user.audio.analyzeAndWait(await audioRequest(fx.audio, false), { pollIntervalMs: 200, timeoutMs: 120000 }));
  });

  it('missing Audio is 400', async () => {
    await expectApiError(400, () => user.audio.analyze({ Functions: { Transcribe: { Enable: true } } }));
  });

  it('default analysis listens through the mock (Mode Full)', async (t) => {
    if (!requireFixtures(t)) return;
    const result = await user.audio.analyzeAndWait({ Audio: await mediaFromFile(fx.audio) }, { pollIntervalMs: 200, timeoutMs: 120000 });
    assert.equal(result.Success, true, (result.Errors || []).join('; '));
    const listening = result.Listening;
    assert.ok(listening, 'Listening missing');
    assert.equal(listening.Listened, true, `not listened, reason ${listening.Reason}`);
    assert.equal(listening.Mode, 'Full');
    assert.equal(listening.Reason, null);
    assert.equal(listening.Endpoint, 'mock-listen');
    assert.equal(listening.Rendition.Format, 'Mp3');
    assert.equal(typeof listening.Rendition.SampleRate, 'number');
    assert.ok(listening.DurationMs > 0 && listening.HeardMs > 0, 'DurationMs or HeardMs is zero');
    assert.ok(listening.Clips.some((c) => c.Scope === 'Recording' && c.Index === null && c.EndMs > c.StartMs && c.SizeBytes > 0), 'no Recording clip');
    assert.ok(result.Measurements, 'Measurements missing (Measure is on by default)');
    assert.ok(result.Sections == null, 'Sections present without Sections.Enable');
    assert.ok(result.Telemetry.Stages.some((st) => st.Name === 'ListenExtraction'), 'no ListenExtraction stage');
  });

  it('Listen.Enable false reports NotRequested', async (t) => {
    if (!requireFixtures(t)) return;
    const result = await user.audio.analyzeAndWait({ Audio: await mediaFromFile(fx.audio), Listen: { Enable: false } }, { pollIntervalMs: 200, timeoutMs: 120000 });
    assert.equal(result.Success, true, (result.Errors || []).join('; '));
    assert.equal(result.Listening.Listened, false);
    assert.equal(result.Listening.Mode, 'None');
    assert.equal(result.Listening.Reason, 'NotRequested');
    assert.deepEqual(result.Listening.Clips, []);
  });

  it('measure-only has Measurements and no model calls', async (t) => {
    if (!requireFixtures(t)) return;
    const result = await user.audio.analyzeAndWait({
      Audio: await mediaFromFile(fx.audio),
      Functions: { Transcribe: { Enable: false }, Summarize: { Enable: false } }
    }, { pollIntervalMs: 200, timeoutMs: 120000 });
    assert.equal(result.Success, true, (result.Errors || []).join('; '));
    assert.equal(result.Transcript, null);
    const m = result.Measurements;
    assert.ok(m, 'Measurements missing');
    assert.equal(m.Channels, 1);
    assert.equal(typeof m.IntegratedLufs, 'number');
    assert.equal(typeof m.TruePeakDbtp, 'number');
    assert.equal(m.StereoWidthDb, null);
    assert.equal(m.EffectivelyMono, true);
    assert.equal(typeof m.ClippingLikely, 'boolean');
    assert.ok(m.SilenceRatio >= 0 && m.SilenceRatio <= 1, 'SilenceRatio out of range');
    assert.equal(result.Telemetry.ModelCalls.length, 0, 'measure-only made model calls');
    assert.equal(result.Telemetry.Totals.ModelCalls, 0);
    assert.equal(result.Listening.Reason, 'NoModelFunctions');
    assert.ok(result.Telemetry.Stages.some((st) => st.Name === 'Measure'), 'no Measure stage');
  });

  it('sections with per-section summaries', async (t) => {
    if (!requireFixtures(t)) return;
    const result = await user.audio.analyzeAndWait({
      Audio: await mediaFromFile(fx.audio),
      Functions: { Sections: { Enable: true, Summarize: { Enable: true } } }
    }, { pollIntervalMs: 200, timeoutMs: 120000 });
    assert.equal(result.Success, true, (result.Errors || []).join('; '));
    assert.ok(Array.isArray(result.Sections) && result.Sections.length > 0, 'Sections missing or empty');
    const first = result.Sections[0];
    assert.equal(first.Index, 0);
    assert.ok(first.EndMs > first.StartMs && first.DurationMs > 0, 'section bounds');
    assert.equal(typeof first.Results.Summary, 'string');
    assert.ok(result.Telemetry.ModelCalls.some((c) => c.Purpose === 'SectionSummarize'), 'no SectionSummarize call');
    assert.ok(result.Listening.Clips.some((c) => c.Scope === 'Section' && c.Index === 0), 'no Section clip');
  });
});

describe('segments', () => {
  it('binary Audio segment', async (t) => {
    if (!requireFixtures(t)) return;
    const seg = await user.segments.get({ Source: await mediaFromFile(fx.audio), StartMs: 1000, EndMs: 3000, Output: 'Audio' });
    assert.ok(seg.data instanceof Uint8Array);
    assert.ok(seg.data.length > 0);
    assert.equal(seg.sizeBytes, seg.data.length);
    assert.equal(seg.mediaType, 'audio/mpeg');
    assert.equal(seg.filename, 'segment-1000-3000.mp3');
    assert.equal(seg.startMs, 1000);
    assert.equal(seg.endMs, 3000);
    assert.equal(seg.durationMs, 2000);
    assert.ok(seg.requestId);
  });

  it('binary Audio segment from a video, wav format', async (t) => {
    if (!requireFixtures(t)) return;
    const seg = await user.segments.get({ Source: await mediaFromFile(fx.video), StartMs: 0, EndMs: 1500, Output: 'Audio', Format: 'wav' });
    assert.ok(seg.data.length > 0);
    assert.equal(seg.mediaType, 'audio/wav');
    assert.equal(seg.filename, 'segment-0-1500.wav');
    assert.equal(seg.durationMs, 1500);
  });

  it('binary Frame segment', async (t) => {
    if (!requireFixtures(t)) return;
    const seg = await user.segments.get({ Source: await mediaFromFile(fx.video), StartMs: 2000, Output: 'Frame', MaxEdge: 160 });
    assert.ok(seg.data.length > 0);
    assert.equal(seg.mediaType, 'image/jpeg');
    assert.equal(seg.filename, 'segment-2000.jpg');
    assert.equal(seg.startMs, 2000);
    assert.equal(seg.endMs, 2000);
    assert.equal(seg.durationMs, 0);
    assert.equal(seg.data[0], 0xff);
    assert.equal(seg.data[1], 0xd8);
  });

  it('json segment decodes to SizeBytes bytes', async (t) => {
    if (!requireFixtures(t)) return;
    const seg = await user.segments.getJson({ Source: await mediaFromFile(fx.video), StartMs: 1000, EndMs: 2000, Output: 'Video' });
    assert.equal(seg.MediaType, 'video/mp4');
    assert.equal(seg.Filename, 'segment-1000-2000.mp4');
    assert.equal(seg.StartMs, 1000);
    assert.equal(seg.EndMs, 2000);
    assert.equal(seg.DurationMs, 1000);
    assert.ok(seg.SizeBytes > 0);
    assert.equal(Buffer.from(seg.Data, 'base64').length, seg.SizeBytes);
  });

  it('EndMs <= StartMs is 400', async (t) => {
    if (!requireFixtures(t)) return;
    const source = await mediaFromFile(fx.audio);
    const err = await expectApiError(400, () => user.segments.get({ Source: source, StartMs: 3000, EndMs: 3000, Output: 'Audio' }));
    assert.equal(err.error, 'BadRequest');
    assert.ok(err.details.length > 0, err.responseBody);
    await expectApiError(400, () => user.segments.getJson({ Source: source, StartMs: 3000, EndMs: 1000, Output: 'Audio' }));
  });
});

describe('analytics', () => {
  it('summary after an analysis', async (t) => {
    if (!requireFixtures(t)) return;
    const response = await user.analyzeImageFile(fx.image, { Functions: { Summarize: { Enable: true } } });
    await waitForAnalysis(response.Id);
    const summary = await admin.analytics.summary({ bucket: 'minute', fromUtc: new Date(Date.now() - 3600000) });
    assert.equal(summary.Bucket, 'minute');
    assert.ok(summary.Totals.Count > 0);
    assert.ok(Array.isArray(summary.Buckets) && summary.Buckets.length > 0);
    assert.ok(summary.Buckets.every((b) => b.StartUtc && b.Stats));
    for (const kind of ['Image', 'Video', 'Audio']) assert.ok(kind in summary.ByMediaKind, `ByMediaKind.${kind} missing`);
    assert.ok(summary.ByMediaKind.Image.Count > 0);
    assert.ok(summary.Totals.AnalysisMs && summary.Totals.AnalysisMs.Count > 0);
    assert.ok(Array.isArray(summary.ByModel) && Array.isArray(summary.ByStage) && Array.isArray(summary.ByPurpose));
    const filtered = await admin.analytics.summary({ mediaKind: 'Audio', bucket: 'hour' });
    assert.equal(filtered.MediaKind, 'Audio');
    assert.equal(filtered.Bucket, 'hour');
  });

  it('list analyses filtered by mediaKind Audio', async (t) => {
    if (!requireFixtures(t)) return;
    const result = await user.audio.analyzeAndWait(await audioRequest(fx.audio, true), { pollIntervalMs: 200, timeoutMs: 120000 });
    await waitForAnalysis(result.Id);
    const page = await admin.analytics.listAnalyses({ mediaKind: 'Audio', maxResults: 100 });
    assert.ok(page.Items.length > 0);
    assert.ok(page.Items.every((a) => a.MediaKind === 'Audio'));
    assert.ok(page.Items.every((a) => a.Calls === null));
    assert.ok(page.Items.some((a) => a.Id === result.Id));
    const one = await admin.analytics.listAnalyses({ maxResults: 1, skip: 1 });
    assert.ok(one.Items.length <= 1);
    assert.equal(one.PageNumber, 2);
  });

  it('get analysis with Calls', async (t) => {
    if (!requireFixtures(t)) return;
    const result = await user.audio.analyzeAndWait(await audioRequest(fx.audio, true), { pollIntervalMs: 200, timeoutMs: 120000 });
    const record = await waitForAnalysis(result.Id);
    assert.equal(record.Id, result.Id);
    assert.equal(record.MediaKind, 'Audio');
    assert.equal(record.Success, true);
    assert.equal(record.RequestId, result.RequestId);
    assert.ok(record.JobId && record.JobId.startsWith('kfj_'));
    assert.ok(Array.isArray(record.Calls) && record.Calls.length > 0);
    assert.ok(record.Calls.every((c) => c.AnalysisId === result.Id));
    assert.ok(record.Calls.some((c) => c.Purpose === 'Transcribe'));
    const completion = record.Calls.find((c) => c.Purpose === 'Summarize');
    assert.ok(completion && typeof completion.TokensPerSecond === 'number');
    assert.equal(record.TranscriptionEndpoint, 'mock-stt');
  });

  it('delete one then 404', async (t) => {
    if (!requireFixtures(t)) return;
    const response = await user.analyzeImageFile(fx.image, { Functions: { Summarize: { Enable: true } } });
    await waitForAnalysis(response.Id);
    const deleted = await admin.analytics.deleteAnalysis(response.Id);
    assert.equal(deleted.DeletedCount, 1);
    await expectApiError(404, () => admin.analytics.getAnalysis(response.Id));
    await expectApiError(404, () => admin.analytics.deleteAnalysis(response.Id));
  });

  it('deleteAnalyses with a past cutoff returns a count', async () => {
    const before = await admin.analytics.listAnalyses({ maxResults: 1 });
    const result = await admin.analytics.deleteAnalyses({ olderThanUtc: new Date('2000-01-01T00:00:00Z') });
    assert.equal(typeof result.DeletedCount, 'number');
    assert.equal(result.DeletedCount, 0);
    const afterward = await admin.analytics.listAnalyses({ maxResults: 1 });
    assert.ok(afterward.TotalCount >= before.TotalCount);
  });

  it('unknown analysis is 404', async () => {
    const err = await expectApiError(404, () => admin.analytics.getAnalysis('kfa_doesnotexist'));
    assert.equal(err.error, 'NotFound');
  });

  it('non-admin key is 403', async () => {
    const err = await expectApiError(403, () => user.analytics.summary());
    assert.equal(err.error, 'AuthorizationFailed');
    await expectApiError(403, () => user.analytics.listAnalyses());
    await expectApiError(403, () => user.analytics.getAnalysis('kfa_doesnotexist'));
    await expectApiError(403, () => user.analytics.deleteAnalysis('kfa_doesnotexist'));
    await expectApiError(403, () => user.analytics.deleteAnalyses({ olderThanUtc: new Date('2000-01-01T00:00:00Z') }));
    await expectApiError(401, () => anonymous.analytics.summary());
  });
});

describe('endpoints', () => {
  it('list endpoints', async () => {
    const endpoints = await user.endpoints.list();
    const mock = endpoints.find((e) => e.Name === 'mock');
    assert.ok(mock);
    assert.equal(mock.Provider, 'Mock');
    assert.equal(mock.IsDefault, true);
    assert.ok(endpoints.some((e) => e.Kind === 'Transcription'));
    assert.equal(mock.SupportsAudioInput, true);
    assert.ok(endpoints.some((e) => e.Name === 'mock-listen' && e.SupportsAudioInput === true), 'mock-listen missing or without SupportsAudioInput');
  });

  it('test endpoint (admin)', async () => {
    const result = await admin.endpoints.test('mock');
    assert.equal(result.Success, true, result.Error || '');
    assert.equal(result.Name, 'mock');
  });

  it('test unknown endpoint is 404', async () => {
    await expectApiError(404, () => admin.endpoints.test('no-such-endpoint'));
  });
});

describe('settings', () => {
  it('read masks secrets', async () => {
    const response = await admin.settings.get();
    const s = response.Settings;
    assert.ok(s.Auth.AdminApiKey.startsWith('****'));
    assert.notEqual(s.Auth.AdminApiKey, ADMIN_KEY);
    assert.ok(s.Auth.AccessKeys.every((k) => k.Key.startsWith('****')));
    assert.ok(s.Inference.Endpoints.every((e) => e.ApiKey === null || e.ApiKey.startsWith('****')));
    assert.ok(response.Fields.some((f) => f.Path === 'Auth.AdminApiKey' && f.Secret));
    assert.ok(response.Fields.some((f) => f.ApplyMode === 'Restart') && response.Fields.some((f) => f.ApplyMode === 'Live'));
  });

  it('includes Analytics and the segment and audio limits', async () => {
    const s = (await admin.settings.get()).Settings;
    assert.ok(s.Analytics, 'Analytics missing');
    assert.equal(typeof s.Analytics.Enable, 'boolean');
    assert.equal(typeof s.Analytics.RetentionDays, 'number');
    assert.equal(typeof s.Media.MaxSegmentMs, 'number');
    assert.ok(s.Media.MaxSegmentMs >= 1000);
    assert.equal(typeof s.Media.MaxAudioDurationMs, 'number');
  });

  it('includes the listening and measurement settings', async () => {
    const s = (await admin.settings.get()).Settings;
    assert.equal(s.Inference.DefaultListening, 'mock-listen');
    assert.equal(typeof s.Inference.HealthProbeIntervalMs, 'number');
    assert.equal(typeof s.Inference.UnhealthyProbeIntervalMs, 'number');
    assert.equal(s.Media.ListenFormat, 'Mp3');
    assert.ok(s.Media.ListenSampleRate >= 8000 && s.Media.ListenSampleRate <= 48000);
    assert.equal(typeof s.Media.MeasureSilenceDb, 'number');
    assert.equal(typeof s.Media.MonoMaxSideDb, 'number');
    assert.ok(s.Inference.Endpoints.every((e) => e.MaxInlineMediaBytes >= 1048576));
  });

  it('live write round-trips masked secrets', async () => {
    const original = await admin.settings.get();
    const originalDays = original.Settings.RequestHistory.RetentionDays;
    const maskedAdmin = original.Settings.Auth.AdminApiKey;
    const changedDays = originalDays >= 3650 ? originalDays - 1 : originalDays + 1;
    const edited = structuredClone(original.Settings);
    edited.RequestHistory.RetentionDays = changedDays;
    const saved = await admin.settings.update(edited);
    try {
      assert.equal(saved.Settings.RequestHistory.RetentionDays, changedDays);
      assert.equal(saved.Settings.Auth.AdminApiKey, maskedAdmin);
      assert.equal((await admin.whoami.get()).IsAdmin, true, 'admin key stopped working');
      assert.equal((await user.whoami.get()).IsAuthenticated, true, 'access key stopped working');
      assert.equal((await admin.settings.get()).Settings.RequestHistory.RetentionDays, changedDays);
    } finally {
      const restore = (await admin.settings.get()).Settings;
      restore.RequestHistory.RetentionDays = originalDays;
      const restored = await admin.settings.update(restore);
      assert.equal(restored.Settings.RequestHistory.RetentionDays, originalDays);
    }
    const bad = structuredClone(original.Settings);
    bad.Auth.AccessKeys.push({ Name: 'sdk-harness-invalid', Key: '****nope', Active: true });
    await expectApiError(400, () => admin.settings.update(bad));
  });
});

describe('request history', () => {
  it('list and filter', async () => {
    await user.whoami.get();
    const page = await user.requestHistory.list({ pathContains: '/v1.0/whoami', pageSize: 10 });
    assert.ok(page.Items.length > 0);
    assert.ok(page.Items.every((e) => e.Path.includes('/v1.0/whoami')));
    assert.ok(page.Items.every((e) => e.RequestBody === null && e.ResponseBody === null));
    const failures = await user.requestHistory.list({ statusCode: 401, pageSize: 5 });
    assert.ok(failures.Items.length > 0 && failures.Items.every((e) => e.StatusCode === 401 && e.Success === false));
  });

  it('summary', async () => {
    const summary = await user.requestHistory.summary({ bucketMinutes: 30 });
    assert.equal(summary.BucketMinutes, 30);
    assert.ok(summary.Buckets.length >= 48, `buckets ${summary.Buckets.length}`);
    assert.ok(summary.TotalCount > 0 && summary.TotalCount === summary.TotalSuccess + summary.TotalFailure);
  });

  it('read redacts credentials', async () => {
    const page = await user.requestHistory.list({ pathContains: '/v1.0/settings', method: 'GET', statusCode: 200, pageSize: 1 });
    assert.ok(page.Items.length > 0);
    const entry = await user.requestHistory.get(page.Items[0].Id);
    assert.equal(entry.Id, page.Items[0].Id);
    assert.ok(entry.RequestHeaders && Object.keys(entry.RequestHeaders).length > 0);
    assert.ok(!JSON.stringify(entry.RequestHeaders).includes(ADMIN_KEY), 'admin key stored in headers');
    assert.ok(!(entry.ResponseBody || '').includes(`"${ADMIN_KEY}"`), 'admin key stored in response body');
    await expectApiError(404, () => user.requestHistory.get('req_doesnotexist'));
  });

  it('delete (admin) then 404', async (t) => {
    const page = await admin.requestHistory.list({ pathContains: '/openapi.json', pageSize: 1 });
    if (page.Items.length === 0) {
      t.skip('no /openapi.json entry to delete');
      return;
    }
    const id = page.Items[0].Id;
    await admin.requestHistory.delete(id);
    await expectApiError(404, () => admin.requestHistory.get(id));
    await expectApiError(404, () => admin.requestHistory.delete(id));
  });

  it('bulk delete with a no-match filter', async () => {
    const result = await admin.requestHistory.deleteMany({ pathContains: '/no-such-path-' + Date.now() });
    assert.equal(result.DeletedCount, 0);
  });
});
