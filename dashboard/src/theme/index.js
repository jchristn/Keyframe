// Appearance runtime: palette and mode (light, dark, or system) persisted per browser.
// main.jsx calls installPaletteStyles() and applyAppearance() before the first render, and
// the inline script in index.html sets the html attributes and background even earlier, so
// a reload never flashes the wrong colors.
import { DEFAULT_PALETTE, PALETTE_IDS } from './palettes.js';
import { buildPaletteCss, buildTokens } from './tokens.js';

export const APPEARANCE_STORAGE_KEY = 'keyframe.appearance';
export const LEGACY_THEME_KEY = 'keyframe.theme';
export const MODE_OPTIONS = ['light', 'dark', 'system'];
const STYLE_ID = 'keyframe-palettes';

function systemPrefersDark() {
  return typeof window !== 'undefined' && !!window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
}

/** Normalize anything read from storage into { palette, mode }. */
export function normalizeAppearance(value) {
  const v = value && typeof value === 'object' ? value : {};
  return {
    palette: PALETTE_IDS.includes(v.palette) ? v.palette : DEFAULT_PALETTE,
    mode: MODE_OPTIONS.includes(v.mode) ? v.mode : 'system'
  };
}

export function readAppearance() {
  try {
    const raw = localStorage.getItem(APPEARANCE_STORAGE_KEY);
    if (raw) return normalizeAppearance(JSON.parse(raw));
    // Earlier builds stored only 'light' or 'dark'.
    const legacy = localStorage.getItem(LEGACY_THEME_KEY);
    if (legacy === 'light' || legacy === 'dark') return normalizeAppearance({ mode: legacy });
  } catch {
    // Storage unavailable or corrupt: fall back to defaults.
  }
  return normalizeAppearance(null);
}

export function resolveMode(mode) {
  if (mode === 'light' || mode === 'dark') return mode;
  return systemPrefersDark() ? 'dark' : 'light';
}

/** Inject the stylesheet holding every palette's tokens (idempotent). */
export function installPaletteStyles() {
  if (typeof document === 'undefined' || document.getElementById(STYLE_ID)) return;
  const style = document.createElement('style');
  style.id = STYLE_ID;
  style.textContent = buildPaletteCss();
  document.head.appendChild(style);
}

/** Apply an appearance to <html>, persist it, and cache the background for the pre-paint script. */
export function applyAppearance(appearance) {
  const a = normalizeAppearance(appearance);
  const resolved = resolveMode(a.mode);
  if (typeof document !== 'undefined') {
    const root = document.documentElement;
    root.setAttribute('data-palette', a.palette);
    root.setAttribute('data-theme', resolved);
    root.style.colorScheme = resolved;
    root.style.backgroundColor = buildTokens(a.palette, resolved)['--bg'];
  }
  try {
    localStorage.setItem(APPEARANCE_STORAGE_KEY, JSON.stringify({ ...a, bg: { light: buildTokens(a.palette, 'light')['--bg'], dark: buildTokens(a.palette, 'dark')['--bg'] } }));
  } catch {
    // Ignore; the choice still applies for this session.
  }
  return resolved;
}
