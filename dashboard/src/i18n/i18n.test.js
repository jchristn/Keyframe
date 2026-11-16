import { describe, expect, it } from 'vitest';
import { existsSync } from 'node:fs';
import { join } from 'node:path';
import i18n, { ensureLocale, setActiveLocale } from './index';
import { DEFAULT_LOCALE, LOCALES, LOCALE_STORAGE_KEY, TRANSLATED_CODES, canonicalizeLocale, getLocaleMeta } from './localeRegistry';
import { TRANSLATIONS, en } from './resources';
import {
  formatBytes,
  formatCompact,
  formatDate,
  formatDateTime,
  formatDuration,
  formatList,
  formatNumber,
  formatPercent,
  formatRelativeTime,
  formatTime,
  formatTimestamp
} from './formatters';
import { compareCatalog, findHardcoded, findMissing, findOrphans } from '../../scripts/check-i18n.mjs';

const RECOMMENDED = ['en', 'es', 'pt', 'fr', 'it', 'de', 'zh', 'ar', 'ru', 'ms', 'hi', 'ja'];

describe('locale registry', () => {
  it('lists the twelve recommended locales with autonyms, direction, and fallback, plus two pseudo-locales', () => {
    for (const code of RECOMMENDED) {
      const meta = getLocaleMeta(code);
      expect(meta.code, code).toBe(code);
      expect(meta.nativeName, code).toBeTruthy();
      expect(meta.englishName, code).toBeTruthy();
      expect(['ltr', 'rtl']).toContain(meta.dir);
      if (code !== DEFAULT_LOCALE) expect(meta.fallback).toBe('en');
    }
    expect(getLocaleMeta('ar').dir).toBe('rtl');
    expect(getLocaleMeta('ja').nativeName).toBe('日本語');
    expect(getLocaleMeta('hi').nativeName).toBe('हिन्दी');
    expect(LOCALES.filter((l) => l.pseudo).map((l) => l.code)).toEqual(['en-XA', 'ar-XB']);
  });

  it('canonicalizes locale tags and aliases', () => {
    expect(canonicalizeLocale('en-US')).toBe('en');
    expect(canonicalizeLocale('en_GB')).toBe('en');
    expect(canonicalizeLocale('pt-BR')).toBe('pt');
    expect(canonicalizeLocale('zh-Hans-CN')).toBe('zh');
    expect(canonicalizeLocale('zh-CN')).toBe('zh');
    expect(canonicalizeLocale('Kanji')).toBe('ja');
    expect(canonicalizeLocale('de-AT')).toBe('de');
    expect(canonicalizeLocale('ar-xb')).toBe('ar-XB');
    expect(canonicalizeLocale('zz')).toBe('en');
    expect(canonicalizeLocale(null)).toBe('en');
  });

  it('has a catalog file for every translated locale in the registry', () => {
    for (const code of TRANSLATED_CODES) expect(existsSync(join(__dirname, 'locales', code + '.json')), code).toBe(true);
    expect(Object.keys(TRANSLATIONS).sort()).toEqual([...TRANSLATED_CODES].sort());
  });
});

describe('catalogs', () => {
  it('has every referenced translation key in the English bundle', async () => {
    expect(await findMissing()).toEqual([]);
  });

  it('has no orphaned English keys', () => {
    expect(findOrphans()).toEqual([]);
  });

  it('has no hard-coded user-facing strings in JSX', () => {
    expect(findHardcoded()).toEqual([]);
  });

  it('has no em-dashes or en-dashes in English', () => {
    expect(JSON.stringify(en)).not.toMatch(/[\u2014\u2013]/);
  });

  for (const code of RECOMMENDED.filter((c) => c !== 'en')) {
    it('gives ' + code + ' every key of English with matching placeholders', () => {
      const result = compareCatalog(TRANSLATIONS[code] || {});
      expect(result.missing, code + ' missing').toEqual([]);
      expect(result.extra, code + ' extra').toEqual([]);
      expect(result.mismatched, code + ' placeholders').toEqual([]);
      expect(result.empty, code + ' empty').toEqual([]);
      expect(result.dashes, code + ' dashes').toEqual([]);
    });
  }

  it('actually translates the catalogs (most values differ from English)', () => {
    for (const code of RECOMMENDED.filter((c) => c !== 'en')) {
      const catalog = TRANSLATIONS[code] || {};
      const same = ['nav.home', 'common.save', 'common.cancel', 'analytics.title', 'appearance.title', 'jobs.title'].filter((k) => k.split('.').reduce((n, p) => n && n[p], catalog) === k.split('.').reduce((n, p) => n && n[p], en));
      expect(same.length, code + ': ' + same.join(', ')).toBeLessThanOrEqual(2);
    }
  });
});

