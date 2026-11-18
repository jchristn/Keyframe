// Captures the README screenshots (AUDIO_ENHANCEMENTS_PLAN.md section 5.11): drives the dashboard
// with Playwright against a running Keyframe server, seeds the analyses it needs from the factory
// samples, waits for the listening endpoint to report healthy when one is configured, and writes
// ten PNGs at 1280 by 800 in the light theme. Rerun it to regenerate every image.
//
// Usage (from dashboard/):
//   KEYFRAME_KEY=keyframeadmin node scripts/capture-screenshots.mjs
//
// Environment:
//   KEYFRAME_URL        server URL (default http://127.0.0.1:8700)
//   KEYFRAME_KEY        admin key (required; Analytics and Settings need it)
//   DASHBOARD_URL       dashboard URL (default http://127.0.0.1:8701)
//   SCREENSHOT_DIR      output directory (default ../assets/screenshots)
//   SAMPLES_DIR         directory holding sample.jpg, sample.mp4, sample.mp3 (default ../docker/factory/samples)
//   LISTEN_TIMEOUT_MS   how long to wait for the listening endpoint to become healthy (default 900000)
//   JOB_TIMEOUT_MS      how long to wait for each seeded job (default 900000)
//   PLAYWRIGHT_MODULE   path to a playwright package directory, when 'playwright' is not installed here
//
// The key is typed into the login form only. Before every capture the script blanks password
// fields and masked secrets, and the server URL should be a 127.0.0.1 address, so no secret
// reaches an image. Functions passed to page.evaluate run in the browser, hence the globals below.
/* global document, window, PopStateEvent */
import { mkdirSync, readFileSync } from 'node:fs';
import { dirname, isAbsolute, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const dashboardRoot = join(here, '..');
const repoRoot = join(dashboardRoot, '..');

const SERVER_URL = (process.env.KEYFRAME_URL || 'http://127.0.0.1:8700').replace(/\/+$/, '');
const KEY = process.env.KEYFRAME_KEY || '';
const DASHBOARD_URL = (process.env.DASHBOARD_URL || 'http://127.0.0.1:8701').replace(/\/+$/, '');
const OUT_DIR = absolute(process.env.SCREENSHOT_DIR || join(repoRoot, 'assets', 'screenshots'));
const SAMPLES_DIR = absolute(process.env.SAMPLES_DIR || join(repoRoot, 'docker', 'factory', 'samples'));
const LISTEN_TIMEOUT_MS = Number(process.env.LISTEN_TIMEOUT_MS || 900000);
const JOB_TIMEOUT_MS = Number(process.env.JOB_TIMEOUT_MS || 900000);
const VIEWPORT = { width: 1280, height: 800 };

function absolute(p) {
  return isAbsolute(p) ? p : resolve(process.cwd(), p);
}

function log(message) {
  console.log('[capture] ' + message);
}

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

async function loadPlaywright() {
  try {
    return await import('playwright');
  } catch (err) {
    if (!process.env.PLAYWRIGHT_MODULE) {
      throw new Error("Playwright is not installed. Run 'npm install --no-save playwright' or set PLAYWRIGHT_MODULE to a playwright package directory. (" + err.message + ')');
    }
    const mod = await import(pathToFileURL(join(absolute(process.env.PLAYWRIGHT_MODULE), 'index.mjs')).href);
    return mod.default && mod.default.chromium ? mod.default : mod;
  }
}

async function api(method, path, body) {
  const res = await fetch(SERVER_URL + path, {
    method,
    headers: { Accept: 'application/json', Authorization: 'Bearer ' + KEY, ...(body ? { 'Content-Type': 'application/json' } : {}) },
    body: body ? JSON.stringify(body) : undefined
  });
  const text = await res.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }
  if (!res.ok) throw new Error(method + ' ' + path + ' returned ' + res.status + ': ' + (typeof data === 'string' ? data : JSON.stringify(data)));
  return data;
}

function sample(name, mediaType) {
  return { Data: readFileSync(join(SAMPLES_DIR, name)).toString('base64'), MediaType: mediaType };
}

async function waitForServer() {
  const until = Date.now() + 60000;
  for (;;) {
    try {
      await api('GET', '/v1.0/health');
      return;
    } catch (err) {
      if (Date.now() > until) throw new Error('The server at ' + SERVER_URL + ' is not reachable: ' + err.message);
      await sleep(1000);
    }
  }
}

/** Wait until Inference.DefaultListening reports healthy, so audio screenshots show a listened result. */
async function waitForListening() {
  const settings = await api('GET', '/v1.0/settings');
  const name = settings && settings.Settings && settings.Settings.Inference ? settings.Settings.Inference.DefaultListening : null;
  if (!name) {
    log('no default listening endpoint is configured; audio screenshots will show the transcript fallback');
    return;
  }
  const until = Date.now() + LISTEN_TIMEOUT_MS;
  log('waiting for listening endpoint ' + name + ' to report healthy');
  for (;;) {
    const endpoints = await api('GET', '/v1.0/endpoints');
    const e = (endpoints || []).find((x) => String(x.Name).toLowerCase() === String(name).toLowerCase());
    if (!e) throw new Error('Inference.DefaultListening names ' + name + ', which is not a configured endpoint.');
    if (e.Healthy) {
      log(name + ' is healthy');
      return;
    }
    if (Date.now() > until) throw new Error(name + ' did not become healthy within ' + LISTEN_TIMEOUT_MS + ' ms: ' + (e.HealthMessage || 'no message'));
    await sleep(5000);
  }
}

