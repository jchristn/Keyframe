// Activity chart range presets (FRONTEND_ARCHITECTURE.md): exact bucket sizes and counts.
export const RANGES = [
  { id: 'hour', bucketMinutes: 1, bucketCount: 60 },
  { id: 'day', bucketMinutes: 15, bucketCount: 96 },
  { id: 'week', bucketMinutes: 120, bucketCount: 84 },
  { id: 'month', bucketMinutes: 360, bucketCount: 120 }
];

export function getRange(id) {
  return RANGES.find((r) => r.id === id) || RANGES[1];
}

/** Window for a range ending at the next bucket boundary after now. */
export function rangeToParams(id, now = Date.now()) {
  const range = getRange(id);
  const bucketMs = range.bucketMinutes * 60000;
  const end = Math.ceil(now / bucketMs) * bucketMs;
  const start = end - bucketMs * range.bucketCount;
  return {
    fromUtc: new Date(start).toISOString(),
    toUtc: new Date(end).toISOString(),
    bucketMinutes: range.bucketMinutes
  };
}

/**
 * Normalize a RequestHistorySummary into exactly bucketCount buckets for the window, filling
 * missing buckets with zeros. The server gap-fills too, but the chart never relies on it.
 */
export function normalizeBuckets(summary, params, bucketCount) {
  const bucketMs = params.bucketMinutes * 60000;
  const start = new Date(params.fromUtc).getTime();
  const byStart = new Map();
  for (const b of (summary && summary.Buckets) || []) {
    byStart.set(new Date(b.BucketStartUtc).getTime(), b);
  }
  const out = [];
  for (let i = 0; i < bucketCount; i += 1) {
    const s = start + i * bucketMs;
    const b = byStart.get(s);
    out.push({
      startUtc: new Date(s).toISOString(),
      endUtc: new Date(s + bucketMs).toISOString(),
      success: b ? Number(b.SuccessCount || 0) : 0,
      failure: b ? Number(b.FailureCount || 0) : 0,
      averageDurationMs: b ? Number(b.AverageDurationMs || 0) : 0
    });
  }
  return out;
}

/** Convert an ISO string to the value a datetime-local input expects (local time). */
export function isoToLocalInput(iso) {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const pad = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

/** Convert a datetime-local input value (local time) to an ISO UTC string. */
export function localInputToIso(value) {
  if (!value) return '';
  const d = new Date(value);
  return Number.isNaN(d.getTime()) ? '' : d.toISOString();
}
