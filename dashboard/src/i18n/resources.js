// Every translation catalog, loaded eagerly. Used by tests and tooling only; the running app
// loads catalogs lazily through i18n/index.js so only the active locale is downloaded.
// English is the source of truth; pseudo-locales are generated from it.
import en from './locales/en.json';
import { pseudoCatalog } from './pseudo';
import { deepMerge } from './merge';

const modules = import.meta.glob('./locales/*.json', { eager: true });

/** Translated catalogs by locale code, before merging over English. */
export const TRANSLATIONS = {};
for (const [file, mod] of Object.entries(modules)) {
  const code = file.replace('./locales/', '').replace('.json', '');
  if (code !== 'en') TRANSLATIONS[code] = mod.default || mod;
}

const resources = { en: { translation: en } };
for (const [code, catalog] of Object.entries(TRANSLATIONS)) resources[code] = { translation: deepMerge(en, catalog) };
resources['en-XA'] = { translation: pseudoCatalog('en-XA', en) };
resources['ar-XB'] = { translation: pseudoCatalog('ar-XB', en) };

export { en };
export default resources;