async function waitForJob(id) {
  const until = Date.now() + JOB_TIMEOUT_MS;
  for (;;) {
    const job = await api('GET', '/v1.0/jobs/' + encodeURIComponent(id));
    if (job.Status === 'Succeeded') return job;
    if (['Failed', 'Canceled', 'Interrupted'].includes(job.Status)) throw new Error('Job ' + id + ' ended ' + job.Status + ': ' + (job.Error || ''));
    if (Date.now() > until) throw new Error('Job ' + id + ' did not finish within ' + JOB_TIMEOUT_MS + ' ms');
    await sleep(2000);
  }
}

async function seed() {
  log('seeding an image analysis');
  await api('POST', '/v1.0/image', {
    Image: sample('sample.jpg', 'image/jpeg'),
    Functions: {
      Summarize: { Enable: true, MaxWords: 60 },
      Extract: { Enable: true, Fields: [{ Key: 'objects', Type: 'StringList', Prompt: 'Objects you can see.' }, { Key: 'hasPeople', Type: 'Boolean', Prompt: 'Are there people?' }] }
    }
  });

  log('seeding a video job with scenes and scene audio');
  const video = await api('POST', '/v1.0/video', {
    Video: sample('sample.mp4', 'video/mp4'),
    IncludeKeyframeData: true,
    Functions: {
      Summarize: { Enable: true, MaxWords: 80 },
      Extract: { Enable: true, Fields: [{ Key: 'topics', Type: 'StringList', Prompt: 'Topics covered.' }] },
      Scenes: { Enable: true, Detection: { MinSceneMs: 2000 }, Summarize: { Enable: true, MaxWords: 30 }, Audio: { Enable: true } },
      Transcribe: { Enable: true }
    }
  });

  log('seeding an audio job with sections');
  const audio = await api('POST', '/v1.0/audio', {
    Audio: sample('sample.mp3', 'audio/mpeg'),
    Listen: { Enable: true },
    Functions: {
      Transcribe: { Enable: true },
      Measure: { Enable: true },
      Summarize: { Enable: true, MaxWords: 80 },
      Extract: { Enable: true, Fields: [{ Key: 'recording', Type: 'Enum', Options: ['Live', 'Studio', 'Rehearsal', 'Demo'], Prompt: 'How was this recorded?' }] },
      Sections: { Enable: true, Detection: { MinSilenceMs: 1000, MinSectionMs: 10000 }, Summarize: { Enable: true, MaxWords: 40 } }
    }
  });

  const videoJob = await waitForJob(video.Id);
  const audioJob = await waitForJob(audio.Id);
  log('seeded video job ' + videoJob.Id + ' and audio job ' + audioJob.Id);
  return { videoJobId: videoJob.Id, audioJobId: audioJob.Id };
}

/** Blank every key field and masked secret on the page before a capture. */
async function blankSecrets(page) {
  await page.evaluate(() => {
    for (const input of document.querySelectorAll('input[type="password"], input[autocomplete="new-password"], input[autocomplete="current-password"]')) input.value = '';
    for (const badge of document.querySelectorAll('.secret-display .badge')) badge.textContent = '****';
  });
}

async function capture(page, name, prepare) {
  await prepare();
  await page.waitForTimeout(600);
  for (const close of await page.locator('.toast-region .icon-button').all()) await close.click().catch(() => {});
  await blankSecrets(page);
  const file = join(OUT_DIR, name);
  await page.screenshot({ path: file });
  log('wrote ' + file);
}

async function scrollTo(page, selector, offset = 72) {
  await page.locator(selector).first().waitFor();
  await page.evaluate(
    ([sel, off]) => {
      const el = document.querySelector(sel);
      const main = document.querySelector('main.workspace') || document.scrollingElement;
      if (!el) return;
      const top = el.getBoundingClientRect().top - (main.getBoundingClientRect ? main.getBoundingClientRect().top : 0) + main.scrollTop - off;
      main.scrollTo({ top, behavior: 'instant' });
      if (main !== document.scrollingElement) document.scrollingElement.scrollTo({ top: 0 });
    },
    [selector, offset]
  );
}

