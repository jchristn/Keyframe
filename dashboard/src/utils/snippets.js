// Code snippets for a composed request. The API key is never embedded: snippets reference
// the KEYFRAME_API_KEY environment variable so copied commands do not leak credentials.
// Large base64 payloads are replaced with a placeholder so snippets stay readable.

const KEY_PLACEHOLDER_ENV = 'KEYFRAME_API_KEY';
const MAX_INLINE_DATA = 200;

/** Replace long base64 Data values (media uploads, keyframes) with a short placeholder. */
export function redactLargeData(value) {
  if (Array.isArray(value)) return value.map(redactLargeData);
  if (value && typeof value === 'object') {
    const out = {};
    for (const [k, v] of Object.entries(value)) {
      if (k === 'Data' && typeof v === 'string' && v.length > MAX_INLINE_DATA) out[k] = '<base64 data, ' + v.length + ' characters>';
      else out[k] = redactLargeData(v);
    }
    return out;
  }
  return value;
}

function shellQuote(text) {
  return "'" + String(text).replace(/'/g, "'\\''") + "'";
}

export function buildCurl({ method, url, body, headers = {} }) {
  const lines = ['curl -X ' + method + ' ' + shellQuote(url), '  -H "Authorization: Bearer $' + KEY_PLACEHOLDER_ENV + '"'];
  for (const [k, v] of Object.entries(headers)) lines.push('  -H ' + shellQuote(k + ': ' + v));
  if (body !== undefined && body !== null && body !== '') {
    lines.push("  -H 'Content-Type: application/json'");
    lines.push('  -d ' + shellQuote(typeof body === 'string' ? body : JSON.stringify(body, null, 2)));
  }
  return lines.join(' \\\n');
}

export function buildFetch({ method, url, body }) {
  const hasBody = body !== undefined && body !== null && body !== '';
  const json = hasBody ? (typeof body === 'string' ? body : JSON.stringify(body, null, 2)) : null;
  return [
    'const response = await fetch(' + JSON.stringify(url) + ', {',
    '  method: ' + JSON.stringify(method) + ',',
    '  headers: {',
    "    Authorization: 'Bearer ' + process.env." + KEY_PLACEHOLDER_ENV + (hasBody ? ',' : ''),
    hasBody ? "    'Content-Type': 'application/json'" : null,
    '  }' + (hasBody ? ',' : ''),
    hasBody ? '  body: JSON.stringify(' + json.replace(/\n/g, '\n  ') + ')' : null,
    '});',
    'const result = await response.json();'
  ]
    .filter((l) => l !== null)
    .join('\n');
}

export function buildPython({ method, url, body }) {
  const hasBody = body !== undefined && body !== null && body !== '';
  const json = hasBody ? (typeof body === 'string' ? body : JSON.stringify(body, null, 4)) : null;
  const pyJson = json ? json.replace(/\btrue\b/g, 'True').replace(/\bfalse\b/g, 'False').replace(/\bnull\b/g, 'None') : null;
  return [
    'import os',
    'import httpx',
    '',
    hasBody ? 'payload = ' + pyJson : null,
    hasBody ? '' : null,
    'response = httpx.request(',
    '    ' + JSON.stringify(method) + ',',
    '    ' + JSON.stringify(url) + ',',
    '    headers={"Authorization": "Bearer " + os.environ["' + KEY_PLACEHOLDER_ENV + '"]},',
    hasBody ? '    json=payload,' : null,
    '    timeout=600,',
    ')',
    'print(response.json())'
  ]
    .filter((l) => l !== null)
    .join('\n');
}

export function buildCSharp({ method, url, body }) {
  const hasBody = body !== undefined && body !== null && body !== '';
  const json = hasBody ? (typeof body === 'string' ? body : JSON.stringify(body, null, 2)) : null;
  const verb = method.charAt(0) + method.slice(1).toLowerCase();
  return [
    'using System.Net.Http;',
    'using System.Text;',
    '',
    'using (HttpClient client = new HttpClient())',
    '{',
    '    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + Environment.GetEnvironmentVariable("' + KEY_PLACEHOLDER_ENV + '"));',
    hasBody ? '    string json = """\n' + json.split('\n').map((l) => '        ' + l).join('\n') + '\n        """;' : null,
    '    using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.' + verb + ', "' + url + '"))',
    '    {',
    hasBody ? '        request.Content = new StringContent(json, Encoding.UTF8, "application/json");' : null,
    '        using (HttpResponseMessage response = await client.SendAsync(request))',
    '        {',
    '            Console.WriteLine(await response.Content.ReadAsStringAsync());',
    '        }',
    '    }',
    '}'
  ]
    .filter((l) => l !== null)
    .join('\n');
}

export const SNIPPET_LANGUAGES = [
  { id: 'curl', build: buildCurl },
  { id: 'fetch', build: buildFetch },
  { id: 'python', build: buildPython },
  { id: 'csharp', build: buildCSharp }
];

export function buildSnippets(request) {
  const safe = { ...request, body: request.body && typeof request.body === 'object' ? redactLargeData(request.body) : request.body };
  const out = {};
  for (const lang of SNIPPET_LANGUAGES) out[lang.id] = lang.build(safe);
  return out;
}
