// Checks that every translation key referenced in src/ exists in the English bundle.
// Static keys are string literals such as 'common.save'; dynamic keys built as
// 'enums.jobStatus.' + value are checked against the known value lists below.
// Usage: node scripts/check-i18n.mjs [--list]
import { readFileSync, readdirSync, statSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const src = join(root, 'src');
const en = JSON.parse(readFileSync(join(src, 'i18n', 'locales', 'en.json'), 'utf8'));

const NAMESPACES = Object.keys(en);

function walk(dir, out = []) {
  for (const name of readdirSync(dir)) {
    const p = join(dir, name);
    if (statSync(p).isDirectory()) walk(p, out);
    else if (/\.(js|jsx)$/.test(name) && !/\.test\.(js|jsx)$/.test(name)) out.push(p);
  }
  return out;
}

export function flatten(obj, prefix = '', out = {}) {
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? prefix + '.' + k : k;
    if (v && typeof v === 'object') flatten(v, key, out);
    else out[key] = v;
  }
  return out;
}

export function collectKeys() {
  const keys = new Set();
  const pattern = new RegExp("['\"`]((?:" + NAMESPACES.join('|') + ")\\.[A-Za-z0-9_.]*[A-Za-z0-9_])['\"`]", 'g');
  for (const file of walk(src)) {
    const text = readFileSync(file, 'utf8');
    for (const m of text.matchAll(pattern)) keys.add(m[1]);
  }
  return keys;
}

