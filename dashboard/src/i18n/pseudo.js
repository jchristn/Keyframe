// Pseudo-locales generated from English so expansion and right-to-left layouts can be
// exercised without a translator. {{interpolation}} tokens are left untouched.

const ACCENTS = {
  a: 'á', b: 'ƀ', c: 'ç', d: 'ḓ', e: 'é', f: 'ƒ', g: 'ĝ', h: 'ĥ', i: 'í', j: 'ĵ', k: 'ķ', l: 'ĺ', m: 'ḿ',
  n: 'ñ', o: 'ó', p: 'ƥ', r: 'ŕ', s: 'š', t: 'ţ', u: 'ú', w: 'ŵ', y: 'ý', z: 'ž',
  A: 'Á', B: 'Ɓ', C: 'Ç', D: 'Ḓ', E: 'É', G: 'Ĝ', H: 'Ĥ', I: 'Í', K: 'Ķ', L: 'Ĺ', N: 'Ñ', O: 'Ó',
  P: 'Ƥ', R: 'Ŕ', S: 'Š', T: 'Ţ', U: 'Ú', W: 'Ŵ', Y: 'Ý', Z: 'Ž'
};

function transformText(text, fn) {
  return text
    .split(/(\{\{[^}]+\}\})/g)
    .map((part) => (part.startsWith('{{') ? part : fn(part)))
    .join('');
}

/** Accented and about 35 percent longer, wrapped in brackets so clipping is easy to spot. */
export function expand(text) {
  const accented = transformText(text, (s) => s.replace(/[A-Za-z]/g, (c) => ACCENTS[c] || c));
  const padding = '~'.repeat(Math.max(2, Math.ceil(text.length * 0.35)));
  return '[' + accented + ' ' + padding + ']';
}

/** Wrapped in right-to-left override marks so bidi handling is exercised. */
export function rtl(text) {
  return text.includes('{{') ? text : '‮' + text + '‬';
}

function mapTree(node, fn) {
  if (typeof node === 'string') return fn(node);
  const out = {};
  for (const [k, v] of Object.entries(node)) out[k] = mapTree(v, fn);
  return out;
}

export function pseudoCatalog(code, en) {
  return mapTree(en, code === 'ar-XB' ? rtl : expand);
}
