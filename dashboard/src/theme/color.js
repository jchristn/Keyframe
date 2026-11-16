// Small sRGB color helpers used to derive theme tokens and to verify WCAG contrast.
// mix() interpolates gamma-encoded channels, which matches CSS color-mix(in srgb, ...).

function clamp01(n) {
  return Math.min(1, Math.max(0, n));
}

/** Parse #rgb or #rrggbb into { r, g, b } with 0-255 channels. */
export function parseHex(hex) {
  let h = String(hex).trim().replace(/^#/, '');
  if (h.length === 3) h = h.split('').map((c) => c + c).join('');
  if (!/^[0-9a-fA-F]{6}$/.test(h)) throw new Error('Invalid hex color: ' + hex);
  return { r: parseInt(h.slice(0, 2), 16), g: parseInt(h.slice(2, 4), 16), b: parseInt(h.slice(4, 6), 16) };
}

export function toHex({ r, g, b }) {
  const p = (n) => Math.round(Math.min(255, Math.max(0, n))).toString(16).padStart(2, '0');
  return '#' + p(r) + p(g) + p(b);
}

/** Mix a toward b by amount t (0 keeps a, 1 gives b). */
export function mix(a, b, t) {
  const x = parseHex(a);
  const y = parseHex(b);
  const k = clamp01(t);
  return toHex({ r: x.r + (y.r - x.r) * k, g: x.g + (y.g - x.g) * k, b: x.b + (y.b - x.b) * k });
}

/** rgba() string for a hex color and alpha. */
export function rgba(hex, alpha) {
  const { r, g, b } = parseHex(hex);
  return 'rgba(' + r + ', ' + g + ', ' + b + ', ' + alpha + ')';
}

function channel(c) {
  const s = c / 255;
  return s <= 0.03928 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
}

/** WCAG relative luminance. */
export function luminance(hex) {
  const { r, g, b } = parseHex(hex);
  return 0.2126 * channel(r) + 0.7152 * channel(g) + 0.0722 * channel(b);
}

/** WCAG contrast ratio between two colors (1 to 21). */
export function contrast(a, b) {
  const la = luminance(a);
  const lb = luminance(b);
  const hi = Math.max(la, lb);
  const lo = Math.min(la, lb);
  return (hi + 0.05) / (lo + 0.05);
}

/**
 * Nudge fg toward black or white (whichever direction can reach the target against every
 * background) until it meets the ratio against all of them. Returns fg unchanged when it
 * already passes, so well-chosen palette colors are never altered.
 */
export function ensureContrast(fg, backgrounds, ratio) {
  const bgs = Array.isArray(backgrounds) ? backgrounds : [backgrounds];
  const passes = (c) => bgs.every((bg) => contrast(c, bg) >= ratio);
  if (passes(fg)) return fg;
  const avgLum = bgs.reduce((s, bg) => s + luminance(bg), 0) / bgs.length;
  const targets = avgLum > 0.4 ? ['#000000', '#ffffff'] : ['#ffffff', '#000000'];
  for (const target of targets) {
    for (let i = 1; i <= 50; i += 1) {
      const c = mix(fg, target, i / 50);
      if (passes(c)) return c;
    }
  }
  return targets[0];
}

/** Whichever of the candidates contrasts best with bg. */
export function bestContrast(bg, candidates) {
  return candidates.reduce((best, c) => (contrast(c, bg) > contrast(best, bg) ? c : best), candidates[0]);
}
