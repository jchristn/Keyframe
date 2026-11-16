// Translation resources. English is the source bundle; pseudo-locales are derived from it
// so expansion and RTL layouts can be exercised without a translator.
import en from './locales/en.json';

const ACCENTS = {
  a: 'á', b: 'ƀ', c: 'ç', d: 'ḓ', e: 'é', f: 'ƒ', g: 'ĝ', h: 'ĥ', i: 'í', j: 'ĵ', k: 'ķ', l: 'ĺ', m: 'ḿ',
  n: 'ñ', o: 'ó', p: 'ƥ', r: 'ŕ', s: 'š', t: 'ţ', u: 'ú', w: 'ŵ', y: 'ý', z: 'ž',
  A: 'Á', B: 'Ɓ', C: 'Ç', D: 'Ḓ', E: 'É', G: 'Ĝ', H: 'Ĥ', I: 'Í', K: 'Ķ', L: 'Ĺ', N: 'Ñ', O: 'Ó',
  P: 'Ƥ', R: 'Ŕ', S: 'Š', T: 'Ţ', U: 'Ú', W: 'Ŵ', Y: 'Ý', Z: 'Ž'
};

// Leave {{interpolation}} tokens untouched.
function transformText(text, fn) {
  return text
    .split(/(\{\{[^}]+\}\})/g)
    .map((part) => (part.startsWith('{{') ? part : fn(part)))
    .join('');
}

function expand(text) {
  const accented = transformText(text, (s) => s.replace(/[A-Za-z]/g, (c) => ACCENTS[c] || c));
  const padding = '~'.repeat(Math.max(2, Math.ceil(text.length * 0.35)));
  return '[' + accented + ' ' + padding + ']';
}

function rtl(text) {
  return '‮' + text + '‬';
}

function mapTree(node, fn) {
  if (typeof node === 'string') return fn(node);
  const out = {};
  for (const [k, v] of Object.entries(node)) out[k] = mapTree(v, fn);
  return out;
}

export const resources = {
  en: { translation: en },
  'en-XA': { translation: mapTree(en, expand) },
  'ar-XB': { translation: mapTree(en, (s) => (s.includes('{{') ? s : rtl(s))) }
};

export default resources;
