import { useId, useMemo, useState } from 'react';
import { useLocale } from '../i18n';
import { formatDuration, formatTimestamp } from '../i18n/formatters';
import useElementWidth from '../hooks/useElementWidth';
import { FieldsTable, ResultsList } from './AnalysisParts';
import Icon from './Icon';
import { SegmentActions, SegmentSourceNote } from './SegmentDownloads';

const BAR_Y = 4;
const BAR_H = 40;
const HEARD_GAP = 4;
const HEARD_H = 6;
const HEIGHT = BAR_Y + BAR_H + HEARD_GAP + HEARD_H + 4;

/**
 * Audio sections over the recording as a hand-rolled SVG bar: one block per section, the silences
 * that separate them (section cuts sit in the middle of a silence), leading and trailing silence
 * from Measurements, and the clips the listening model heard. The bar is a keyboard listbox
 * (arrow keys, Home, End); the selected section's results, fields, transcript, and audio downloads
 * appear below it.
 */
function SectionTimeline({ sections, durationMs = null, measurements = null, listening = null }) {
  const { t, locale } = useLocale();
  const [ref, width] = useElementWidth();
  const [selected, setSelected] = useState(0);
  const baseId = useId().replace(/[^A-Za-z0-9_-]/g, '');
  const list = useMemo(() => (Array.isArray(sections) ? sections : []), [sections]);
  const clips = useMemo(() => ((listening && listening.Clips) || []).filter((c) => c.Scope === 'Section'), [listening]);

  if (!Array.isArray(sections)) return <p className="muted">{t('sections.notRequested')}</p>;
  if (!list.length) return <p className="muted">{t('sections.none')}</p>;

  const total = Math.max(Number(durationMs) || 0, list[list.length - 1].EndMs || 0, 1);
  const sx = (ms) => (Math.max(0, Math.min(total, ms)) / total) * width;
  const index = Math.min(selected, list.length - 1);
  const section = list[index];
  const leading = measurements ? Number(measurements.LeadingSilenceMs || 0) : 0;
  const trailing = measurements ? Number(measurements.TrailingSilenceMs || 0) : 0;
  const optionId = (i) => baseId + '-section-' + i;
  const sectionClips = clips.filter((c) => c.Index === section.Index);

  const move = (next) => setSelected(Math.max(0, Math.min(list.length - 1, next)));
  const onKeyDown = (e) => {
    const keys = { ArrowRight: index + 1, ArrowDown: index + 1, ArrowLeft: index - 1, ArrowUp: index - 1, Home: 0, End: list.length - 1 };
    if (e.key in keys) {
      e.preventDefault();
      move(keys[e.key]);
    }
  };
  const label = (s) =>
    t('sections.sectionAria', {
      n: s.Index + 1,
      count: list.length,
      start: formatTimestamp(s.StartMs, locale),
      end: formatTimestamp(s.EndMs, locale),
      duration: formatDuration(s.DurationMs, locale)
    }) + (s.Errors && s.Errors.length ? ' ' + t('sections.hasErrors', { count: s.Errors.length }) : '');

  return (
    <div className="section-timeline">
      <SegmentSourceNote />
      <div
        ref={ref}
        className="section-bar"
        role="listbox"
        tabIndex={0}
        aria-label={t('sections.barAria', { count: list.length, duration: formatDuration(total, locale) })}
        aria-activedescendant={optionId(index)}
        onKeyDown={onKeyDown}
      >
        <svg width={width} height={HEIGHT} role="presentation" focusable="false">
          <defs>
            <pattern id={baseId + '-hatch'} width="6" height="6" patternUnits="userSpaceOnUse" patternTransform="rotate(45)">
              <rect width="6" height="6" className="section-silence-bg" />
              <line x1="0" y1="0" x2="0" y2="6" className="section-silence-line" />
            </pattern>
          </defs>
          {list.map((s, i) => {
            const x = sx(s.StartMs);
            const w = Math.max(2, sx(s.EndMs) - x - 1);
            const cls = 'section-rect' + (i % 2 ? ' alt' : '') + (i === index ? ' active' : '') + (s.Errors && s.Errors.length ? ' has-error' : '');
            return (
              <g key={s.Index} id={optionId(i)} role="option" aria-selected={i === index} aria-label={label(s)} onClick={() => move(i)} className="section-option">
                <title>{label(s)}</title>
                <rect x={x} y={BAR_Y} width={w} height={BAR_H} rx={3} className={cls} />
                {w > 22 ? (
                  <text x={x + 6} y={BAR_Y + BAR_H / 2 + 4} className="section-rect-label">
                    {s.Index + 1}
                  </text>
                ) : null}
                {s.Errors && s.Errors.length ? <rect x={x} y={BAR_Y + BAR_H - 4} width={w} height={4} className="section-error-mark" /> : null}
              </g>
            );
          })}
          {leading > 0 ? <rect x={0} y={BAR_Y} width={Math.max(2, sx(leading))} height={BAR_H} fill={'url(#' + baseId + '-hatch)'} className="section-silence" /> : null}
          {trailing > 0 ? <rect x={sx(total - trailing)} y={BAR_Y} width={Math.max(2, sx(total) - sx(total - trailing))} height={BAR_H} fill={'url(#' + baseId + '-hatch)'} className="section-silence" /> : null}
          {list.slice(1).map((s) => (
            <line key={'cut-' + s.Index} x1={sx(s.StartMs)} x2={sx(s.StartMs)} y1={BAR_Y - 2} y2={BAR_Y + BAR_H + 2} className="section-cut" />
          ))}
          {clips.map((c, i) => (
            <rect key={'clip-' + i} x={sx(c.StartMs)} y={BAR_Y + BAR_H + HEARD_GAP} width={Math.max(2, sx(c.EndMs) - sx(c.StartMs))} height={HEARD_H} rx={2} className="section-heard" />
          ))}
        </svg>
      </div>
      {/* Time runs left to right on the bar in every locale, so the ruler does too. */}
      <div className="scene-ruler" aria-hidden="true" dir="ltr">
        <span>{formatTimestamp(0, locale)}</span>
        <span>{formatTimestamp(total / 2, locale)}</span>
        <span>{formatTimestamp(total, locale)}</span>
      </div>
      <ul className="section-legend">
        <li>
          <span className="legend-swatch section-swatch" aria-hidden="true" />
          {t('sections.legend.section')}
        </li>
        {leading > 0 || trailing > 0 ? (
          <li>
            <span className="legend-swatch silence-swatch" aria-hidden="true" />
            {t('sections.legend.silence')}
          </li>
        ) : null}
        {clips.length ? (
          <li>
            <span className="legend-swatch heard-swatch" aria-hidden="true" />
            {t('sections.legend.heard')}
          </li>
        ) : null}
        <li className="muted">{t('sections.keyboardHint')}</li>
      </ul>

      <div className="scene-detail">
        <div className="scene-detail-header">
          <h3 className="panel-subtitle">{t('sections.heading', { n: section.Index + 1, count: list.length })}</h3>
          <span className="muted mono">
            {formatTimestamp(section.StartMs, locale)} - {formatTimestamp(section.EndMs, locale)} ({formatDuration(section.DurationMs, locale)})
          </span>
          <span className="scene-nav">
            <button type="button" className="icon-button" onClick={() => move(index - 1)} disabled={index === 0} aria-label={t('sections.prev')} title={t('sections.prev')}>
              <Icon name="chevronLeft" />
            </button>
            <button type="button" className="icon-button" onClick={() => move(index + 1)} disabled={index === list.length - 1} aria-label={t('sections.next')} title={t('sections.next')}>
              <Icon name="chevronRight" />
            </button>
          </span>
        </div>
        <SegmentActions startMs={section.StartMs} endMs={section.EndMs} />
        {sectionClips.length ? (
          <div className="section-clips">
            <div className="section-label">{t('sections.heardClips')}</div>
            <ul>
              {sectionClips.map((c, i) => (
                <li key={i}>
                  <Icon name="headphones" size={14} />
                  <span className="mono">
                    {formatTimestamp(c.StartMs, locale)} - {formatTimestamp(c.EndMs, locale)}
                  </span>
                  <span className="muted">({formatDuration(c.EndMs - c.StartMs, locale)})</span>
                  <SegmentActions startMs={c.StartMs} endMs={c.EndMs} compact />
                </li>
              ))}
            </ul>
          </div>
        ) : null}
        {section.Errors && section.Errors.length ? (
          <div className="banner banner-danger" role="alert">
            <Icon name="alert" />
            <div className="banner-body">
              <div className="banner-title">{t('results.errors', { count: section.Errors.length })}</div>
              <ul className="banner-list">
                {section.Errors.map((e, i) => (
                  <li key={i}>{e}</li>
                ))}
              </ul>
            </div>
          </div>
        ) : null}
        <div className="section-label">{t('sections.results')}</div>
        <ResultsList results={section.Results} />
        {section.Fields && section.Fields.length ? (
          <>
            <div className="section-label">{t('sections.fields')}</div>
            <FieldsTable fields={section.Fields} />
          </>
        ) : null}
        {section.TranscriptText ? (
          <>
            <div className="section-label">{t('sections.transcript')}</div>
            <p className="transcript-text">{section.TranscriptText}</p>
          </>
        ) : null}
      </div>
    </div>
  );
}

export default SectionTimeline;
