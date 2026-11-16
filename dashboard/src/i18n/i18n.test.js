import { describe, expect, it } from 'vitest';
import i18n, { setActiveLocale } from './index';
import { canonicalizeLocale, LOCALE_STORAGE_KEY } from './localeRegistry';
import { formatBytes, formatDuration, formatList, formatNumber, formatTimestamp } from './formatters';
import { findMissing } from '../../scripts/check-i18n.mjs';

describe('i18n', () => {
  it('has every referenced translation key in the English bundle', async () => {
    expect(await findMissing()).toEqual([]);
  });

  it('canonicalizes locale tags and aliases', () => {
    expect(canonicalizeLocale('en-US')).toBe('en');
    expect(canonicalizeLocale('en_GB')).toBe('en');
    expect(canonicalizeLocale('ar-xb')).toBe('ar-XB');
    expect(canonicalizeLocale('zz')).toBe('en');
    expect(canonicalizeLocale(null)).toBe('en');
  });

  it('persists the locale and updates document lang and dir', async () => {
    await setActiveLocale('ar-XB');
    expect(localStorage.getItem(LOCALE_STORAGE_KEY)).toBe('ar-XB');
    expect(document.documentElement.lang).toBe('ar-XB');
    expect(document.documentElement.dir).toBe('rtl');
    await setActiveLocale('en-XA');
    expect(i18n.t('common.save')).toMatch(/^\[.+~+\]$/);
    expect(document.documentElement.dir).toBe('ltr');
    await setActiveLocale('en');
    expect(i18n.t('common.save')).toBe('Save');
    expect(i18n.t('table.selectedCount', { count: 2 })).toBe('2 selected');
  });

  it('formats with an explicit locale', () => {
    expect(formatNumber(1234.5, 'en')).toBe('1,234.5');
    expect(formatNumber(1234.5, 'de')).toBe('1,234.5');
    expect(formatBytes(2048, 'en')).toBe('2 kB');
    expect(formatDuration(1500, 'en')).toBe('1.5 sec');
    expect(formatDuration(5.59, 'en')).toBe('5.59 ms');
    expect(formatTimestamp(62500, 'en')).toBe('1:02.5');
    expect(formatList(['a', 'b', 'c'], 'en')).toBe('a, b, and c');
  });
});
