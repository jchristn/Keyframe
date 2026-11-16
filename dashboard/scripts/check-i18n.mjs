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
  'enums.stage': ['Resolve', 'Probe', 'Normalize', 'SceneDetection', 'FrameExtraction', 'AudioExtraction', 'Transcribe', 'Scenes', 'Summarize', 'Extract', 'Reduce'],
  'enums.purpose': ['Summarize', 'Extract', 'SceneSummarize', 'SceneExtract', 'Transcribe'],
  'enums.health': ['Healthy', 'Degraded'],
  'chart.ranges': ['hour', 'day', 'week', 'month'],
  'settings.groups': ['rest', 'auth', 'database', 'inference', 'media', 'jobs', 'requestHistory', 'logging', 'telemetry'].flatMap((g) => [g + '.title', g + '.help']),
  'settings.severity': ['0', '1', '2', '3', '4', '5', '6', '7'],
  'settings.addItem': ['Auth.AccessKeys', 'Inference.Endpoints', 'Logging.Servers'],
  'snippets.languages': ['curl', 'fetch', 'python', 'csharp'],
  'externalServices.services': ['grafana', 'prometheus', 'tempo'].flatMap((s) => [s + '.name', s + '.purpose'])
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

if (process.argv[1] && process.argv[1].endsWith('check-i18n.mjs')) {
  const missing = await findMissing();
  if (process.argv.includes('--list')) for (const k of [...collectKeys()].sort()) console.log(k);
  if (missing.length) {
    console.error('Missing translation keys (' + missing.length + '):');
    for (const k of missing) console.error('  ' + k);
    process.exit(1);
  }
  console.log('[check-i18n] all referenced keys exist in en.json');
}