// Dynamic key families and every value they can take.
export const DYNAMIC = {
  'enums.jobStatus': ['Queued', 'Running', 'Succeeded', 'Failed', 'Canceled', 'Interrupted'],
  'enums.fieldType': ['Boolean', 'String', 'Integer', 'Number', 'Enum', 'StringList', 'Object'],
  'enums.mediaKind': ['Unknown', 'Image', 'Video', 'Audio'],
  'enums.summaryStyle': ['Paragraph', 'Bullets', 'Caption'],
  'enums.extractMode': ['Combined', 'PerField'],
  'enums.extractModeHelp': ['Combined', 'PerField'],
  'enums.endpointKind': ['Completion', 'Transcription'],
  'enums.provider': ['Mock', 'Ollama', 'OllamaCloud', 'OpenAi', 'OpenAiCompatible', 'AzureOpenAi', 'Anthropic', 'Gemini', 'VertexAi', 'Bedrock', 'XAi', 'Cohere'],
  'enums.stage': ['Resolve', 'Probe', 'Normalize', 'SceneDetection', 'FrameExtraction', 'Measure', 'AudioExtraction', 'SectionDetection', 'ListenExtraction', 'Transcribe', 'Scenes', 'Sections', 'Summarize', 'Extract', 'Reduce'],
  'enums.progressStage': ['Queued', 'Done', 'Complete', 'Resolve', 'Probe', 'Measure', 'AudioExtraction', 'Transcribe', 'SceneDetection', 'FrameExtraction', 'Scenes', 'SectionDetection', 'Listen', 'ListenExtraction', 'Sections', 'Reduce', 'Summarize', 'Extract'],
  'enums.purpose': ['Summarize', 'Extract', 'SceneSummarize', 'SceneExtract', 'SectionSummarize', 'SectionExtract', 'Transcribe'],
  'enums.listeningMode': ['Full', 'Sections', 'Excerpts', 'Scenes', 'None'],
  'enums.listenReason': ['NotRequested', 'NoModelFunctions', 'NoEndpointConfigured', 'EndpointLacksAudioInput', 'EndpointUnhealthy', 'NoAudioTrack'],
  'enums.listenReasonHelp': ['NotRequested', 'NoModelFunctions', 'NoEndpointConfigured', 'EndpointLacksAudioInput', 'EndpointUnhealthy', 'NoAudioTrack'],
  'enums.listenScope': ['Recording', 'Section', 'Scene'],
  'enums.listenFormat': ['Mp3', 'Wav', 'Flac'],
  'listening.modeHelp': ['Sections', 'Excerpts'],
  'units': ['lufs', 'lu', 'dbtp', 'dbfs', 'db', 'kilohertz'],
  'measurements.fields': ['IntegratedLufs', 'LoudnessRangeLu', 'TruePeakDbtp', 'PeakDbfs', 'RmsDbfs', 'CrestFactorDb', 'PeakCount', 'ClippingLikely', 'SilenceRatio', 'SilenceCount', 'LeadingSilenceMs', 'TrailingSilenceMs', 'Channels', 'StereoWidthDb', 'EffectivelyMono'],
  'analyzeAudio.listen.status': ['ok', 'unchecked', 'unknown', 'none', 'noDefault', 'unhealthy', 'allUnhealthy'],
  'endpoints.capabilities': ['images', 'structuredOutput', 'audioInput', 'transcription'].flatMap((c) => [c, c + 'Help']),
  'settings.subgroups': ['listen', 'measure'].flatMap((g) => [g + '.title', g + '.help']),
  'enums.health': ['Healthy', 'Degraded'],
  'chart.ranges': ['hour', 'day', 'week', 'month'],
  'settings.groups': ['rest', 'auth', 'database', 'inference', 'media', 'jobs', 'requestHistory', 'analytics', 'logging', 'telemetry'].flatMap((g) => [g + '.title', g + '.help']),
  'settings.severity': ['0', '1', '2', '3', '4', '5', '6', '7'],
  'settings.addItem': ['Auth.AccessKeys', 'Inference.Endpoints', 'Logging.Servers'],
  'snippets.languages': ['curl', 'fetch', 'python', 'csharp'],
  'externalServices.services': ['grafana', 'prometheus', 'tempo'].flatMap((s) => [s + '.name', s + '.purpose']),
  'appearance.palettes': ['slate', 'indigoNight', 'fjord', 'nocturne', 'solstice', 'cocoa', 'duskRose', 'metroNight', 'forest', 'ocean', 'amber', 'graphite', 'lavender', 'mint', 'sandstone', 'terracotta'],
  'appearance.modes': ['light', 'dark', 'system'],
  'analytics.metrics': ['AnalysisMs', 'QueueWaitMs', 'ModelCallMs', 'TimeToFirstTokenMs', 'GenerationMs', 'TokensPerSecond', 'TokensPerAnalysis', 'RealtimeFactor', 'MediaDurationMs', 'MediaBytes'],
  'analytics.stats': ['avg', 'p50', 'p90', 'p95', 'p99', 'max'],
  'analytics.ranges': ['1h', '24h', '7d', '30d', 'custom'],
  'analytics.buckets': ['minute', 'hour', 'day'],
  'analytics.totals': ['promptTokens', 'completionTokens', 'cachedTokens', 'reasoningTokens', 'totalTokens', 'tokensPerAnalysisP50', 'tokensPerAnalysisP95', 'modelCalls', 'failedModelCalls', 'retries', 'repairAttempts', 'inputImages', 'inputImageBytes', 'inputAudioBytes', 'scenes', 'transcriptCharacters'],
  'analytics.detail.fields': ['kind', 'principal', 'started', 'completed', 'analysisTime', 'queueWait', 'mediaDuration', 'mediaSize', 'realtimeFactor', 'visionModel', 'textModel', 'transcriptionModel', 'modelCalls', 'retries', 'promptTokens', 'completionTokens', 'cachedTokens', 'reasoningTokens', 'totalTokens', 'inputImages', 'inputAudio', 'scenes', 'transcriptCharacters', 'fields'],
  'segments.download': ['Video', 'Audio', 'Frame'],
  'segments.hint': ['Video', 'Audio', 'Frame']
};

export function settingsFieldKeys(schema) {
  const out = [];
  for (const section of schema) {
    for (const f of section.fields) out.push('settings.fields.' + f.path);
    for (const arr of section.arrays || []) {
      out.push('settings.fields.' + arr.path + '._');
      for (const f of arr.itemFields) out.push('settings.fields.' + arr.path + '.' + f.name);
    }
  }
  return out;
}

