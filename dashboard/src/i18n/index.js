// i18n bootstrap. English is bundled; every other catalog is a lazily loaded chunk. main.jsx
// awaits `i18nReady` before rendering, so the active locale is loaded before the first
// meaningful paint and there is no fallback-language flash.
//
// Resolution order (I18N.md): explicit ?lang= selection, the persisted choice
// (localStorage keyframe.locale), the browser preference, then English. The choice survives
// reloads, sign-out, and deep links.
import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next, useTranslation } from 'react-i18next';
import en from './locales/en.json';
import { pseudoCatalog } from './pseudo';
import { deepMerge } from './merge';
import { DEFAULT_LOCALE, LOCALE_STORAGE_KEY, SUPPORTED_CODES, canonicalizeLocale, getLocaleMeta } from './localeRegistry';

const loaders = import.meta.glob(['./locales/*.json', '!./locales/en.json']);

function applyDocumentLocale(code) {
  if (typeof document === 'undefined') return;
  const meta = getLocaleMeta(canonicalizeLocale(code));
  const root = document.documentElement;
  root.lang = meta.code;
  root.dir = meta.dir;
  if (meta.fontFamily) root.style.setProperty('--locale-font', meta.fontFamily);
  else root.style.removeProperty('--locale-font');
}

/** Load a locale's catalog into i18next. No-op when it is already loaded. */
export async function ensureLocale(code) {
  const locale = canonicalizeLocale(code);
  if (i18n.hasResourceBundle(locale, 'translation')) return locale;
  const meta = getLocaleMeta(locale);
  let catalog = en;
  if (meta.pseudo) catalog = pseudoCatalog(locale, en);
  else if (locale !== DEFAULT_LOCALE) {
    const loader = loaders['./locales/' + locale + '.json'];
    if (loader) {
      const mod = await loader();
      catalog = deepMerge(en, mod.default || mod);
    }
  }
  i18n.addResourceBundle(locale, 'translation', catalog, true, true);
  return locale;
}

if (!i18n.isInitialized) {
  i18n
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources: { en: { translation: en } },
      partialBundledLanguages: true,
      fallbackLng: DEFAULT_LOCALE,
      supportedLngs: SUPPORTED_CODES,
      nonExplicitSupportedLngs: false,
      load: 'currentOnly',
      interpolation: { escapeValue: false },
      returnNull: false,
      returnEmptyString: false,
      initImmediate: false,
      detection: {
        order: ['querystring', 'localStorage', 'navigator'],
        lookupQuerystring: 'lang',
        lookupLocalStorage: LOCALE_STORAGE_KEY,
        caches: ['localStorage'],
        convertDetectedLanguage: (lng) => canonicalizeLocale(lng)
      },
      react: { useSuspense: false },
      saveMissing: false
    });
  applyDocumentLocale(i18n.language);
  i18n.on('languageChanged', (lng) => applyDocumentLocale(lng));
}

/** Resolves once the detected locale's catalog is loaded and active. */
export const i18nReady = ensureLocale(i18n.language || DEFAULT_LOCALE)
  .then((locale) => i18n.changeLanguage(locale))
  .catch(() => i18n.changeLanguage(DEFAULT_LOCALE));

/** Change the active locale: load its catalog, persist it, and update document lang and dir. */
export async function setActiveLocale(code) {
  const locale = await ensureLocale(code);
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, locale);
  } catch {
    // Storage may be unavailable (private mode); the change still applies for this session.
  }
  await i18n.changeLanguage(locale);
  return locale;
}

/** Hook returning the active canonical locale code, t(), and a setter. */
export function useLocale() {
  const { t, i18n: instance } = useTranslation();
  return { t, locale: canonicalizeLocale(instance.language), setLocale: setActiveLocale };
}

export default i18n;