describe('runtime', () => {
  it('lazily loads a locale, persists it, and updates document lang and dir', async () => {
    await setActiveLocale('ja');
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('ja');
    expect(document.documentElement.lang).toBe('ja');
    expect(document.documentElement.dir).toBe('ltr');
    expect(i18n.t('common.save')).toBe(TRANSLATIONS.ja.common.save);
    expect(document.documentElement.style.getPropertyValue('--locale-font')).toContain('Hiragino');

    await setActiveLocale('ar');
    expect(document.documentElement.dir).toBe('rtl');
    expect(i18n.t('nav.analytics')).toBe(TRANSLATIONS.ar.nav.analytics);

    await setActiveLocale('ar-XB');
    expect(document.documentElement.lang).toBe('ar-XB');
    expect(document.documentElement.dir).toBe('rtl');

    await setActiveLocale('en-XA');
    expect(i18n.t('common.save')).toMatch(/^\[.+~+\]$/);
    expect(document.documentElement.dir).toBe('ltr');

    await setActiveLocale('en');
    expect(i18n.t('common.save')).toBe('Save');
    expect(i18n.t('table.selectedCount', { count: 2 })).toBe('2 selected');
  });

  it('uses each language plural rules', async () => {
    await ensureLocale('ru');
    const ru = i18n.getFixedT('ru');
    expect(ru('analytics.byKind.count', { count: 1, value: '1' })).not.toBe(ru('analytics.byKind.count', { count: 5, value: '5' }));
    await ensureLocale('ar');
    const ar = i18n.getFixedT('ar');
    expect(ar('analytics.byKind.count', { count: 2, value: '2' })).toBeTruthy();
  });
});

describe('formatters', () => {
  const when = '2026-11-15T18:30:00Z';

  it('format numbers, percentages, and bytes per locale', () => {
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
    expect(formatNumber(1234.5, 'de')).toBe('1.234,5');
    expect(formatNumber(1234.5, 'hi')).toMatch(/1,234.5/);
    expect(formatNumber(1234.5, 'ru')).toMatch(/^1\s234,5$/);
    expect(formatPercent(0.256, 'en')).toBe('25.6%');
    expect(formatPercent(0.256, 'fr')).toMatch(/25,6\s?%/);
    expect(formatBytes(2048, 'en')).toBe('2 kB');
    expect(formatBytes(5 * 1024 * 1024, 'de')).toMatch(/5\sMB/);
    expect(formatCompact(12500, 'en')).toBe('12.5K');
    expect(formatCompact(12500, 'ja')).toBe('1.3万');
  });

  it('format durations, timestamps, and lists per locale', () => {
    expect(formatDuration(1500, 'en')).toBe('1.5 sec');
    expect(formatDuration(5.59, 'en')).toBe('5.59 ms');
    expect(formatDuration(1500, 'de')).toMatch(/1,5/);
    expect(formatTimestamp(62500, 'en')).toBe('1:02.5');
    expect(formatList(['a', 'b', 'c'], 'en')).toBe('a, b, and c');
    expect(formatList(['a', 'b', 'c'], 'es')).toBe('a, b y c');
    expect(formatList(['a', 'b'], 'ja')).toBe('a、b');
  });

  it('format dates, times, and relative time with an explicit locale', () => {
    expect(formatDate(when, 'en', { dateStyle: 'medium', timeZone: 'UTC' })).toBe('Nov 15, 2026');
    expect(formatDate(when, 'de', { dateStyle: 'medium', timeZone: 'UTC' })).toBe('15.11.2026');
    expect(formatDate(when, 'ja', { dateStyle: 'medium', timeZone: 'UTC' })).toBe('2026/11/15');
    expect(formatTime(when, 'en', { timeStyle: 'short', timeZone: 'UTC' })).toBe('6:30 PM');
    expect(formatDateTime(when, 'ar-XB', { dateStyle: 'short', timeZone: 'UTC' })).toBe('11/15/26');
    const now = new Date(when).getTime();
    expect(formatRelativeTime(now - 5 * 60000, 'en', now)).toBe('5 minutes ago');
    expect(formatRelativeTime(now - 5 * 60000, 'es', now)).toBe('hace 5 minutos');
    expect(formatRelativeTime(now - 86400000, 'fr', now)).toBe('hier');
    expect(formatDate(null, 'en')).toBe('');
  });
});