export async function findMissing() {
  const flat = flatten(en);
  const { SETTINGS_SECTIONS } = await import('../src/utils/settingsSchema.js');
  const wanted = new Set(collectKeys());
  for (const [prefix, values] of Object.entries(DYNAMIC)) for (const v of values) wanted.add(prefix + '.' + v);
  for (const k of settingsFieldKeys(SETTINGS_SECTIONS)) wanted.add(k);
  // Keys that are prefixes of dynamic families appear as literals; drop them.
  const has = (k) => k in flat || k + '_one' in flat || k + '_other' in flat;
  const missing = [...wanted].filter((k) => !has(k) && !Object.keys(DYNAMIC).includes(k) && !Object.keys(flat).some((f) => f.startsWith(k + '.')));
  return missing.sort();
}

// ---------------------------------------------------------------- hard-coded strings
// JSX text that is not prose: punctuation, digits, symbols, and product or protocol names
// that stay untranslated in every language (see README, Internationalization).
const TEXT_ALLOW = /^[\s\d.,:;!?()[\]{}/\\|+*=<>#@&%$'"`~^_·…×→←↑↓*-]*$|^(Keyframe|JSON|HTTP|URL|ID|ffmpeg)$/;
const ATTR_ALLOW = /^(https?:\/\/?|#|\*|·)$/;

function stripComments(code) {
  return code.replace(/\/\*[\s\S]*?\*\//g, '').replace(/(^|[^:'"`\\])\/\/.*$/gm, '$1');
}

/** User-facing literals in JSX: text nodes and placeholder, title, aria-label, and alt values. */
export function findHardcoded() {
  const problems = [];
  const jsxText = /(?<![=-])>([^<>{}]*[A-Za-z\u00c0-\uffff][^<>{}]*)</g;
  const attrRe = /\s(placeholder|title|aria-label|alt|label)="([^"]*[A-Za-z][^"]*)"/g;
  const literalProp = /\b(label|title|placeholder|message|summary|emptyTitle|emptyMessage|confirmLabel)=\{\s*'([^']*[A-Za-z][^']*)'\s*\}/g;
  for (const file of walk(src).filter((f) => f.endsWith('.jsx'))) {
    const text = stripComments(readFileSync(file, 'utf8'));
    const rel = file.slice(src.length + 1);
    let m;
    while ((m = jsxText.exec(text))) {
      const value = m[1].trim();
      if (!value || TEXT_ALLOW.test(value) || /[=;]|=>|&&|\|\||\(\)/.test(value) || /^[a-z][A-Za-z0-9]*$/.test(value) || /'\s*\+|\+\s*'/.test(value) || /^[A-Z_]+ \?/.test(value) || /\? \($/.test(value)) continue;
      problems.push(rel + ': text "' + value + '"');
    }
    while ((m = attrRe.exec(text))) {
      if (ATTR_ALLOW.test(m[2])) continue;
      problems.push(rel + ': ' + m[1] + '="' + m[2] + '"');
    }
    while ((m = literalProp.exec(text))) problems.push(rel + ': ' + m[1] + "={'" + m[2] + "'}");
  }
  return problems;
}

// ---------------------------------------------------------------- orphaned keys
// Key prefixes that are built from data at runtime (enum values, settings paths, template keys).
const GENERATED_PREFIXES = [
  'enums.',
  'settings.fields.',
  'settings.groups.',
  'settings.severity.',
  'settings.addItem.',
  'settings.badges.',
  'settings.options.',
  'errors.',
  'explorer.tabs.',
  'results.tabs.',
  'jobDetail.tabs.',
  'jobDetail.meta.',
  'properties.',
  'telemetry.',
  'chart.',
  'status.',
  'roles.'
];

/** English keys that no source file references and no generated family covers. */
export function findOrphans() {
  const flat = flatten(en);
  const used = collectKeys();
  const sources = walk(src).map((f) => readFileSync(f, 'utf8')).join('\n');
  const dynamic = Object.entries(DYNAMIC).flatMap(([prefix, values]) => values.map((v) => prefix + '.' + v));
  const orphans = [];
  for (const key of Object.keys(flat)) {
    const base = key.replace(/_(zero|one|two|few|many|other)$/, '');
    if (used.has(base) || dynamic.includes(base)) continue;
    if (GENERATED_PREFIXES.some((p) => base.startsWith(p))) continue;
    // A key referenced by a template such as t('explorer.' + kind + 'Params') shows up as its prefix.
    const parent = base.slice(0, base.lastIndexOf('.') + 1);
    if (sources.includes("'" + parent + "'") || sources.includes("'" + parent + "' +")) continue;
    orphans.push(key);
  }
  return orphans.sort();
}

// ---------------------------------------------------------------- locale parity
function placeholders(text) {
  return [...String(text).matchAll(/\{\{\s*([A-Za-z0-9_]+)\s*\}\}/g)].map((m) => m[1]).sort().join(',');
}

/**
 * Compare a translated catalog with English: missing keys, extra keys, placeholder mismatches,
 * empty values, and forbidden dashes. Plural suffixes may differ by language (for example
 * Arabic adds _zero, _two, _few, _many), so keys compare without the suffix.
 */
export function compareCatalog(catalog) {
  const strip = (k) => k.replace(/_(zero|one|two|few|many|other)$/, '');
  const enFlat = flatten(en);
  const flat = flatten(catalog);
  const enBase = new Map();
  for (const [k, v] of Object.entries(enFlat)) enBase.set(strip(k), v);
  const base = new Map();
  for (const [k, v] of Object.entries(flat)) base.set(strip(k), v);
  const missing = [...enBase.keys()].filter((k) => !base.has(k));
  const extra = [...base.keys()].filter((k) => !enBase.has(k));
  const mismatched = [];
  const empty = [];
  const dashes = [];
  for (const [k, v] of Object.entries(flat)) {
    // A form English lacks (_few, _many, _zero, _two) is compared with English _other.
    const ek = enFlat[k] !== undefined ? k : enFlat[strip(k) + '_other'] !== undefined ? strip(k) + '_other' : Object.keys(enFlat).find((x) => strip(x) === strip(k));
    if (typeof v !== 'string' || !v.trim()) empty.push(k);
    else if (ek && placeholders(enFlat[ek]).split(',').filter(Boolean).some((p) => p !== 'count' && !placeholders(v).split(',').includes(p))) mismatched.push(k);
    if (typeof v === 'string' && /[\u2014\u2013]/.test(v)) dashes.push(k);
  }
  return { missing, extra, mismatched, empty, dashes };
}

export function localeFiles() {
  return readdirSync(join(src, 'i18n', 'locales')).filter((f) => f.endsWith('.json') && f !== 'en.json');
}

export function readLocale(file) {
  return JSON.parse(readFileSync(join(src, 'i18n', 'locales', file), 'utf8'));
}

if (process.argv[1] && process.argv[1].endsWith('check-i18n.mjs')) {
  const missing = await findMissing();
  if (process.argv.includes('--list')) for (const k of [...collectKeys()].sort()) console.log(k);
  if (missing.length) {
    console.error('Missing translation keys (' + missing.length + '):');
    for (const k of missing) console.error('  ' + k);
    process.exit(1);
  }
  let failed = false;
  const hard = findHardcoded();
  if (hard.length) {
    failed = true;
    console.error('Hard-coded UI strings (' + hard.length + '):');
    for (const h of hard) console.error('  ' + h);
  }
  const orphans = findOrphans();
  if (orphans.length) {
    failed = true;
    console.error('Orphaned keys (' + orphans.length + '):');
    for (const o of orphans) console.error('  ' + o);
  }
  for (const file of localeFiles()) {
    const r = compareCatalog(readLocale(file));
    for (const [kind, list] of Object.entries(r)) {
      if (!list.length) continue;
      failed = true;
      console.error(file + ' ' + kind + ' (' + list.length + '): ' + list.slice(0, 20).join(', '));
    }
  }
  if (failed) process.exit(1);
  console.log('[check-i18n] keys, hard-coded strings, orphans, and locale parity all pass');
}
