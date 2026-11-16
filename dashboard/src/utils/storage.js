// Safe wrappers around localStorage. Storage can be unavailable (private mode, blocked site
// data), so every access is guarded and the dashboard keeps working without it.

export function readStorage(key, fallback = null) {
  try {
    const raw = localStorage.getItem(key);
    if (raw === null) return fallback;
    return JSON.parse(raw);
  } catch {
    return fallback;
  }
}

export function writeStorage(key, value) {
  try {
    if (value === undefined || value === null) localStorage.removeItem(key);
    else localStorage.setItem(key, JSON.stringify(value));
  } catch {
    // Ignore quota and availability errors.
  }
}

export function removeStorage(key) {
  try {
    localStorage.removeItem(key);
  } catch {
    // Ignore.
  }
}

export const STORAGE_KEYS = {
  serverUrl: 'keyframe.serverUrl',
  apiKey: 'keyframe.apiKey',
  appearance: 'keyframe.appearance',
  sidebarCollapsed: 'keyframe.sidebarCollapsed',
  imagePresets: 'keyframe.presets.image',
  videoPresets: 'keyframe.presets.video',
  audioPresets: 'keyframe.presets.audio',
  explorerHistory: 'keyframe.explorer.history',
  explorerSelected: 'keyframe.explorer.selected',
  columns: (table) => 'keyframe.columns.' + table,
  pageSize: (table) => 'keyframe.pageSize.' + table
};
