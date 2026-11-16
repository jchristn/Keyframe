// Field builder model and the conversions to and from the wire FieldSpec
// (Keyframe.Core/Requests/FieldSpec.cs). Validation mirrors RequestValidator.cs so problems
// show inline before the request is sent; the server stays authoritative.

export const FIELD_TYPES = ['Boolean', 'String', 'Integer', 'Number', 'Enum', 'StringList', 'Object'];
export const KEY_PATTERN = /^[A-Za-z0-9_.-]{1,64}$/;
export const MAX_PROMPT_LENGTH = 4000;
export const MAX_FIELDS = 100;
export const MAX_OPTIONS = 100;
export const MAX_CONTEXT_ITEMS = 50;
export const MAX_CONTEXT_LENGTH = 20000;
export const MAX_LABEL_LENGTH = 128;

let counter = 0;
function nextUid() {
  counter += 1;
  return 'f' + Date.now().toString(36) + counter;
}

/** Which constraints apply to each type. */
export const TYPE_CONSTRAINTS = {
  Boolean: [],
  String: ['MaxLength'],
  Integer: ['Minimum', 'Maximum'],
  Number: ['Minimum', 'Maximum'],
  Enum: ['Options'],
  StringList: ['MaxItems'],
  Object: ['Schema']
};

/** A blank editable field. Numeric constraints are kept as strings while editing. */
export function createField(type = 'String', overrides = {}) {
  return {
    uid: nextUid(),
    Key: '',
    Type: type,
    Prompt: '',
    Required: false,
    Default: '',
    Options: [],
    Minimum: '',
    Maximum: '',
    MaxLength: '',
    MaxItems: '',
    Schema: '',
    ...overrides
  };
}

function numberOrNull(value, integer) {
  if (value === '' || value === null || value === undefined) return null;
  const n = Number(value);
  if (!Number.isFinite(n)) return null;
  return integer ? Math.trunc(n) : n;
}

/** Parse the editable Default string into a typed value for the field type. */
export function parseDefault(field) {
  const raw = field.Default;
  if (raw === '' || raw === null || raw === undefined) return null;
  switch (field.Type) {
    case 'Boolean':
      return raw === true || raw === 'true';
    case 'Integer':
      return numberOrNull(raw, true);
    case 'Number':
      return numberOrNull(raw, false);
    case 'StringList':
      return String(raw)
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean);
    case 'Object':
      try {
        return JSON.parse(raw);
      } catch {
        return null;
      }
    default:
      return String(raw);
  }
}

/** Convert an editable field to a wire FieldSpec, omitting constraints that do not apply. */
export function toFieldSpec(field) {
  const spec = { Key: field.Key.trim(), Type: field.Type, Prompt: field.Prompt.trim() };
  if (field.Required) spec.Required = true;
  const def = parseDefault(field);
  if (def !== null) spec.Default = def;
  const applies = TYPE_CONSTRAINTS[field.Type] || [];
  if (applies.includes('Options')) spec.Options = (field.Options || []).map((o) => String(o).trim()).filter(Boolean);
  if (applies.includes('Minimum')) {
    const v = numberOrNull(field.Minimum, field.Type === 'Integer');
    if (v !== null) spec.Minimum = v;
  }
  if (applies.includes('Maximum')) {
    const v = numberOrNull(field.Maximum, field.Type === 'Integer');
    if (v !== null) spec.Maximum = v;
  }
  if (applies.includes('MaxLength')) {
    const v = numberOrNull(field.MaxLength, true);
    if (v !== null) spec.MaxLength = v;
  }
  if (applies.includes('MaxItems')) {
    const v = numberOrNull(field.MaxItems, true);
    if (v !== null) spec.MaxItems = v;
  }
  if (applies.includes('Schema') && field.Schema && field.Schema.trim()) spec.Schema = field.Schema.trim();
  return spec;
}

/** Build an editable field from a wire FieldSpec (presets, duplicates). */
export function fromFieldSpec(spec) {
  const def = spec.Default;
  let defText = '';
  if (def !== null && def !== undefined) {
    if (Array.isArray(def)) defText = def.join(', ');
    else if (typeof def === 'object') defText = JSON.stringify(def);
    else defText = String(def);
  }
  return createField(spec.Type || 'String', {
    Key: spec.Key || '',
    Prompt: spec.Prompt || '',
    Required: !!spec.Required,
    Default: defText,
    Options: Array.isArray(spec.Options) ? spec.Options.slice() : [],
    Minimum: spec.Minimum ?? '',
    Maximum: spec.Maximum ?? '',
    MaxLength: spec.MaxLength ?? '',
    MaxItems: spec.MaxItems ?? '',
    Schema: spec.Schema || ''
  });
}

/**
 * Validate a list of editable fields. Returns { byUid: { uid: { prop: messageKey } }, count }.
 * Messages are i18n keys with optional params: { key, params }.
 */
