import { useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocale } from '../i18n';
import { formatAxisTime, formatDateTime, formatDuration, formatNumber } from '../i18n/formatters';
import useElementWidth from '../hooks/useElementWidth';
import Icon from './Icon';
import { RANGES } from '../utils/ranges';

const HEIGHT = 200;
const PAD = { top: 12, right: 12, bottom: 30, left: 44 };

function niceMax(value) {
  if (value <= 4) return 4;
  const pow = Math.pow(10, Math.floor(Math.log10(value)));
  const steps = [1, 2, 2.5, 5, 10];
  for (const s of steps) if (s * pow >= value) return s * pow;
  return 10 * pow;
}

/** Segmented control for Last Hour / Day / Week / Month. */
export function RangeSelector({ value, onChange }) {
  const { t } = useLocale();
  return (
    <div className="segmented" role="group" aria-label={t('chart.range')}>
      {RANGES.map((r) => (
        <button key={r.id} type="button" className={'segment' + (value === r.id ? ' active' : '')} aria-pressed={value === r.id} onClick={() => onChange(r.id)}>
          {t('chart.ranges.' + r.id)}
        </button>
      ))}
    </div>
  );
}

/**
 * Stacked SVG bar chart of request activity: success (green) stacked on failure (red).
 * buckets: [{ startUtc, endUtc, success, failure, averageDurationMs }], already normalized.
 * Tooltip renders through a portal so it escapes overflow containers.
 */
function ActivityChart({ buckets, bucketMinutes, onBucketClick = null, title = null, rangeId, onRangeChange, onRefresh, loading = false, loadedAt = null }) {
  const { t, locale } = useLocale();
  const [ref, width] = useElementWidth();
  const [hover, setHover] = useState(null);

  const data = buckets || [];
  const maxTotal = data.reduce((m, b) => Math.max(m, b.success + b.failure), 0);
  const yMax = niceMax(maxTotal);
  const innerW = width - PAD.left - PAD.right;
  const innerH = HEIGHT - PAD.top - PAD.bottom;
  const slot = data.length ? innerW / data.length : innerW;
  const barW = Math.max(1, slot * 0.72);
  const y = (v) => PAD.top + innerH - (v / yMax) * innerH;
  const ticks = [0, yMax / 2, yMax];
  const labelEvery = Math.max(1, Math.ceil(data.length / Math.max(2, Math.floor(innerW / 90))));
  const empty = maxTotal === 0;

  return (
    <section className="panel chart-panel" aria-label={title || t('chart.title')}>
      <div className="panel-header">
        <h2 className="panel-title">{title || t('chart.title')}</h2>
        <div className="panel-actions">
          {onRangeChange ? <RangeSelector value={rangeId} onChange={onRangeChange} /> : null}
          {onRefresh ? (
            <button type="button" className="icon-button" onClick={onRefresh} disabled={loading} aria-label={t('chart.refresh')} title={t('chart.refresh')}>
              <Icon name="refresh" className={loading ? 'spin' : ''} />
            </button>
          ) : null}
        </div>
      </div>
      <div className="chart-legend">
        <span className="legend-item">
          <span className="legend-swatch swatch-success" />
          {t('chart.success')}
        </span>
        <span className="legend-item">
          <span className="legend-swatch swatch-danger" />
          {t('chart.failure')}
        </span>
        {loadedAt ? <span className="legend-meta">{t('common.updatedAt', { time: formatDateTime(loadedAt, locale, { timeStyle: 'medium' }) })}</span> : null}
      </div>
      <div ref={ref} className="chart-wrap">
        <svg width={width} height={HEIGHT} role="img" aria-label={t('chart.ariaSummary', { count: data.reduce((s, b) => s + b.success + b.failure, 0) })}>
          <g className="chart-axis">
            {ticks.map((tick) => (
              <g key={tick}>
                <line x1={PAD.left} x2={width - PAD.right} y1={y(tick)} y2={y(tick)} className="chart-grid" />
                <text x={PAD.left - 6} y={y(tick)} dy="0.32em" textAnchor="end">
                  {formatNumber(tick, locale, { maximumFractionDigits: 1 })}
                </text>
              </g>
            ))}
            {data.map((b, i) =>
              i % labelEvery === 0 ? (
                <text key={b.startUtc} x={PAD.left + i * slot + slot / 2} y={HEIGHT - 10} textAnchor="middle">
                  {formatAxisTime(b.startUtc, locale, bucketMinutes)}
                </text>
              ) : null
            )}
          </g>
          {data.map((b, i) => {
            const x = PAD.left + i * slot + (slot - barW) / 2;
            const failH = (b.failure / yMax) * innerH;
            const succH = (b.success / yMax) * innerH;
            const base = PAD.top + innerH;
            return (
              <g
                key={b.startUtc}
                className={'chart-bar' + (onBucketClick && b.success + b.failure > 0 ? ' clickable' : '')}
                onMouseEnter={(e) => setHover({ b, x: e.clientX, y: e.clientY })}
                onMouseMove={(e) => setHover({ b, x: e.clientX, y: e.clientY })}
                onMouseLeave={() => setHover(null)}
                onClick={() => onBucketClick && b.success + b.failure > 0 && onBucketClick(b)}
              >
                <rect x={PAD.left + i * slot} y={PAD.top} width={slot} height={innerH} fill="transparent" />
                {b.failure > 0 ? <rect x={x} y={base - failH} width={barW} height={failH} className="bar-failure" /> : null}
                {b.success > 0 ? <rect x={x} y={base - failH - succH} width={barW} height={succH} className="bar-success" /> : null}
              </g>
            );
          })}
        </svg>
        {empty && !loading ? <div className="chart-empty">{t('chart.noData')}</div> : null}
      </div>
      {hover
        ? createPortal(
            <div className="chart-tooltip" style={{ left: Math.min(hover.x + 12, window.innerWidth - 240), top: hover.y + 12 }} role="tooltip">
              <div className="tooltip-title">
                {formatDateTime(hover.b.startUtc, locale, { dateStyle: 'short', timeStyle: 'short' })}
                {' - '}
                {formatDateTime(hover.b.endUtc, locale, { timeStyle: 'short' })}
              </div>
              <div>{t('chart.tooltip.total', { value: formatNumber(hover.b.success + hover.b.failure, locale) })}</div>
              <div>{t('chart.tooltip.success', { value: formatNumber(hover.b.success, locale) })}</div>
              <div>{t('chart.tooltip.failure', { value: formatNumber(hover.b.failure, locale) })}</div>
              <div>{t('chart.tooltip.avg', { value: formatDuration(hover.b.averageDurationMs, locale) })}</div>
            </div>,
            document.body
          )
        : null}
    </section>
  );
}

export default ActivityChart;
