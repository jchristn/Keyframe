// Color palettes. Each palette has a light and a dark base token set plus eight chart
// series hues. Every other token (hover, soft fills, faint text, chart fills, JSON colors,
// shadows) is derived in tokens.js, and contrast.test.js checks WCAG AA for every
// palette and mode. Names are i18n keys under appearance.palettes.<id>.
//
// Base token meanings:
//   bg              page background behind panels
//   surface         panels, cards, inputs, tables
//   surface2        alternate rows, toolbars, code blocks
//   border          hairlines between and around surfaces
//   text, textMuted primary and secondary text
//   primary         primary buttons, active navigation, links
//   primaryContrast text on primary
//   accent          secondary highlight (badges, chart emphasis)
//   success, warning, danger, info   status hues
//   focus           keyboard focus ring
//   shadow          shadow and scrim tint

export const DEFAULT_PALETTE = 'slate';

export const PALETTES = [
  {
    id: 'slate',
    light: {
      bg: '#f1f5f9', surface: '#ffffff', surface2: '#f8fafc', border: '#e2e8f0', text: '#0f172a', textMuted: '#475569',
      primary: '#2563eb', primaryContrast: '#ffffff', accent: '#0e7490', success: '#15803d', warning: '#b45309', danger: '#dc2626',
      info: '#0369a1', focus: '#3b82f6', shadow: '#0f172a'
    },
    dark: {
      bg: '#0b1120', surface: '#111827', surface2: '#172033', border: '#1f2a3d', text: '#e2e8f0', textMuted: '#94a3b8',
      primary: '#60a5fa', primaryContrast: '#0b1120', accent: '#22d3ee', success: '#4ade80', warning: '#fbbf24', danger: '#f87171',
      info: '#38bdf8', focus: '#93c5fd', shadow: '#000000'
    },
    chart: ['#3b82f6', '#14b8a6', '#f59e0b', '#8b5cf6', '#ef4444', '#22c55e', '#ec4899', '#64748b']
  },
  {
    id: 'indigoNight',
    light: {
      bg: '#f3f4fb', surface: '#ffffff', surface2: '#f7f7fd', border: '#e0e2f2', text: '#1b1d3a', textMuted: '#535775',
      primary: '#4f46e5', primaryContrast: '#ffffff', accent: '#be185d', success: '#15803d', warning: '#b45309', danger: '#dc2626',
      info: '#2563eb', focus: '#6366f1', shadow: '#1e1b4b'
    },
    dark: {
      bg: '#0d0e1f', surface: '#14152b', surface2: '#1b1c38', border: '#2a2c4d', text: '#e4e5fb', textMuted: '#a0a3c8',
      primary: '#818cf8', primaryContrast: '#0d0e1f', accent: '#f472b6', success: '#4ade80', warning: '#fbbf24', danger: '#fb7185',
      info: '#60a5fa', focus: '#a5b4fc', shadow: '#000000'
    },
    chart: ['#6366f1', '#ec4899', '#14b8a6', '#f59e0b', '#a855f7', '#06b6d4', '#84cc16', '#f97316']
  },
  {
    id: 'fjord',
    light: {
      bg: '#eceff4', surface: '#ffffff', surface2: '#f4f6f9', border: '#d8dee9', text: '#2e3440', textMuted: '#4c566a',
      primary: '#4c6f9a', primaryContrast: '#ffffff', accent: '#2f7f94', success: '#4f7a3a', warning: '#9a5f17', danger: '#b4444f',
      info: '#46769c', focus: '#5e81ac', shadow: '#2e3440'
    },
    dark: {
      bg: '#242933', surface: '#2e3440', surface2: '#353c4a', border: '#434c5e', text: '#eceff4', textMuted: '#b3bccc',
      primary: '#88c0d0', primaryContrast: '#242933', accent: '#81a1c1', success: '#a3be8c', warning: '#ebcb8b', danger: '#e5949a',
      info: '#8fbcbb', focus: '#88c0d0', shadow: '#000000'
    },
    chart: ['#5e81ac', '#bf616a', '#a3be8c', '#ebcb8b', '#b48ead', '#88c0d0', '#d08770', '#8fbcbb']
  },
  {
    id: 'nocturne',
    light: {
      bg: '#f5f3fa', surface: '#ffffff', surface2: '#faf8fd', border: '#e4dff0', text: '#22212c', textMuted: '#5d5a73',
      primary: '#7c3aed', primaryContrast: '#ffffff', accent: '#b0226f', success: '#1f7a3f', warning: '#8a6400', danger: '#c92a2a',
      info: '#0b7189', focus: '#9d6bf5', shadow: '#22212c'
    },
    dark: {
      bg: '#1e1f29', surface: '#282a36', surface2: '#2f3242', border: '#3c3f52', text: '#f8f8f2', textMuted: '#b4b8d0',
      primary: '#bd93f9', primaryContrast: '#1e1f29', accent: '#ff79c6', success: '#50fa7b', warning: '#f1fa8c', danger: '#ff6e6e',
      info: '#8be9fd', focus: '#bd93f9', shadow: '#000000'
    },
    chart: ['#bd93f9', '#ff79c6', '#50fa7b', '#ffb86c', '#8be9fd', '#f1fa8c', '#ff5555', '#6272a4']
  },
  {
    id: 'solstice',
    light: {
      bg: '#eee8d5', surface: '#fdf6e3', surface2: '#f7f0dc', border: '#ddd4b8', text: '#073642', textMuted: '#4a5f66',
      primary: '#1a6aa0', primaryContrast: '#fdf6e3', accent: '#1b7a73', success: '#5a7300', warning: '#8f5600', danger: '#b8292a',
      info: '#1a6aa0', focus: '#268bd2', shadow: '#073642'
    },
    dark: {
      bg: '#00212b', surface: '#002b36', surface2: '#073642', border: '#0d4451', text: '#eee8d5', textMuted: '#93a1a1',
      primary: '#4ea6e6', primaryContrast: '#00212b', accent: '#2aa198', success: '#a5b93a', warning: '#d9a521', danger: '#f26b5b',
      info: '#4fb8d0', focus: '#3a9ae0', shadow: '#000000'
    },
    chart: ['#268bd2', '#2aa198', '#b58900', '#d33682', '#859900', '#6c71c4', '#cb4b16', '#dc322f']
  },
  {
    id: 'cocoa',
    light: {
      bg: '#eff1f5', surface: '#ffffff', surface2: '#f6f7fa', border: '#dce0e8', text: '#4c4f69', textMuted: '#5c5f77',
      primary: '#8839ef', primaryContrast: '#ffffff', accent: '#1e66f5', success: '#2f7d1a', warning: '#a65b00', danger: '#d20f39',
      info: '#1a5fd8', focus: '#5f6fe8', shadow: '#4c4f69'
    },
    dark: {
      bg: '#11111b', surface: '#1e1e2e', surface2: '#262637', border: '#313244', text: '#cdd6f4', textMuted: '#a6adc8',
      primary: '#cba6f7', primaryContrast: '#1e1e2e', accent: '#f5c2e7', success: '#a6e3a1', warning: '#f9e2af', danger: '#f38ba8',
      info: '#89b4fa', focus: '#b4befe', shadow: '#000000'
    },
    chart: ['#cba6f7', '#89b4fa', '#a6e3a1', '#fab387', '#f38ba8', '#94e2d5', '#f9e2af', '#f5c2e7']
  },
  {
    id: 'duskRose',
    light: {
      bg: '#faf4ed', surface: '#fffaf3', surface2: '#f8f0e7', border: '#ebdfd3', text: '#575279', textMuted: '#625e7c',
      primary: '#7a62a0', primaryContrast: '#fffaf3', accent: '#a8506a', success: '#3d7a4f', warning: '#9a6212', danger: '#a8455f',
      info: '#286983', focus: '#907aa9', shadow: '#575279'
    },
    dark: {
      bg: '#191724', surface: '#1f1d2e', surface2: '#26233a', border: '#393552', text: '#e0def4', textMuted: '#a19dbb',
      primary: '#c4a7e7', primaryContrast: '#191724', accent: '#ebbcba', success: '#a3d9a5', warning: '#f6c177', danger: '#eb6f92',
      info: '#9ccfd8', focus: '#c4a7e7', shadow: '#000000'
    },
    chart: ['#c4a7e7', '#ebbcba', '#9ccfd8', '#f6c177', '#eb6f92', '#31748f', '#a3be8c', '#908caa']
  },
  {
    id: 'metroNight',
    light: {
      bg: '#e6e7ed', surface: '#f8f9fc', surface2: '#eff0f5', border: '#d5d7e2', text: '#343b58', textMuted: '#4c5374',
      primary: '#2e5bd8', primaryContrast: '#ffffff', accent: '#7847bd', success: '#496629', warning: '#8a5a12', danger: '#b3244a',
      info: '#166775', focus: '#2e7de9', shadow: '#343b58'
    },
    dark: {
      bg: '#16161e', surface: '#1a1b26', surface2: '#222336', border: '#2f3148', text: '#c0caf5', textMuted: '#9aa5ce',
      primary: '#7aa2f7', primaryContrast: '#16161e', accent: '#bb9af7', success: '#9ece6a', warning: '#e0af68', danger: '#f7768e',
      info: '#7dcfff', focus: '#7aa2f7', shadow: '#000000'
    },
    chart: ['#7aa2f7', '#bb9af7', '#9ece6a', '#e0af68', '#f7768e', '#7dcfff', '#ff9e64', '#2ac3de']
  },
  {
    id: 'forest',
    light: {
      bg: '#f1f4ef', surface: '#ffffff', surface2: '#f7f9f5', border: '#dde4d7', text: '#1a2a1f', textMuted: '#4d5e52',
      primary: '#2e7d4a', primaryContrast: '#ffffff', accent: '#8a5a2b', success: '#2e7d32', warning: '#965f00', danger: '#c0392b',
      info: '#2f6f8f', focus: '#3f8a4e', shadow: '#1a2a1f'
    },
    dark: {
      bg: '#0e1410', surface: '#141c17', surface2: '#1a251e', border: '#26342b', text: '#e3ece4', textMuted: '#9fb3a5',
      primary: '#6fcf8a', primaryContrast: '#0e1410', accent: '#d9a55b', success: '#7ddc8e', warning: '#e6b450', danger: '#f07a6a',
      info: '#74b9d6', focus: '#8fe0a3', shadow: '#000000'
    },
    chart: ['#3f8f55', '#c28a3a', '#4f86a8', '#9b6fb0', '#c25b4a', '#7aa83b', '#d4b14a', '#5aa79a']
  },
  {
    id: 'ocean',
    light: {
      bg: '#eef5f9', surface: '#ffffff', surface2: '#f5f9fc', border: '#d6e4ee', text: '#0b2533', textMuted: '#47616f',
      primary: '#0b6fa4', primaryContrast: '#ffffff', accent: '#0f7f7f', success: '#187a4b', warning: '#a35a00', danger: '#c62f3a',
      info: '#1d5fa8', focus: '#2490d1', shadow: '#0b2533'
    },
    dark: {
      bg: '#06141d', surface: '#0b1d29', surface2: '#102633', border: '#1a3546', text: '#dcedf6', textMuted: '#8fb0c2',
      primary: '#4cc3f0', primaryContrast: '#06141d', accent: '#3fd1c4', success: '#5ed69a', warning: '#f2b950', danger: '#f77b7b',
      info: '#7ab8ff', focus: '#67cdf5', shadow: '#000000'
    },
    chart: ['#0ea5e9', '#14b8a6', '#6366f1', '#f59e0b', '#ec4899', '#22c55e', '#f97316', '#06b6d4']
  },
  {
    id: 'amber',
    light: {
      bg: '#faf5ee', surface: '#ffffff', surface2: '#fdf9f3', border: '#ecdfcd', text: '#2b1d0e', textMuted: '#6b5640',
      primary: '#c2410c', primaryContrast: '#ffffff', accent: '#be185d', success: '#3f7d20', warning: '#9a5800', danger: '#c0262d',
      info: '#1f63a8', focus: '#d0600e', shadow: '#2b1d0e'
    },
    dark: {
      bg: '#16100a', surface: '#1e1610', surface2: '#261c14', border: '#3a2c1f', text: '#f5e9dc', textMuted: '#c2ab93',
      primary: '#fb923c', primaryContrast: '#1a0f05', accent: '#f472b6', success: '#86d17a', warning: '#fcd34d', danger: '#f87171',
      info: '#7cb8f0', focus: '#fdba74', shadow: '#000000'
    },
    chart: ['#f97316', '#e11d48', '#f59e0b', '#a855f7', '#0ea5e9', '#84cc16', '#ec4899', '#14b8a6']
  },
  {
    id: 'graphite',
    light: {
      bg: '#f4f4f5', surface: '#ffffff', surface2: '#fafafa', border: '#e4e4e7', text: '#18181b', textMuted: '#52525b',
      primary: '#27272a', primaryContrast: '#fafafa', accent: '#52525b', success: '#15803d', warning: '#a16207', danger: '#b91c1c',
      info: '#1d4ed8', focus: '#71717a', shadow: '#000000'
    },
    dark: {
      bg: '#09090b', surface: '#121214', surface2: '#18181b', border: '#27272a', text: '#f4f4f5', textMuted: '#a1a1aa',
      primary: '#e4e4e7', primaryContrast: '#18181b', accent: '#a1a1aa', success: '#4ade80', warning: '#facc15', danger: '#f87171',
      info: '#60a5fa', focus: '#d4d4d8', shadow: '#000000'
    },
    chart: ['#52525b', '#2563eb', '#a1a1aa', '#0891b2', '#78716c', '#7c3aed', '#d4d4d8', '#be123c']
  },
  {
    id: 'lavender',
    light: {
      bg: '#f6f3fb', surface: '#ffffff', surface2: '#faf8fd', border: '#e7e0f3', text: '#2a2238', textMuted: '#5f5672',
      primary: '#7c4dcc', primaryContrast: '#ffffff', accent: '#b23c7e', success: '#2f7d4f', warning: '#965c00', danger: '#c23a55',
      info: '#3e63c9', focus: '#9b6ff0', shadow: '#2a2238'
    },
    dark: {
      bg: '#130f1c', surface: '#1b1626', surface2: '#221c30', border: '#332a47', text: '#ece6f7', textMuted: '#b0a5c6',
      primary: '#b79cf5', primaryContrast: '#130f1c', accent: '#f19ccb', success: '#8ad7a8', warning: '#f0c46c', danger: '#f58fa5',
      info: '#9cb6ff', focus: '#c9b2ff', shadow: '#000000'
    },
    chart: ['#9b6ff0', '#f472b6', '#60a5fa', '#34d399', '#fbbf24', '#c084fc', '#fb7185', '#22d3ee']
  },
  {
    id: 'mint',
    light: {
      bg: '#eff6f5', surface: '#ffffff', surface2: '#f6faf9', border: '#d7e7e4', text: '#0f2523', textMuted: '#4a6360',
      primary: '#0f766e', primaryContrast: '#ffffff', accent: '#6d28d9', success: '#15803d', warning: '#9e5900', danger: '#c53030',
      info: '#1d64a8', focus: '#0d9488', shadow: '#0f2523'
    },
    dark: {
      bg: '#071412', surface: '#0d1c1a', surface2: '#122522', border: '#1d3632', text: '#dff3ef', textMuted: '#93b5af',
      primary: '#2dd4bf', primaryContrast: '#062420', accent: '#a78bfa', success: '#4ade80', warning: '#fbbf24', danger: '#f87171',
      info: '#67b7f7', focus: '#5eead4', shadow: '#000000'
    },
    chart: ['#14b8a6', '#8b5cf6', '#f59e0b', '#3b82f6', '#f43f5e', '#84cc16', '#06b6d4', '#ec4899']
  },
  {
    id: 'sandstone',
    light: {
      bg: '#f5f2ec', surface: '#fffdf9', surface2: '#faf7f1', border: '#e6dfd2', text: '#2a2520', textMuted: '#645a4e',
      primary: '#7a5c2e', primaryContrast: '#ffffff', accent: '#2f6f73', success: '#4a7a2c', warning: '#945d00', danger: '#b3361f',
      info: '#2e6290', focus: '#9a7440', shadow: '#2a2520'
    },
    dark: {
      bg: '#14120f', surface: '#1c1915', surface2: '#24201b', border: '#36302a', text: '#eee7dc', textMuted: '#b3a796',
      primary: '#d8b27a', primaryContrast: '#1a140c', accent: '#7cc4c7', success: '#a3cf7a', warning: '#e8b85a', danger: '#ec8a74',
      info: '#8db8e0', focus: '#e3c391', shadow: '#000000'
    },
    chart: ['#b0864a', '#4f8a8b', '#a0522d', '#7c8f3a', '#8a6fa0', '#c2a14a', '#5a7fa8', '#b85c6e']
  },
  {
    id: 'terracotta',
    light: {
      bg: '#f7f1ee', surface: '#ffffff', surface2: '#fcf8f6', border: '#ecdcd5', text: '#2b1813', textMuted: '#6b4e45',
      primary: '#b5452b', primaryContrast: '#ffffff', accent: '#2f7a74', success: '#3c7a32', warning: '#945900', danger: '#b42318',
      info: '#2c5f94', focus: '#d0643f', shadow: '#2b1813'
    },
    dark: {
      bg: '#170e0b', surface: '#201410', surface2: '#2a1a15', border: '#3d2820', text: '#f5e6df', textMuted: '#c4a598',
      primary: '#f08a6c', primaryContrast: '#1c0d08', accent: '#6cc6bd', success: '#90cf7e', warning: '#f3bf5a', danger: '#ff8077',
      info: '#86b6e8', focus: '#f5a58c', shadow: '#000000'
    },
    chart: ['#c4553a', '#2f8a83', '#d99a3a', '#6a5acd', '#8a9a3a', '#c06a8a', '#4a7fb0', '#b07a50']
  }
];

export const PALETTE_IDS = PALETTES.map((p) => p.id);

export function getPalette(id) {
  return PALETTES.find((p) => p.id === id) || PALETTES.find((p) => p.id === DEFAULT_PALETTE);
}
