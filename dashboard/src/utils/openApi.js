// OpenAPI helpers for the API Explorer: flatten the document into operations, derive
// parameter defaults, and build example request bodies from schemas.

const METHODS = ['get', 'post', 'put', 'patch', 'delete', 'head', 'options'];

function resolveRef(ref, spec) {
  if (!ref || !ref.startsWith('#/')) return null;
  return ref
    .slice(2)
    .split('/')
    .reduce((node, part) => (node ? node[part.replace(/~1/g, '/').replace(/~0/g, '~')] : null), spec);
}

export function resolveSchema(schema, spec, depth = 0) {
  if (!schema || depth > 12) return schema || {};
  if (schema.$ref) return resolveSchema(resolveRef(schema.$ref, spec) || {}, spec, depth + 1);
  if (schema.allOf) {
    return schema.allOf.reduce(
      (acc, part) => {
        const r = resolveSchema(part, spec, depth + 1);
        return { ...acc, ...r, properties: { ...(acc.properties || {}), ...(r.properties || {}) } };
      },
      { type: 'object' }
    );
  }
  return schema;
}

/** Flatten an OpenAPI document into { id, tag, method, path, summary, description, parameters, requestBody, responses }. */
export function flattenOpenApiSpec(spec) {
  if (!spec || !spec.paths) return [];
  const ops = [];
  for (const [path, item] of Object.entries(spec.paths)) {
    const shared = item.parameters || [];
    for (const method of METHODS) {
      const op = item[method];
      if (!op) continue;
      const parameters = [...shared, ...(op.parameters || [])].map((p) => (p.$ref ? resolveRef(p.$ref, spec) : p)).filter(Boolean);
      ops.push({
        id: op.operationId || method.toUpperCase() + ' ' + path,
        tag: (op.tags && op.tags[0]) || 'Other',
        method: method.toUpperCase(),
        path,
        summary: op.summary || '',
        description: op.description || '',
        parameters,
        requestBody: op.requestBody || null,
        responses: op.responses || {}
      });
    }
  }
  return ops;
}

/** Group operations by tag, preserving the document's tag order where declared. */
export function groupByTag(ops, spec) {
  const order = ((spec && spec.tags) || []).map((t) => t.name);
  const groups = new Map();
  for (const op of ops) {
    if (!groups.has(op.tag)) groups.set(op.tag, []);
    groups.get(op.tag).push(op);
  }
  return [...groups.entries()]
    .sort((a, b) => {
      const ia = order.indexOf(a[0]);
      const ib = order.indexOf(b[0]);
      return (ia === -1 ? 999 : ia) - (ib === -1 ? 999 : ib) || a[0].localeCompare(b[0]);
    })
    .map(([tag, items]) => ({ tag, items }));
}

export function getParameterDefault(parameter) {
  const schema = parameter.schema || {};
  if (parameter.example !== undefined) return String(parameter.example);
  if (schema.example !== undefined) return String(schema.example);
  if (schema.default !== undefined) return String(schema.default);
  return '';
}

/** Example value for a schema: example, default, first enum, or a type-based zero value. */
export function exampleForSchema(schema, spec, depth = 0) {
  const s = resolveSchema(schema, spec, depth);
  if (!s || depth > 8) return null;
  if (s.example !== undefined) return s.example;
  if (s.default !== undefined) return s.default;
  if (Array.isArray(s.enum) && s.enum.length) return s.enum[0];
  switch (s.type) {
    case 'object': {
      const out = {};
      for (const [k, v] of Object.entries(s.properties || {})) out[k] = exampleForSchema(v, spec, depth + 1);
      return out;
    }
    case 'array':
      return s.items ? [exampleForSchema(s.items, spec, depth + 1)] : [];
    case 'integer':
    case 'number':
      return 0;
    case 'boolean':
      return false;
    case 'string':
      return s.format === 'date-time' ? new Date(0).toISOString() : '';
    default:
      return s.properties ? exampleForSchema({ ...s, type: 'object' }, spec, depth + 1) : null;
  }
}

export function getRequestBodyTemplate(requestBody, spec) {
  if (!requestBody || !requestBody.content) return '';
  const json = requestBody.content['application/json'];
  if (!json || !json.schema) return '';
  return JSON.stringify(exampleForSchema(json.schema, spec), null, 2);
}

/** Substitute {name} path parameters, URL-encoding each value. */
export function substitutePathParams(path, values) {
  return path.replace(/\{([^}]+)\}/g, (match, name) => (values[name] ? encodeURIComponent(values[name]) : match));
}

export function isDestructive(method, path) {
  return method === 'DELETE' || /\/bulk|\/restart/i.test(path);
}

export function headersToObject(headers) {
  const out = {};
  if (headers && typeof headers.forEach === 'function') headers.forEach((v, k) => (out[k] = v));
  return out;
}
