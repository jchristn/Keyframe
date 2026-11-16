import { describe, expect, it } from 'vitest';
import { PALETTES, PALETTE_IDS } from './palettes';
import { AA_GRAPHIC, AA_TEXT, MODES, buildPaletteCss, buildTokens } from './tokens';
import { contrast, ensureContrast, mix } from './color';
import { applyAppearance, normalizeAppearance, readAppearance, APPEARANCE_STORAGE_KEY } from './index';

// Pairs checked in every palette and mode: [foreground token, background tokens, minimum ratio].
const TEXT_PAIRS = [
  ['--text', ['--bg', '--surface', '--surface-2', '--surface-sunken', '--code-bg']],
  ['--text-muted', ['--bg', '--surface', '--surface-2']],
  ['--text-faint', ['--bg', '--surface', '--surface-2']],
  ['--primary-contrast', ['--primary', '--primary-hover']],
  ['--primary-text', ['--bg', '--surface', '--surface-2', '--primary-soft']],
  ['--accent-text', ['--surface', '--accent-soft']],
  ['--success', ['--surface', '--success-soft']],
  ['--warning', ['--surface', '--warning-soft']],
  ['--danger', ['--surface', '--danger-soft']],
  ['--info', ['--surface', '--info-soft']],
  ['--danger-contrast', ['--danger-solid']],
  ['--text', ['--primary-soft', '--neutral-soft']],
  ['--json-key', ['--code-bg']],
  ['--json-string', ['--code-bg']],
  ['--json-number', ['--code-bg']],
  ['--json-bool', ['--code-bg']],
  ['--json-null', ['--code-bg']]
];
const GRAPHIC_PAIRS = [
  ['--focus', ['--bg', '--surface']],
  ['--chart-success', ['--surface']],
  ['--chart-failure', ['--surface']],
  ...[1, 2, 3, 4, 5, 6, 7, 8].map((i) => ['--chart-' + i, ['--surface', '--surface-2']])
];

describe('palettes', () => {
  it('ships at least a dozen palettes, each with a light and a dark variant and eight chart hues', () => {
    expect(PALETTES.length).toBeGreaterThanOrEqual(14);
    expect(new Set(PALETTE_IDS).size).toBe(PALETTE_IDS.length);
    for (const p of PALETTES) {
      expect(p.light, p.id).toBeTruthy();
      expect(p.dark, p.id).toBeTruthy();
      expect(p.chart, p.id).toHaveLength(8);
      for (const key of ['bg', 'surface', 'surface2', 'border', 'text', 'textMuted', 'primary', 'primaryContrast', 'accent', 'success', 'warning', 'danger', 'info', 'focus', 'shadow']) {
        expect(p.light[key], p.id + ' light ' + key).toMatch(/^#[0-9a-f]{6}$/i);
        expect(p.dark[key], p.id + ' dark ' + key).toMatch(/^#[0-9a-f]{6}$/i);
      }
    }
  });

  it('meets WCAG AA for text (4.5:1) in every palette and mode', () => {
    const failures = [];
    for (const p of PALETTES) {
      for (const mode of MODES) {
        const tk = buildTokens(p.id, mode);
        for (const [fg, bgs] of TEXT_PAIRS) {
          for (const bg of bgs) {
            const ratio = contrast(tk[fg], tk[bg]);
            if (ratio < AA_TEXT) failures.push(p.id + ' ' + mode + ' ' + fg + ' on ' + bg + ' = ' + ratio.toFixed(2));
          }
        }
      }
    }
    expect(failures).toEqual([]);
  });

  it('meets 3:1 for focus rings and chart marks in every palette and mode', () => {
    const failures = [];
    for (const p of PALETTES) {
      for (const mode of MODES) {
        const tk = buildTokens(p.id, mode);
        for (const [fg, bgs] of GRAPHIC_PAIRS) {
          for (const bg of bgs) {
            const ratio = contrast(tk[fg], tk[bg]);
            if (ratio < AA_GRAPHIC) failures.push(p.id + ' ' + mode + ' ' + fg + ' on ' + bg + ' = ' + ratio.toFixed(2));
          }
        }
      }
    }
    expect(failures).toEqual([]);
  });

  it('keeps the designed base colors for text and primary buttons (no silent repair)', () => {
    for (const p of PALETTES) {
      for (const mode of MODES) {
        const base = p[mode];
        expect(contrast(base.text, base.surface), p.id + ' ' + mode + ' text').toBeGreaterThanOrEqual(AA_TEXT);
        expect(contrast(base.textMuted, base.surface), p.id + ' ' + mode + ' muted').toBeGreaterThanOrEqual(AA_TEXT);
        expect(contrast(base.primaryContrast, base.primary), p.id + ' ' + mode + ' primary button').toBeGreaterThanOrEqual(AA_TEXT);
      }
    }
  });

  it('emits one CSS block per palette and mode with every token', () => {
    const css = buildPaletteCss();
    expect(css.match(/:root\[data-palette=/g)).toHaveLength(PALETTES.length * 2);
    expect(css).toContain(':root[data-palette="slate"][data-theme="dark"]');
    expect(css).toContain('--chart-8:');
    expect(css).not.toMatch(/undefined|NaN/);
  });
});

describe('color helpers', () => {
  it('computes WCAG contrast and mixes in sRGB', () => {
    expect(contrast('#000000', '#ffffff')).toBeCloseTo(21, 5);
    expect(contrast('#777777', '#777777')).toBeCloseTo(1, 5);
    expect(mix('#000000', '#ffffff', 0.5)).toBe('#808080');
  });

  it('only adjusts a color that fails the target', () => {
    expect(ensureContrast('#111111', '#ffffff', 4.5)).toBe('#111111');
    const fixed = ensureContrast('#bbbbbb', '#ffffff', 4.5);
    expect(contrast(fixed, '#ffffff')).toBeGreaterThanOrEqual(4.5);
  });
});

describe('appearance persistence', () => {
  it('normalizes unknown values to the defaults', () => {
    expect(normalizeAppearance({ palette: 'nope', mode: 'sepia' })).toEqual({ palette: 'slate', mode: 'system' });
  });

  it('applies the palette and resolved mode to <html> and remembers them', () => {
    applyAppearance({ palette: 'forest', mode: 'dark' });
    expect(document.documentElement.getAttribute('data-palette')).toBe('forest');
    expect(document.documentElement.getAttribute('data-theme')).toBe('dark');
    expect(readAppearance()).toEqual({ palette: 'forest', mode: 'dark' });
    const stored = JSON.parse(localStorage.getItem(APPEARANCE_STORAGE_KEY));
    expect(stored.bg.dark).toBe(buildTokens('forest', 'dark')['--bg']);
  });

  it('migrates the old light or dark preference', () => {
    localStorage.setItem('keyframe.theme', 'dark');
    expect(readAppearance()).toEqual({ palette: 'slate', mode: 'dark' });
  });
});
