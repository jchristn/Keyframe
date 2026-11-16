// Deep merge used to lay a translated catalog over English, so a key that is somehow missing
// still renders (in English) instead of showing the raw key. CI blocks missing keys anyway.
export function deepMerge(base, override) {
  const out = { ...base };
  for (const [key, value] of Object.entries(override || {})) {
    if (value && typeof value === 'object' && !Array.isArray(value) && base && typeof base[key] === 'object') out[key] = deepMerge(base[key], value);
    else out[key] = value;
  }
  return out;
}