async function login(page) {
  await page.goto(DASHBOARD_URL + '/login');
  await page.getByLabel('Server URL').fill(SERVER_URL);
  await page.getByLabel('API key').fill(KEY);
  const remember = page.getByLabel('Remember key on this device');
  if (await remember.isChecked()) await remember.uncheck();
  await page.getByRole('button', { name: 'Connect' }).click();
  await page.waitForURL(/\/dashboard\//);
}

/** Navigate inside the single-page app without a reload, so the in-memory key survives. */
async function go(page, path) {
  await page.evaluate((p) => {
    window.history.pushState(null, '', p);
    window.dispatchEvent(new PopStateEvent('popstate'));
  }, path);
  await page.waitForURL('**' + path.split('#')[0]);
}

async function main() {
  if (!KEY) throw new Error('Set KEYFRAME_KEY to the admin key.');
  mkdirSync(OUT_DIR, { recursive: true });
  await waitForServer();
  await waitForListening();
  const seeded = await seed();

  const { chromium } = await loadPlaywright();
  const browser = await chromium.launch();
  try {
    const context = await browser.newContext({ viewport: VIEWPORT, colorScheme: 'light', locale: 'en-US' });
    await context.addInitScript(() => {
      try {
        localStorage.setItem('keyframe.appearance', JSON.stringify({ palette: 'slate', mode: 'light' }));
        localStorage.setItem('keyframe.locale', 'en');
      } catch {
        // Storage can be unavailable; the defaults are light and English anyway.
      }
    });
    const page = await context.newPage();
    await login(page);

    await capture(page, 'home.png', async () => {
      await go(page, '/dashboard/home');
      await page.locator('.chart-wrap svg').first().waitFor();
    });

    await capture(page, 'analyze-image.png', async () => {
      await go(page, '/dashboard/analyze/image');
      await page.locator('input[type="file"]').first().setInputFiles(join(SAMPLES_DIR, 'sample.jpg'));
      await page.locator('form.analyze-form button[type="submit"]').click();
      await page.getByRole('tab', { name: 'Telemetry' }).first().waitFor({ timeout: JOB_TIMEOUT_MS });
      await page.getByRole('tab', { name: 'Telemetry' }).first().click();
      await scrollTo(page, '.analyze-result');
    });

    await capture(page, 'video-job.png', async () => {
      await go(page, '/dashboard/jobs/' + encodeURIComponent(seeded.videoJobId));
      await page.getByRole('tab', { name: /Scenes/ }).click();
      await page.locator('.scene-strip').waitFor();
      await scrollTo(page, '[data-testid="listening-card"]');
    });

    await capture(page, 'analyze-audio.png', async () => {
      await go(page, '/dashboard/analyze/audio');
      await page.locator('[data-testid="listen-panel"] .option-details > summary').click();
      await page.getByRole('checkbox', { name: 'Split the recording at silences and analyze each section' }).check();
      await page.getByRole('checkbox', { name: 'Extract typed fields' }).check();
      await scrollTo(page, '[data-testid="listen-panel"]');
    });

    await capture(page, 'audio-result.png', async () => {
      await go(page, '/dashboard/analyze/audio');
      await page.locator('input[type="file"]').first().setInputFiles(join(SAMPLES_DIR, 'sample.mp3'));
      await page.getByRole('button', { name: 'Submit audio job' }).click();
      await page.getByRole('tab', { name: 'Measurements' }).waitFor({ timeout: JOB_TIMEOUT_MS });
      await page.getByRole('tab', { name: 'Measurements' }).click();
      await scrollTo(page, '.analyze-result [data-testid="listening-card"]');
    });

    await capture(page, 'audio-sections.png', async () => {
      await go(page, '/dashboard/jobs/' + encodeURIComponent(seeded.audioJobId));
      await page.getByRole('tab', { name: /Sections/ }).click();
      await page.locator('.section-bar').waitFor();
      // The job was uploaded inline, so pick the original file to enable the clip downloads.
      const pick = page.locator('.segment-note input[type="file"]');
      if (await pick.count()) await pick.setInputFiles(join(SAMPLES_DIR, 'sample.mp3'));
      await page.locator('.section-bar').focus();
      await page.keyboard.press('ArrowRight');
      await scrollTo(page, '.tabs');
    });

    await capture(page, 'endpoints.png', async () => {
      await go(page, '/dashboard/endpoints');
      await page.locator('.capability-badges').first().waitFor();
    });

    await capture(page, 'analytics.png', async () => {
      await go(page, '/dashboard/analytics');
      await page.getByRole('img', { name: /Analysis time percentiles/ }).waitFor();
      await scrollTo(page, '.chart-panel:has(svg[aria-label*="percentiles"])');
    });

    await capture(page, 'request-history.png', async () => {
      await go(page, '/dashboard/request-history');
      const row = page.locator('table.data-table tbody tr').first();
      await row.waitFor();
      await row.click();
      await page.getByRole('dialog').waitFor();
    });

    await capture(page, 'settings.png', async () => {
      await go(page, '/dashboard/settings');
      await page.locator('#settings-media-listen').waitFor();
      await scrollTo(page, '#settings-media-listen', 24);
    });
  } finally {
    await browser.close();
  }
  log('done: ten screenshots in ' + OUT_DIR);
}

main().catch((err) => {
  console.error('[capture] ' + err.message);
  process.exit(1);
});
