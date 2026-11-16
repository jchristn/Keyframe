// Central locale registry. Adding a locale means adding a bundle under locales/ and an entry
// here; no application logic changes. Pseudo-locales are generated from English at runtime
// for expansion and RTL testing.

export const LOCALE_STORAGE_KEY = 'keyframe.locale';
export const DEFAULT_LOCALE = 'en';

export const LOCALES = [
  { code: 'en', englishName: 'English', nativeName: 'English', dir: 'ltr', fallback: null, intl: 'en' },
  { code: 'en-XA', englishName: 'Pseudo (expansion)', nativeName: '[Ƥşḗŭḓǿ]', dir: 'ltr', fallback: 'en', intl: 'en', pseudo: 'expand' },
  { code: 'ar-XB', englishName: 'Pseudo (RTL)', nativeName: '[RTL Pseudo]', dir: 'rtl', fallback: 'en', intl: 'en', pseudo: 'rtl' }
];

const ALIASES = {
  'en-us': 'en',
  'en-gb': 'en',
  'en-au': 'en',
  'en-ca': 'en',
  'en-xa': 'en-XA',
  'ar-xb': 'ar-XB'
};

/** Map any BCP 47 tag or alias to a supported locale code, or the default. */
export function canonicalizeLocale(tag) {
  if (!tag) return DEFAULT_LOCALE;
  const lower = String(tag).trim().replace('_', '-').toLowerCase();
  if (ALIASES[lower]) return ALIASES[lower];
  const exact = LOCALES.find((l) => l.code.toLowerCase() === lower);
  if (exact) return exact.code;
  const base = lower.split('-')[0];
  const byBase = LOCALES.find((l) => !l.pseudo && l.code.toLowerCase() === base);
  return byBase ? byBase.code : DEFAULT_LOCALE;
}

export function getLocaleMeta(code) {
  return LOCALES.find((l) => l.code === code) || LOCALES[0];
}

/** Locale to hand to Intl formatters. Pseudo-locales format like their base language. */
export function intlLocale(code) {
  return getLocaleMeta(canonicalizeLocale(code)).intl;
}
