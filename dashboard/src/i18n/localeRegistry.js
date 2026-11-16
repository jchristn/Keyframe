// Central locale registry. Adding a locale means adding src/i18n/locales/<code>.json and an
// entry here; no application logic changes. Pseudo-locales are generated from English at
// runtime for expansion and RTL testing.
//
// Fields: code (BCP 47, also the catalog file name), englishName, nativeName (the autonym
// shown in the selector), dir, fallback, intl (tag handed to Intl formatters), variant
// (regional variant the catalog is written in), aliases (lower-case tags that resolve here),
// fontFamily (optional script-first font stack applied as --locale-font), pseudo.

export const LOCALE_STORAGE_KEY = 'keyframe.locale';
export const DEFAULT_LOCALE = 'en';

const FONT_BASE = "Inter, -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, 'Noto Sans'";
const FONT_ZH = FONT_BASE + ", 'PingFang SC', 'Hiragino Sans GB', 'Microsoft YaHei', 'Noto Sans SC', 'Noto Sans CJK SC', sans-serif";
const FONT_JA = FONT_BASE + ", 'Hiragino Sans', 'Hiragino Kaku Gothic ProN', 'Yu Gothic', Meiryo, 'Noto Sans JP', 'Noto Sans CJK JP', sans-serif";
const FONT_HI = FONT_BASE + ", 'Noto Sans Devanagari', 'Kohinoor Devanagari', 'Nirmala UI', Mangal, sans-serif";
const FONT_AR = "'Noto Sans Arabic', 'Geeza Pro', 'Segoe UI', Tahoma, " + FONT_BASE + ', sans-serif';

export const LOCALES = [
  { code: 'en', englishName: 'English', nativeName: 'English', dir: 'ltr', fallback: null, intl: 'en', aliases: ['en-us', 'en-gb', 'en-au', 'en-ca', 'english'] },
  { code: 'es', englishName: 'Spanish', nativeName: 'Español', dir: 'ltr', fallback: 'en', intl: 'es', aliases: ['es-es', 'es-mx', 'es-419', 'spanish'] },
  { code: 'pt', englishName: 'Portuguese', nativeName: 'Português', dir: 'ltr', fallback: 'en', intl: 'pt-BR', variant: 'pt-BR', aliases: ['pt-br', 'pt-pt', 'portuguese'] },
  { code: 'fr', englishName: 'French', nativeName: 'Français', dir: 'ltr', fallback: 'en', intl: 'fr', aliases: ['fr-fr', 'fr-ca', 'fr-be', 'fr-ch', 'french'] },
  { code: 'it', englishName: 'Italian', nativeName: 'Italiano', dir: 'ltr', fallback: 'en', intl: 'it', aliases: ['it-it', 'it-ch', 'italian'] },
  { code: 'de', englishName: 'German', nativeName: 'Deutsch', dir: 'ltr', fallback: 'en', intl: 'de', aliases: ['de-de', 'de-at', 'de-ch', 'german'] },
  {
    code: 'zh',
    englishName: 'Mandarin Chinese',
    nativeName: '中文',
    dir: 'ltr',
    fallback: 'en',
    intl: 'zh-Hans',
    variant: 'zh-Hans',
    aliases: ['zh-hans', 'zh-cn', 'zh-sg', 'zh-hans-cn', 'cmn', 'chinese', 'mandarin'],
    fontFamily: FONT_ZH
  },
  { code: 'ar', englishName: 'Arabic', nativeName: 'العربية', dir: 'rtl', fallback: 'en', intl: 'ar', aliases: ['ar-sa', 'ar-eg', 'ar-ae', 'arabic'], fontFamily: FONT_AR },
  { code: 'ru', englishName: 'Russian', nativeName: 'Русский', dir: 'ltr', fallback: 'en', intl: 'ru', aliases: ['ru-ru', 'russian'] },
  { code: 'ms', englishName: 'Malay', nativeName: 'Bahasa Melayu', dir: 'ltr', fallback: 'en', intl: 'ms', aliases: ['ms-my', 'malay'] },
  { code: 'hi', englishName: 'Hindi', nativeName: 'हिन्दी', dir: 'ltr', fallback: 'en', intl: 'hi', aliases: ['hi-in', 'hindi'], fontFamily: FONT_HI },
  { code: 'ja', englishName: 'Japanese', nativeName: '日本語', dir: 'ltr', fallback: 'en', intl: 'ja', aliases: ['ja-jp', 'kanji', 'japanese'], fontFamily: FONT_JA },
  { code: 'en-XA', englishName: 'Pseudo (expansion)', nativeName: '[Ƥşḗŭḓǿ]', dir: 'ltr', fallback: 'en', intl: 'en', pseudo: 'expand', aliases: [] },
  { code: 'ar-XB', englishName: 'Pseudo (RTL)', nativeName: '[RTL Pseudo]', dir: 'rtl', fallback: 'en', intl: 'en', pseudo: 'rtl', aliases: [] }
];

export const SUPPORTED_CODES = LOCALES.map((l) => l.code);
export const TRANSLATED_CODES = LOCALES.filter((l) => !l.pseudo && l.code !== DEFAULT_LOCALE).map((l) => l.code);

/** Map any BCP 47 tag or alias to a supported locale code, or the default. */
export function canonicalizeLocale(tag) {
  if (!tag) return DEFAULT_LOCALE;
  const lower = String(tag).trim().replace(/_/g, '-').toLowerCase();
  const exact = LOCALES.find((l) => l.code.toLowerCase() === lower);
  if (exact) return exact.code;
  const alias = LOCALES.find((l) => l.aliases.includes(lower));
  if (alias) return alias.code;
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
