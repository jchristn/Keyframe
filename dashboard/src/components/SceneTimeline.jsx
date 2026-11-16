import { useMemo, useState } from 'react';
import { useLocale } from '../i18n';
import { formatDuration, formatTimestamp } from '../i18n/formatters';
import { keyframeSrc } from '../utils/media';
import { FieldsTable, ResultsList } from './AnalysisParts';
import Icon from './Icon';

/** Proportional scene strip, thumbnails, and per-scene detail. */
function SceneTimeline({ scenes, durationMs }) {
  const { t, locale } = useLocale();
  const [selected, setSelected] = useState(0);
  const list = useMemo(() => scenes || [], [scenes]);
  const total = Math.max(durationMs || 0, list.length ? list[list.length - 1].EndMs : 0, 1);
  const scene = list[selected] || null;

  if (!list.length) return <p className="muted">{t('jobDetail.noScenes')}</p>;

  return (
    <div className="scene-timeline">
      <div className="scene-strip" role="listbox" aria-label={t('jobDetail.sceneStrip')}>
        {list.map((s, i) => {
          const thumb = s.Keyframes && s.Keyframes.find((k) => k.Data);
          return (
            <button
              key={s.Index}
              type="button"
              role="option"
              aria-selected={i === selected}
              className={'scene-chip' + (i === selected ? ' active' : '') + (s.Errors && s.Errors.length ? ' has-error' : '')}
              style={{ flexGrow: Math.max(1, s.DurationMs), flexBasis: 0 }}
              onClick={() => setSelected(i)}
              title={t('jobDetail.sceneTitle', { n: s.Index + 1, start: formatTimestamp(s.StartMs, locale), end: formatTimestamp(s.EndMs, locale) })}
            >
              {thumb ? <img src={keyframeSrc(thumb.Data)} alt="" /> : null}
              <span className="scene-chip-label">{s.Index + 1}</span>
            </button>
          );
        })}
      </div>
      <div className="scene-ruler" aria-hidden="true">
        <span>{formatTimestamp(0, locale)}</span>
        <span>{formatTimestamp(total / 2, locale)}</span>
        <span>{formatTimestamp(total, locale)}</span>
      </div>

      {scene ? (
        <div className="scene-detail">
          <div className="scene-detail-header">
            <h3 className="panel-subtitle">{t('jobDetail.sceneHeading', { n: scene.Index + 1, count: list.length })}</h3>
            <span className="muted mono">
              {formatTimestamp(scene.StartMs, locale)} - {formatTimestamp(scene.EndMs, locale)} ({formatDuration(scene.DurationMs, locale)})
            </span>
            <span className="scene-nav">
              <button type="button" className="icon-button" onClick={() => setSelected((i) => Math.max(0, i - 1))} disabled={selected === 0} aria-label={t('jobDetail.prevScene')} title={t('jobDetail.prevScene')}>
                <Icon name="chevronLeft" />
              </button>
              <button type="button" className="icon-button" onClick={() => setSelected((i) => Math.min(list.length - 1, i + 1))} disabled={selected === list.length - 1} aria-label={t('jobDetail.nextScene')} title={t('jobDetail.nextScene')}>
                <Icon name="chevronRight" />
              </button>
            </span>
          </div>
          <div className="keyframe-row">
            {(scene.Keyframes || []).map((k, i) => (
              <figure key={i} className="keyframe">
                {k.Data ? (
                  <img src={keyframeSrc(k.Data)} alt={t('jobDetail.keyframeAlt', { time: formatTimestamp(k.TimestampMs, locale) })} />
                ) : (
                  <div className="keyframe-placeholder">
                    <Icon name="image" size={20} />
                    <span>{t('jobDetail.noThumbnail')}</span>
                  </div>
                )}
                <figcaption className="mono">{formatTimestamp(k.TimestampMs, locale)}</figcaption>
              </figure>
            ))}
          </div>
          {scene.Errors && scene.Errors.length ? (
            <ul className="text-danger">
              {scene.Errors.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
            </ul>
          ) : null}
          <div className="section-label">{t('jobDetail.sceneResults')}</div>
          <ResultsList results={scene.Results} />
          {scene.Fields && scene.Fields.length ? (
            <>
              <div className="section-label">{t('jobDetail.sceneFields')}</div>
              <FieldsTable fields={scene.Fields} />
            </>
          ) : null}
          {scene.TranscriptText ? (
            <>
              <div className="section-label">{t('jobDetail.sceneTranscript')}</div>
              <p className="transcript-text">{scene.TranscriptText}</p>
            </>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}

/** Transcript segments with timestamps and a local search filter. */
export function TranscriptViewer({ transcript }) {
  const { t, locale } = useLocale();
  const [query, setQuery] = useState('');
  if (!transcript) return <p className="muted">{t('jobDetail.noTranscript')}</p>;
  const segments = transcript.Segments || [];
  const q = query.trim().toLowerCase();
  const shown = q ? segments.filter((s) => s.Text.toLowerCase().includes(q)) : segments;
  return (
    <div className="transcript">
      <div className="transcript-toolbar">
        <span className="muted">{t('jobDetail.transcriptLanguage', { language: transcript.Language || t('common.unknown') })}</span>
        <input type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('jobDetail.searchTranscript')} aria-label={t('jobDetail.searchTranscript')} className="input-compact" />
      </div>
      {shown.length ? (
        <ol className="transcript-list">
          {shown.map((s, i) => (
            <li key={i}>
              <span className="mono transcript-time">
                {formatTimestamp(s.StartMs, locale)} - {formatTimestamp(s.EndMs, locale)}
              </span>
              <span>{s.Text}</span>
            </li>
          ))}
        </ol>
      ) : (
        <p className="muted">{q ? t('jobDetail.noTranscriptMatches') : transcript.Text || t('jobDetail.noTranscript')}</p>
      )}
    </div>
  );
}

export default SceneTimeline;
