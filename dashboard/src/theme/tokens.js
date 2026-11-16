// Turns a palette's base tokens into the full set of CSS custom properties used by the
// stylesheets. Derived colors are computed here (not with CSS color-mix) so the contrast
// test can verify the exact values the browser will paint.
import { PALETTES, getPalette } from './palettes.js';
import { bestContrast, ensureContrast, mix, rgba } from './color.js';

export const MODES = ['light', 'dark'];

// WCAG targets: 4.5 for text, 3 for focus rings, chart marks, and other graphics.
export const AA_TEXT = 4.5;
export const AA_GRAPHIC = 3;

/** Every derived color token for one palette and mode, keyed by CSS variable name. */
export function buildTokens(paletteId, mode) {
  const palette = getPalette(paletteId);
  const b = palette[mode === 'dark' ? 'dark' : 'light'];
  const dark = mode === 'dark';
  const white = '#ffffff';
  const black = '#000000';

  const surfaceSunken = mix(b.bg, b.surface2, 0.35);
  const textSurfaces = [b.bg, b.surface, b.surface2];
  const text = ensureContrast(b.text, [...textSurfaces, surfaceSunken], AA_TEXT);
  const textMuted = ensureContrast(b.textMuted, textSurfaces, AA_TEXT);
  const textFaint = ensureContrast(mix(b.textMuted, b.surface, 0.12), textSurfaces, AA_TEXT);
  const borderStrong = mix(b.border, b.text, dark ? 0.16 : 0.18);

  const primaryHover = mix(b.primary, dark ? white : black, dark ? 0.12 : 0.14);
  const primarySoft = mix(b.surface, b.primary, dark ? 0.2 : 0.12);
  const primaryText = ensureContrast(b.primary, [...textSurfaces, primarySoft], AA_TEXT);
  const accentSoft = mix(b.surface, b.accent, dark ? 0.2 : 0.12);
  const accentText = ensureContrast(b.accent, [b.surface, b.surface2, accentSoft], AA_TEXT);

  const status = {};
  for (const name of ['success', 'warning', 'danger', 'info']) {
    const soft = mix(b.surface, b[name], dark ? 0.18 : 0.12);
    status[name] = ensureContrast(b[name], [...textSurfaces, soft], AA_TEXT);
    status[name + 'Soft'] = soft;
  }
  // Solid danger buttons: keep the hue, pick the more legible label color, then adjust the
  // fill if neither label reaches AA.
  let dangerSolid = b.danger;
  let dangerContrast = bestContrast(dangerSolid, [white, dark ? b.bg : '#111111']);
  dangerSolid = ensureContrast(dangerSolid, dangerContrast, AA_TEXT);
  dangerContrast = bestContrast(dangerSolid, [white, dark ? b.bg : '#111111']);

  const codeBg = b.surface2;
  const chartBg = [b.surface, b.surface2];
  const chart = palette.chart.map((c) => ensureContrast(c, chartBg, AA_GRAPHIC));

  const tokens = {
    '--bg': b.bg,
    '--surface': b.surface,
    '--surface-2': b.surface2,
    '--surface-alt': b.surface2,
    '--surface-sunken': surfaceSunken,
    '--border': b.border,
    '--border-strong': borderStrong,
    '--text': text,
    '--text-muted': textMuted,
    '--text-faint': textFaint,
    '--primary': b.primary,
    '--primary-hover': primaryHover,
    '--primary-soft': primarySoft,
    '--primary-text': primaryText,
    '--primary-contrast': b.primaryContrast,
    '--accent': b.accent,
    '--accent-soft': accentSoft,
    '--accent-text': accentText,
    '--success': status.success,
    '--success-soft': status.successSoft,
    '--warning': status.warning,
    '--warning-soft': status.warningSoft,
    '--danger': status.danger,
    '--danger-soft': status.dangerSoft,
    '--danger-solid': dangerSolid,
    '--danger-contrast': dangerContrast,
    '--info': status.info,
    '--info-soft': status.infoSoft,
    '--neutral-soft': mix(b.surface, b.text, dark ? 0.1 : 0.07),
    '--focus': b.focus,
    '--focus-ring': rgba(b.focus, dark ? 0.45 : 0.3),
    '--shadow-color': b.shadow,
    '--shadow-sm': '0 1px 2px ' + rgba(b.shadow, dark ? 0.4 : 0.07),
    '--shadow-md': '0 6px 18px ' + rgba(b.shadow, dark ? 0.45 : 0.1),
    '--shadow-lg': '0 18px 50px ' + rgba(b.shadow, dark ? 0.6 : 0.22),
    '--scrim': rgba(dark ? black : b.shadow, dark ? 0.6 : 0.45),
    '--overlay-text': '#ffffff',
    '--overlay-bg': rgba(black, 0.62),
    '--code-bg': codeBg,
    '--json-key': ensureContrast(primaryText, codeBg, AA_TEXT),
    '--json-string': ensureContrast(palette.chart[2], codeBg, AA_TEXT),
    '--json-number': ensureContrast(status.info, codeBg, AA_TEXT),
    '--json-bool': ensureContrast(palette.chart[3], codeBg, AA_TEXT),
    '--json-null': textMuted,
    '--chart-grid': mix(b.border, b.surface, dark ? 0.2 : 0.3),
    '--chart-axis': textMuted,
    '--chart-success': ensureContrast(b.success, chartBg, AA_GRAPHIC),
    '--chart-failure': ensureContrast(b.danger, chartBg, AA_GRAPHIC),
    '--chart-stage': chart[0],
    '--chart-call': chart[1],
    '--chart-queue': mix(chart[1], b.surface, 0.62),
    '--chart-wait': mix(chart[1], b.surface, 0.38)
  };
  chart.forEach((c, i) => {
    tokens['--chart-' + (i + 1)] = c;
  });
  return tokens;
}

/** CSS for every palette and mode, keyed by the data-palette and data-theme attributes on html. */
export function buildPaletteCss() {
  const blocks = [];
  for (const p of PALETTES) {
    for (const mode of MODES) {
      const tokens = buildTokens(p.id, mode);
      const body = Object.entries(tokens)
        .map(([k, v]) => '  ' + k + ': ' + v + ';')
        .join('\n');
      blocks.push(':root[data-palette="' + p.id + '"][data-theme="' + mode + '"] {\n' + body + '\n  color-scheme: ' + mode + ';\n}');
    }
  }
  return blocks.join('\n');
}

/** Swatch colors for the picker: background, surface, primary, accent, and two chart hues. */
export function paletteSwatch(paletteId, mode) {
  const t = buildTokens(paletteId, mode);
  return { bg: t['--bg'], surface: t['--surface'], text: t['--text'], primary: t['--primary'], accent: t['--accent'], chart: [t['--chart-1'], t['--chart-2'], t['--chart-3'], t['--chart-4']] };
}
