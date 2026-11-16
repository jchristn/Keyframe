import { useState } from 'react';
import { createPortal } from 'react-dom';
import { useLocale } from '../../i18n';
import { formatAxisTime, formatCompact, formatDateTime } from '../../i18n/formatters';
import useElementWidth from '../../hooks/useElementWidth';

const BUCKET_MINUTES = { minute: 1, hour: 60, day: 1440 };

function niceMax(value) {
  if (!(value > 0)) return 1;
  const pow = Math.pow(10, Math.floor(Math.log10(value)));
  for (const s of [1, 1.2, 1.5, 2, 2.5, 3, 4, 5, 6, 8, 10]) if (s * pow >= value) return s * pow;
  return 10 * pow;
}

/**
 * Hand-rolled SVG time series. type "line" draws one line per series (null values leave a
 * gap); type "bar" stacks the series per bucket. Colors are CSS custom properties from the
 * active palette (for example var(--chart-1)), so every palette and mode is covered.
 *
 * integer: counts, so the y axis uses whole-number ticks.
 * buckets: [{ startUtc }]; series: [{ key, label, color, values: (number|null)[] }].
 */
function TimeSeriesChart({ buckets, series, type = 'line', height = 220, bucket = 'hour', formatValue = null, ariaLabel, onBucketClick = null, emptyText = null, integer = false }) {
  const { t, locale } = useLocale();
  const [ref, width] = useElementWidth();
  const [hover, setHover] = useState(null);
  const fmt = formatValue || ((v) => formatCompact(v, locale));
  const n = buckets.length;
  const visible = series.filter((s) => !s.hidden);

  let max = 0;
  for (let i = 0; i < n; i += 1) {
    if (type === 'bar') {
      const sum = visible.reduce((acc, s) => acc + (Number(s.values[i]) || 0), 0);
      max = Math.max(max, sum);
    } else {
      for (const s of visible) if (typeof s.values[i] === 'number') max = Math.max(max, s.values[i]);
    }
  }
  const tickCount = height >= 260 ? 4 : 3;
  // Counts get whole-number ticks.
  const yMax = integer ? Math.max(tickCount, Math.ceil(niceMax(max) / tickCount) * tickCount) : niceMax(max);
  const ticks = Array.from({ length: tickCount + 1 }, (_, i) => (yMax * i) / tickCount);
  const labelChars = Math.max(...ticks.map((v) => fmt(v).length), 3);
  const pad = { top: 12, right: 14, bottom: 28, left: Math.min(96, 14 + labelChars * 7) };
  const innerW = Math.max(10, width - pad.left - pad.right);
  const innerH = height - pad.top - pad.bottom;
  const slot = n ? innerW / n : innerW;
  const xCenter = (i) => pad.left + i * slot + slot / 2;
  const y = (v) => pad.top + innerH - (v / yMax) * innerH;
  const labelEvery = Math.max(1, Math.ceil(n / Math.max(2, Math.floor(innerW / 92))));
  const empty = max === 0;
  const minutes = BUCKET_MINUTES[bucket] || 60;

  const linePath = (values) => {
    let d = '';
    let pen = false;
    values.forEach((v, i) => {
      if (typeof v !== 'number') {
        pen = false;
        return;
      }
      d += (pen ? 'L' : 'M') + xCenter(i).toFixed(1) + ' ' + y(v).toFixed(1) + ' ';
      pen = true;
    });
    return d.trim();
  };

  const barW = Math.max(1, slot * 0.7);

  return (
    <div ref={ref} className="chart-wrap timeseries">
      <svg width={width} height={height} role="img" aria-label={ariaLabel}>
        <g className="chart-axis">
          {ticks.map((tick) => (
            <g key={tick}>
              <line x1={pad.left} x2={width - pad.right} y1={y(tick)} y2={y(tick)} className="chart-grid" />
              <text x={pad.left - 6} y={y(tick)} dy="0.32em" textAnchor="end">
                {fmt(tick)}
              </text>
            </g>
          ))}
          {buckets.map((b, i) =>
            i % labelEvery === 0 ? (
              <text key={b.startUtc} x={xCenter(i)} y={height - 9} textAnchor="middle">
                {formatAxisTime(b.startUtc, locale, minutes)}
              </text>
            ) : null
          )}
        </g>
        {hover !== null ? <line x1={xCenter(hover.i)} x2={xCenter(hover.i)} y1={pad.top} y2={pad.top + innerH} className="chart-guide" /> : null}
        {type === 'bar'
          ? buckets.map((b, i) => {
              let acc = 0;
              return (
                <g key={b.startUtc}>
                  {visible.map((s) => {
                    const v = Number(s.values[i]) || 0;
                    if (!v) return null;
                    const top = y(acc + v);
                    const h = y(acc) - top;
                    acc += v;
                    return <rect key={s.key} x={xCenter(i) - barW / 2} y={top} width={barW} height={Math.max(0.5, h)} style={{ fill: s.color }} />;
                  })}
                </g>
              );
            })
          : visible.map((s) => (
              <g key={s.key}>
                <path d={linePath(s.values)} className="chart-line" style={{ stroke: s.color }} />
                {n <= 48
                  ? s.values.map((v, i) => (typeof v === 'number' ? <circle key={i} cx={xCenter(i)} cy={y(v)} r={2.5} style={{ fill: s.color }} /> : null))
                  : null}
              </g>
            ))}
        {buckets.map((b, i) => (
          <rect
            key={'hit-' + b.startUtc}
            x={pad.left + i * slot}
            y={pad.top}
            width={slot}
            height={innerH}
            fill="transparent"
            className={onBucketClick ? 'chart-hit clickable' : 'chart-hit'}
            onMouseEnter={(e) => setHover({ i, x: e.clientX, y: e.clientY })}
            onMouseMove={(e) => setHover({ i, x: e.clientX, y: e.clientY })}
            onMouseLeave={() => setHover(null)}
            onClick={() => onBucketClick && onBucketClick(b, i)}
          />
        ))}
      </svg>
      {empty ? <div className="chart-empty">{emptyText || t('chart.noData')}</div> : null}
      {hover !== null && buckets[hover.i]
        ? createPortal(
            <div className="chart-tooltip" style={{ left: Math.min(hover.x + 12, window.innerWidth - 270), top: hover.y + 12 }} role="tooltip">
              <div className="tooltip-title">{formatDateTime(buckets[hover.i].startUtc, locale, { dateStyle: 'medium', timeStyle: 'short' })}</div>
              {visible.map((s) => (
                <div key={s.key} className="tooltip-row">
                  <span className="legend-swatch" style={{ background: s.color }} />
                  <span>{s.label}</span>
                  <span className="mono tooltip-value">{typeof s.values[hover.i] === 'number' ? fmt(s.values[hover.i]) : t('common.notAvailable')}</span>
                </div>
              ))}
            </div>,
            document.body
          )
        : null}
    </div>
  );
}

/** Clickable legend that toggles series visibility. */
export function ChartLegend({ series, onToggle = null }) {
  return (
    <div className="chart-legend">
      {series.map((s) =>
        onToggle ? (
          <button key={s.key} type="button" className={'legend-item legend-toggle' + (s.hidden ? ' off' : '')} aria-pressed={!s.hidden} onClick={() => onToggle(s.key)}>
            <span className="legend-swatch" style={{ background: s.color }} />
            {s.label}
          </button>
        ) : (
          <span key={s.key} className="legend-item">
            <span className="legend-swatch" style={{ background: s.color }} />
            {s.label}
          </span>
        )
      )}
    </div>
  );
}

export default TimeSeriesChart;
