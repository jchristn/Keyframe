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

const fx = { dir: null, image: null, video: null, longVideo: null, available: false, reason: null };

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
  try {
    execFileSync(FFMPEG, ['-y', '-loglevel', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=640x360', '-frames:v', '1', fx.image]);
    execFileSync(FFMPEG, videoArgs(6, fx.video));
    execFileSync(FFMPEG, videoArgs(30, fx.longVideo));
    fx.available = existsSync(fx.image) && existsSync(fx.video) && existsSync(fx.longVideo);
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

describe('endpoints', () => {
  it('list endpoints', async () => {
    const endpoints = await user.endpoints.list();
    const mock = endpoints.find((e) => e.Name === 'mock');
    assert.ok(mock);
    assert.equal(mock.Provider, 'Mock');
    assert.equal(mock.IsDefault, true);
    assert.ok(endpoints.some((e) => e.Kind === 'Transcription'));
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
