// i18n bootstrap. Initialized synchronously (resources are bundled) before the first render,
// so there is no fallback-language flash. The selected locale persists under one key and
// survives reloads, logout, and deep links.
import i18n from 'i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import { initReactI18next, useTranslation } from 'react-i18next';
import resources from './resources';
import { DEFAULT_LOCALE, LOCALES, LOCALE_STORAGE_KEY, canonicalizeLocale, getLocaleMeta } from './localeRegistry';

function applyDocumentLocale(code) {
  if (typeof document === 'undefined') return;
  const meta = getLocaleMeta(code);
  document.documentElement.lang = meta.code;
  document.documentElement.dir = meta.dir;
}

if (!i18n.isInitialized) {
  i18n
    .use(LanguageDetector)
    .use(initReactI18next)
    .init({
      resources,
      fallbackLng: DEFAULT_LOCALE,
      supportedLngs: LOCALES.map((l) => l.code),
      nonExplicitSupportedLngs: false,
      load: 'currentOnly',
      interpolation: { escapeValue: false },
      returnNull: false,
      initImmediate: false,
      detection: {
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: LOCALE_STORAGE_KEY,
        caches: ['localStorage'],
        convertDetectedLanguage: (lng) => canonicalizeLocale(lng)
      },
      react: { useSuspense: false },
      saveMissing: false
    });
  applyDocumentLocale(canonicalizeLocale(i18n.language));
  i18n.on('languageChanged', (lng) => applyDocumentLocale(canonicalizeLocale(lng)));
}

/** Change the active locale; persists and updates document lang and dir. */
export function setActiveLocale(code) {
  const canonical = canonicalizeLocale(code);
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, canonical);
  } catch {
    // Storage may be unavailable (private mode); the change still applies for this session.
  }
  return i18n.changeLanguage(canonical);
}

/** Hook returning the active canonical locale code plus t(). */
export function useLocale() {
  const { t, i18n: instance } = useTranslation();
  return { t, locale: canonicalizeLocale(instance.language), setLocale: setActiveLocale };
}

export default i18n;