export function validateFields(fields, summaryKey = null) {
  const byUid = {};
  let count = 0;
  const seen = new Map();
  const add = (uid, prop, key, params) => {
    byUid[uid] = byUid[uid] || {};
    if (!byUid[uid][prop]) {
      byUid[uid][prop] = { key, params };
      count += 1;
    }
  };

  fields.forEach((f) => {
    const key = (f.Key || '').trim();
    if (!KEY_PATTERN.test(key)) add(f.uid, 'Key', 'fieldBuilder.errors.keyInvalid');
    else {
      const lower = key.toLowerCase();
      if (seen.has(lower)) add(f.uid, 'Key', 'fieldBuilder.errors.keyDuplicate', { key });
      seen.set(lower, true);
      if (summaryKey && lower === String(summaryKey).toLowerCase()) add(f.uid, 'Key', 'fieldBuilder.errors.keySummary', { key });
    }
    const prompt = (f.Prompt || '').trim();
    if (!prompt) add(f.uid, 'Prompt', 'fieldBuilder.errors.promptRequired');
    else if (prompt.length > MAX_PROMPT_LENGTH) add(f.uid, 'Prompt', 'fieldBuilder.errors.promptTooLong', { max: MAX_PROMPT_LENGTH });

    if (f.Type === 'Enum') {
      const options = (f.Options || []).map((o) => String(o).trim());
      if (options.filter(Boolean).length < 1) add(f.uid, 'Options', 'fieldBuilder.errors.optionsRequired');
      else if (options.length > MAX_OPTIONS) add(f.uid, 'Options', 'fieldBuilder.errors.optionsTooMany', { max: MAX_OPTIONS });
    }
    if (f.Type === 'Integer' || f.Type === 'Number') {
      const min = f.Minimum === '' ? null : Number(f.Minimum);
      const max = f.Maximum === '' ? null : Number(f.Maximum);
      if (min !== null && !Number.isFinite(min)) add(f.uid, 'Minimum', 'fieldBuilder.errors.notNumber');
      if (max !== null && !Number.isFinite(max)) add(f.uid, 'Maximum', 'fieldBuilder.errors.notNumber');
      if (min !== null && max !== null && Number.isFinite(min) && Number.isFinite(max) && min > max)
        add(f.uid, 'Minimum', 'fieldBuilder.errors.minAboveMax');
    }
    if (f.Type === 'String' && f.MaxLength !== '' && !(Number(f.MaxLength) >= 1))
      add(f.uid, 'MaxLength', 'fieldBuilder.errors.atLeastOne');
    if (f.Type === 'StringList' && f.MaxItems !== '' && !(Number(f.MaxItems) >= 1))
      add(f.uid, 'MaxItems', 'fieldBuilder.errors.atLeastOne');
    if (f.Type === 'Object' && f.Schema && f.Schema.trim()) {
      try {
        JSON.parse(f.Schema);
      } catch {
        add(f.uid, 'Schema', 'fieldBuilder.errors.schemaJson');
      }
    }
    if (f.Default !== '' && f.Default !== null && f.Default !== undefined) {
      if ((f.Type === 'Integer' || f.Type === 'Number') && !Number.isFinite(Number(f.Default)))
        add(f.uid, 'Default', 'fieldBuilder.errors.notNumber');
      if (f.Type === 'Object') {
        try {
          JSON.parse(f.Default);
        } catch {
          add(f.uid, 'Default', 'fieldBuilder.errors.defaultJson');
        }
      }
      if (f.Type === 'Enum' && !(f.Options || []).some((o) => String(o).trim().toLowerCase() === String(f.Default).trim().toLowerCase()))
        add(f.uid, 'Default', 'fieldBuilder.errors.defaultNotOption');
    }
  });
  if (fields.length > MAX_FIELDS) {
    count += 1;
  }
  return { byUid, count };
}

/** Move an item within an array, returning a new array. */
export function moveItem(list, from, to) {
  if (to < 0 || to >= list.length || from === to) return list;
  const copy = list.slice();
  const [item] = copy.splice(from, 1);
  copy.splice(to, 0, item);
  return copy;
}

/** Context editor rows to wire ContextItem[]; empty rows are dropped. */
export function toContextItems(rows) {
  return (rows || [])
    .filter((r) => r.Text && r.Text.trim())
    .map((r) => (r.Label && r.Label.trim() ? { Label: r.Label.trim(), Text: r.Text.trim() } : { Text: r.Text.trim() }));
}

export function validateContext(rows) {
  const errors = [];
  const filled = (rows || []).filter((r) => (r.Text && r.Text.trim()) || (r.Label && r.Label.trim()));
  if (filled.length > MAX_CONTEXT_ITEMS) errors.push({ key: 'context.errors.tooMany', params: { max: MAX_CONTEXT_ITEMS } });
  let total = 0;
  filled.forEach((r, i) => {
    if (!r.Text || !r.Text.trim()) errors.push({ key: 'context.errors.textRequired', params: { n: i + 1 } });
    if (r.Label && r.Label.length > MAX_LABEL_LENGTH) errors.push({ key: 'context.errors.labelTooLong', params: { n: i + 1, max: MAX_LABEL_LENGTH } });
    total += (r.Text || '').length + (r.Label || '').length;
  });
  if (total > MAX_CONTEXT_LENGTH) errors.push({ key: 'context.errors.tooLong', params: { max: MAX_CONTEXT_LENGTH } });
  return errors;
}
