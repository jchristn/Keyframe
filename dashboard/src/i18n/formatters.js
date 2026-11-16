// Locale-aware formatting helpers. Every helper takes an explicit locale; components get it
// from useLocale() so nothing relies on the browser default.
import { intlLocale } from './localeRegistry';

function toDate(value) {
  if (value === null || value === undefined || value === '') return null;
  const d = value instanceof Date ? value : new Date(value);
  return Number.isNaN(d.getTime()) ? null : d;
}

export function formatNumber(value, locale, options = {}) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '';
  return new Intl.NumberFormat(intlLocale(locale), options).format(Number(value));
}

export function formatPercent(value, locale, digits = 1) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '';
  return new Intl.NumberFormat(intlLocale(locale), {
    style: 'percent',
    minimumFractionDigits: 0,
    maximumFractionDigits: digits
  }).format(Number(value));
}

export function formatDate(value, locale, options = { dateStyle: 'medium' }) {
  const d = toDate(value);
  return d ? new Intl.DateTimeFormat(intlLocale(locale), options).format(d) : '';
}

export function formatTime(value, locale, options = { timeStyle: 'medium' }) {
  const d = toDate(value);
  return d ? new Intl.DateTimeFormat(intlLocale(locale), options).format(d) : '';
}

export function formatDateTime(value, locale, options = { dateStyle: 'medium', timeStyle: 'medium' }) {
  const d = toDate(value);
  return d ? new Intl.DateTimeFormat(intlLocale(locale), options).format(d) : '';
}

/** Short axis label for charts, chosen by bucket width. */
export function formatAxisTime(value, locale, bucketMinutes) {
  const d = toDate(value);
  if (!d) return '';
  const options = bucketMinutes >= 120
    ? { month: 'short', day: 'numeric', hour: bucketMinutes >= 360 ? undefined : 'numeric' }
    : { hour: 'numeric', minute: '2-digit' };
  return new Intl.DateTimeFormat(intlLocale(locale), options).format(d);
}

export function formatRelativeTime(value, locale, now = Date.now()) {
  const d = toDate(value);
  if (!d) return '';
  const seconds = Math.round((d.getTime() - now) / 1000);
  const abs = Math.abs(seconds);
  const rtf = new Intl.RelativeTimeFormat(intlLocale(locale), { numeric: 'auto' });
  if (abs < 60) return rtf.format(seconds, 'second');
  if (abs < 3600) return rtf.format(Math.round(seconds / 60), 'minute');
  if (abs < 86400) return rtf.format(Math.round(seconds / 3600), 'hour');
  return rtf.format(Math.round(seconds / 86400), 'day');
}

/** Duration from milliseconds using Intl unit formatting (ms, s, min, h). */
export function formatDuration(ms, locale) {
  if (ms === null || ms === undefined || Number.isNaN(Number(ms))) return '';
  const value = Number(ms);
  const loc = intlLocale(locale);
  const unit = (n, u, digits) =>
    new Intl.NumberFormat(loc, { style: 'unit', unit: u, unitDisplay: 'short', maximumFractionDigits: digits }).format(n);
  if (Math.abs(value) < 1000) return unit(value, 'millisecond', value < 10 ? 2 : 1);
  if (Math.abs(value) < 60000) return unit(value / 1000, 'second', 2);
  if (Math.abs(value) < 3600000) return unit(value / 60000, 'minute', 1);
  return unit(value / 3600000, 'hour', 1);
}

/** Media timestamp such as 1:02:03.4 or 2:05.0 from milliseconds. Digits are locale-formatted. */
export function formatTimestamp(ms, locale) {
  if (ms === null || ms === undefined) return '';
  const total = Math.max(0, Number(ms)) / 1000;
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const nf2 = new Intl.NumberFormat(intlLocale(locale), { minimumIntegerDigits: 2, maximumFractionDigits: 0 });
  const sec = new Intl.NumberFormat(intlLocale(locale), { minimumIntegerDigits: 2, minimumFractionDigits: 1, maximumFractionDigits: 1 }).format(
    Math.floor(s * 10) / 10
  );
  return h > 0 ? `${h}:${nf2.format(m)}:${sec}` : `${m}:${sec}`;
}

export function formatBytes(bytes, locale) {
  if (bytes === null || bytes === undefined || Number.isNaN(Number(bytes))) return '';
  const units = ['byte', 'kilobyte', 'megabyte', 'gigabyte', 'terabyte'];
  let value = Number(bytes);
  let i = 0;
  while (Math.abs(value) >= 1024 && i < units.length - 1) {
    value /= 1024;
    i += 1;
  }
  return new Intl.NumberFormat(intlLocale(locale), {
    style: 'unit',
    unit: units[i],
    // Plain bytes read best narrow ("512B"); larger units keep the short form ("2 kB").
    unitDisplay: i === 0 ? 'narrow' : 'short',
    maximumFractionDigits: i === 0 ? 0 : 1
  }).format(value);
}

export function formatList(items, locale, type = 'conjunction') {
  const list = (items || []).map(String);
  if (typeof Intl.ListFormat !== 'function') return list.join(', ');
  return new Intl.ListFormat(intlLocale(locale), { style: 'long', type }).format(list);
}

/** Compact number such as 1.2K or 3.4M, for chart axes and dense KPI tiles. */
export function formatCompact(value, locale, digits = 1) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '';
  return new Intl.NumberFormat(intlLocale(locale), { notation: 'compact', maximumFractionDigits: digits }).format(Number(value));
}

/** Plain decimal with a fixed maximum of fraction digits. */
export function formatDecimal(value, locale, digits = 2) {
  if (value === null || value === undefined || Number.isNaN(Number(value))) return '';
  return new Intl.NumberFormat(intlLocale(locale), { maximumFractionDigits: digits }).format(Number(value));
}
