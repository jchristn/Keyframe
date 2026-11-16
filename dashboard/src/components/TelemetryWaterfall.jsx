import { useMemo, useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocale } from '../i18n';
import { formatBytes, formatDuration, formatNumber } from '../i18n/formatters';
import useElementWidth from '../hooks/useElementWidth';
import DataTable from './DataTable';
import StatusBadge from './StatusBadge';

const ROW_H = 22;
const LABEL_W = 170;
const AXIS_H = 24;
const DEFAULT_CALL_LIMIT = 40;

function offsetMs(startUtc, origin) {
  const s = new Date(startUtc).getTime();
  return Number.isNaN(s) ? 0 : Math.max(0, s - origin);
}

/** Totals strip shown above the waterfall. */
export function TelemetryTotals({ telemetry }) {
  const { t, locale } = useLocale();
  const totals = (telemetry && telemetry.Totals) || {};
  const items = [
    ['telemetry.totalTime', formatDuration(telemetry && telemetry.TotalMs, locale)],
    ['telemetry.modelTime', formatDuration(totals.ModelMs, locale)],
    ['telemetry.modelCalls', formatNumber(totals.ModelCalls || 0, locale)],
    ['telemetry.failedCalls', formatNumber(totals.FailedModelCalls || 0, locale)],
    ['telemetry.promptTokens', formatNumber(totals.PromptTokens || 0, locale)],
    ['telemetry.completionTokens', formatNumber(totals.CompletionTokens || 0, locale)],
    ['telemetry.totalTokens', formatNumber(totals.TotalTokens || 0, locale)],
    ['telemetry.inputImages', formatNumber(totals.InputImages || 0, locale) + ' (' + formatBytes(totals.InputImageBytes || 0, locale) + ')']
  ];
  if (telemetry && telemetry.QueuedMs) items.splice(1, 0, ['telemetry.queued', formatDuration(telemetry.QueuedMs, locale)]);
  return (
    <dl className="stat-strip">
      {items.map(([k, v]) => (
        <div key={k} className="stat-strip-item">
          <dt>{t(k)}</dt>
          <dd className="mono">{v}</dd>
        </div>
      ))}
    </dl>
  );
}

/**
 * Waterfall of pipeline stages and model calls on one time axis, measured from the
 * telemetry StartUtc. Hover shows details; the model call table below lists tokens.
 */
function TelemetryWaterfall({ telemetry }) {
  const { t, locale } = useLocale();
  const [ref, width] = useElementWidth();
  const [hover, setHover] = useState(null);
  const [showAll, setShowAll] = useState(false);

  const origin = telemetry ? new Date(telemetry.StartUtc).getTime() : 0;
  const rows = useMemo(() => {
    if (!telemetry) return [];
    const stages = (telemetry.Stages || []).map((s, i) => ({
      id: 'stage-' + i,
      kind: 'stage',
      label: t('enums.stage.' + s.Name, { defaultValue: s.Name }),
      start: offsetMs(s.StartUtc, origin),
      duration: Number(s.DurationMs || 0),
      success: s.Success !== false,
      raw: s
    }));
    const calls = (telemetry.ModelCalls || []).map((c, i) => ({
      id: 'call-' + i,
      kind: 'call',
      label: t('enums.purpose.' + c.Purpose, { defaultValue: c.Purpose }) + (c.Target ? ' ' + c.Target : ''),
      start: offsetMs(c.StartUtc, origin),
      duration: Number(c.DurationMs || 0),
      success: c.Success !== false,
      raw: c
    }));
    return { stages, calls };
  }, [telemetry, origin, t]);

  if (!telemetry) return null;
  const calls = showAll ? rows.calls : rows.calls.slice(0, DEFAULT_CALL_LIMIT);
  const all = [...rows.stages, ...calls];
  const total = Math.max(
    Number(telemetry.TotalMs || 0),
    ...all.map((r) => r.start + r.duration),
    1
  );
  const narrow = width < 520;
  const labelW = narrow ? 0 : LABEL_W;
  const barArea = width - labelW - 12;
  const sx = (ms) => labelW + (ms / total) * barArea;
  const rowStep = narrow ? ROW_H + 14 : ROW_H;
  const height = AXIS_H + all.length * rowStep + 8;
  const ticks = [0, 0.25, 0.5, 0.75, 1].map((f) => f * total);

  return (
    <div className="waterfall">
      <div ref={ref} className="chart-wrap">
        <svg width={width} height={height} role="img" aria-label={t('telemetry.waterfallAria', { stages: rows.stages.length, calls: rows.calls.length })}>
          <g className="chart-axis">
            {ticks.map((tick, i) => (
              <g key={i}>
                <line x1={sx(tick)} x2={sx(tick)} y1={AXIS_H - 6} y2={height} className="chart-grid" />
                <text x={sx(tick)} y={12} textAnchor={i === 0 ? 'start' : i === ticks.length - 1 ? 'end' : 'middle'}>
                  {formatDuration(tick, locale)}
                </text>
              </g>
            ))}
          </g>
          {all.map((r, i) => {
            const top = AXIS_H + i * rowStep;
            const barY = narrow ? top + 13 : top + 4;
            const x = sx(r.start);
            const w = Math.max(2, sx(r.start + r.duration) - x);
            return (
              <g
                key={r.id}
                className="waterfall-row"
                onMouseEnter={(e) => setHover({ r, x: e.clientX, y: e.clientY })}
                onMouseMove={(e) => setHover({ r, x: e.clientX, y: e.clientY })}
                onMouseLeave={() => setHover(null)}
              >
                {i === rows.stages.length && rows.calls.length ? <line x1={0} x2={width} y1={top - 1} y2={top - 1} className="waterfall-divider" /> : null}
                <text x={narrow ? 0 : 0} y={narrow ? top + 9 : top + ROW_H / 2 + 4} className={'waterfall-label' + (r.kind === 'call' ? ' call' : '')}>
                  {r.label.length > 26 && !narrow ? r.label.slice(0, 25) + '…' : r.label}
                </text>
                <rect x={x} y={barY} width={w} height={ROW_H - 9} rx={2} className={(r.kind === 'stage' ? 'bar-stage' : 'bar-call') + (r.success ? '' : ' bar-failed')} />
              </g>
            );
          })}
        </svg>
      </div>
      {rows.calls.length > DEFAULT_CALL_LIMIT ? (
        <button type="button" className="button button-link button-sm" onClick={() => setShowAll((s) => !s)}>
          {showAll ? t('telemetry.showFewer') : t('telemetry.showAll', { count: rows.calls.length })}
        </button>
      ) : null}
      {hover
        ? createPortal(
            <div className="chart-tooltip" style={{ left: Math.min(hover.x + 12, window.innerWidth - 260), top: hover.y + 12 }} role="tooltip">
              <div className="tooltip-title">{hover.r.label}</div>
              <div>{t('telemetry.tooltip.start', { value: formatDuration(hover.r.start, locale) })}</div>
              <div>{t('telemetry.tooltip.duration', { value: formatDuration(hover.r.duration, locale) })}</div>
              {hover.r.kind === 'call' ? (
                <>
                  <div>{t('telemetry.tooltip.model', { endpoint: hover.r.raw.Endpoint, model: hover.r.raw.Model || '' })}</div>
                  <div>{t('telemetry.tooltip.tokens', { value: formatNumber(hover.r.raw.TotalTokens || 0, locale) })}</div>
                  <div>{t('telemetry.tooltip.attempts', { value: formatNumber(hover.r.raw.Attempts || 0, locale) })}</div>
                  {hover.r.raw.Error ? <div className="text-danger">{hover.r.raw.Error}</div> : null}
                </>
              ) : null}
              <div>{hover.r.success ? t('status.succeeded') : t('status.failed')}</div>
            </div>,
            document.body
          )
        : null}
    </div>
  );
}

/** Model call table with tokens and attempts. */
export function ModelCallTable({ telemetry, tableId = 'modelCalls' }) {
  const { t, locale } = useLocale();
  const calls = ((telemetry && telemetry.ModelCalls) || []).map((c, i) => ({ ...c, _i: i }));
  const columns = [
    { key: 'Purpose', label: t('telemetry.columns.purpose'), pinned: true, sortable: true, render: (c) => t('enums.purpose.' + c.Purpose, { defaultValue: c.Purpose }) },
    { key: 'Target', label: t('telemetry.columns.target'), sortable: true, mono: true, render: (c) => c.Target || '' },
    { key: 'Endpoint', label: t('telemetry.columns.endpoint'), sortable: true, mono: true },
    { key: 'Model', label: t('telemetry.columns.model'), sortable: true, mono: true },
    { key: 'DurationMs', label: t('telemetry.columns.duration'), sortable: true, align: 'right', nowrap: true, render: (c) => formatDuration(c.DurationMs, locale) },
    { key: 'Attempts', label: t('telemetry.columns.attempts'), sortable: true, align: 'right' },
    { key: 'PromptTokens', label: t('telemetry.columns.promptTokens'), sortable: true, align: 'right', defaultHidden: true, render: (c) => formatNumber(c.PromptTokens || 0, locale) },
    { key: 'CompletionTokens', label: t('telemetry.columns.completionTokens'), sortable: true, align: 'right', defaultHidden: true, render: (c) => formatNumber(c.CompletionTokens || 0, locale) },
    { key: 'TotalTokens', label: t('telemetry.columns.totalTokens'), sortable: true, align: 'right', render: (c) => formatNumber(c.TotalTokens || 0, locale) },
    { key: 'FinishReason', label: t('telemetry.columns.finishReason'), defaultHidden: true, mono: true },
    { key: 'Success', label: t('telemetry.columns.status'), pinned: true, render: (c) => <StatusBadge kind="custom" tone={c.Success ? 'success' : 'danger'} label={c.Success ? t('status.succeeded') : t('status.failed')} /> }
  ];
  return <DataTable tableId={tableId} columns={columns} rows={calls} rowKey={(c) => c._i} emptyTitle={t('telemetry.noCalls')} sortNote={t('table.sortLocal')} />;
}

export default TelemetryWaterfall;
