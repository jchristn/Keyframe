import { useEffect, useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import Tabs from './Tabs';
import JsonViewer from './JsonViewer';
import StatusBadge from './StatusBadge';
import CopyButton from './CopyButton';
import TelemetryWaterfall, { ModelCallTable, TelemetryTotals } from './TelemetryWaterfall';
import { useLocale } from '../i18n';
import { formatBytes, formatDuration, formatNumber } from '../i18n/formatters';
import { SNIPPET_LANGUAGES, buildSnippets } from '../utils/snippets';

/** Upload-or-URL media picker. value: { mode, file, url }. */
export function MediaInput({ kind, value, onChange, error = null, disabled = false }) {
  const { t, locale } = useLocale();
  const fileId = useId();
  const urlId = useId();
  const [dragging, setDragging] = useState(false);
  const [preview, setPreview] = useState(null);

  useEffect(() => {
    if (!value.file || kind !== 'image') {
      setPreview(null);
      return undefined;
    }
    const url = URL.createObjectURL(value.file);
    setPreview(url);
    return () => URL.revokeObjectURL(url);
  }, [value.file, kind]);

  const accept = kind === 'image' ? 'image/*' : kind === 'audio' ? 'audio/*,video/*' : 'video/*';
  const kindIcon = kind === 'image' ? 'image' : kind === 'audio' ? 'audio' : 'film';
  const dropKey = kind === 'image' ? 'media.dropImage' : kind === 'audio' ? 'media.dropAudio' : 'media.dropVideo';
  const urlKey = kind === 'image' ? 'media.imageUrl' : kind === 'audio' ? 'media.audioUrl' : 'media.videoUrl';
  return (
    <div className="media-input">
      <Tabs
        size="sm"
        label={t('media.source')}
        active={value.mode}
        onChange={(mode) => onChange({ ...value, mode })}
        tabs={[
          { id: 'upload', label: t('media.upload') },
          { id: 'url', label: t('media.url') }
        ]}
      >
        {value.mode === 'upload' ? (
          <div
            className={'dropzone' + (dragging ? ' dragging' : '') + (error ? ' invalid' : '')}
            onDragOver={(e) => {
              e.preventDefault();
              setDragging(true);
            }}
            onDragLeave={() => setDragging(false)}
            onDrop={(e) => {
              e.preventDefault();
              setDragging(false);
              const file = e.dataTransfer.files && e.dataTransfer.files[0];
              if (file) onChange({ ...value, file });
            }}
          >
            {preview ? <img className="dropzone-preview" src={preview} alt={t('media.previewAlt')} /> : <Icon name={kindIcon} size={28} />}
            <div className="dropzone-text">
              {value.file ? (
                <>
                  <strong className="mono">{value.file.name}</strong>
                  <span className="muted">
                    {formatBytes(value.file.size, locale)}
                    {value.file.type ? ' · ' + value.file.type : ''}
                  </span>
                </>
              ) : (
                <span>{t(dropKey)}</span>
              )}
            </div>
            <label htmlFor={fileId} className="button button-secondary button-sm">
              <Icon name="upload" />
              {value.file ? t('media.chooseAnother') : t('media.chooseFile')}
            </label>
            <input id={fileId} type="file" accept={accept} className="sr-only" disabled={disabled} onChange={(e) => onChange({ ...value, file: e.target.files[0] || null })} />
            {kind === 'video' ? <p className="help-text">{t('media.videoUploadHint')}</p> : null}
            {kind === 'audio' ? <p className="help-text">{t('media.audioUploadHint')}</p> : null}
          </div>
        ) : (
          <div className="form-field">
            <label htmlFor={urlId} className="field-label">
              {t(urlKey)}
            </label>
            <input
              id={urlId}
              type="url"
              className="mono"
              value={value.url}
              onChange={(e) => onChange({ ...value, url: e.target.value })}
              placeholder="https://"
              disabled={disabled}
              aria-invalid={!!error || undefined}
            />
            <p className="help-text">{t('media.urlHint')}</p>
          </div>
        )}
      </Tabs>
      {error ? (
        <div className="field-error" role="alert">
          {t(error)}
        </div>
      ) : null}
    </div>
  );
}

/** Endpoint and model override picker. Lists active completion endpoints. */
export function ModelPicker({ endpoints, value, onChange, labelKey = 'model.endpoint', disabled = false, kind = 'Completion' }) {
  const { t } = useTranslation();
  const epId = useId();
  const nameId = useId();
  const options = (endpoints || []).filter((e) => e.Kind === kind && e.Active);
  const selected = options.find((e) => e.Name === value.Endpoint);
  return (
    <div className="form-row">
      <div className="form-field">
        <label htmlFor={epId} className="field-label">
          {t(labelKey)}
        </label>
        <select id={epId} value={value.Endpoint} onChange={(e) => onChange({ ...value, Endpoint: e.target.value })} disabled={disabled}>
          <option value="">{t('model.useDefault')}</option>
          {options.map((e) => (
            <option key={e.Name} value={e.Name}>
              {e.Name} ({t('enums.provider.' + e.Provider, { defaultValue: e.Provider })}){e.Healthy ? '' : ' · ' + t('status.unhealthy')}
            </option>
          ))}
        </select>
      </div>
      <div className="form-field">
        <label htmlFor={nameId} className="field-label">
          {t('model.name')}
        </label>
        <input
          id={nameId}
          className="mono"
          value={value.Name}
          onChange={(e) => onChange({ ...value, Name: e.target.value })}
          placeholder={selected && selected.Model ? selected.Model : t('model.namePlaceholder')}
          disabled={disabled}
          maxLength={256}
        />
      </div>
    </div>
  );
}

/** Summarize options: enable, key, max words, style, instructions. */
export function SummarizeOptions({ value, onChange, disabled = false, keyError = null, showKey = true }) {
  const { t } = useTranslation();
  const id = useId();
  const set = (prop, cast) => (e) => onChange({ ...value, [prop]: cast ? cast(e.target) : e.target.value });
  return (
    <div className="option-group">
      <label className="toggle-row">
        <input type="checkbox" checked={value.Enable} onChange={set('Enable', (el) => el.checked)} disabled={disabled} />
        <span>{t('summarize.enable')}</span>
      </label>
      {value.Enable ? (
        <div className="form-row form-row-wrap">
          {showKey ? (
            <div className="form-field form-field-sm">
              <label htmlFor={id + 'key'} className="field-label">
                {t('summarize.key')}
              </label>
              <input id={id + 'key'} className="mono" value={value.Key} onChange={set('Key')} disabled={disabled} maxLength={64} aria-invalid={!!keyError || undefined} />
              {keyError ? <div className="field-error">{t(keyError)}</div> : null}
            </div>
          ) : null}
          <div className="form-field form-field-sm">
            <label htmlFor={id + 'words'} className="field-label">
              {t('summarize.maxWords')}
            </label>
            <input id={id + 'words'} type="number" min={5} max={2000} value={value.MaxWords} onChange={set('MaxWords')} disabled={disabled} />
          </div>
          <div className="form-field form-field-sm">
            <label htmlFor={id + 'style'} className="field-label">
              {t('summarize.style')}
            </label>
            <select id={id + 'style'} value={value.Style} onChange={set('Style')} disabled={disabled}>
              {['Paragraph', 'Bullets', 'Caption'].map((s) => (
                <option key={s} value={s}>
                  {t('enums.summaryStyle.' + s)}
                </option>
              ))}
            </select>
          </div>
          <div className="form-field field-grid-wide">
            <label htmlFor={id + 'instr'} className="field-label">
              {t('summarize.instructions')}
            </label>
            <textarea id={id + 'instr'} rows={2} value={value.Instructions} onChange={set('Instructions')} disabled={disabled} maxLength={4000} placeholder={t('summarize.instructionsPlaceholder')} />
          </div>
        </div>
      ) : null}
    </div>
  );
}

/** Clamp an editable summarize block into a wire SummarizeSpec. */
export function toSummarizeSpec(value) {
  const words = Math.min(2000, Math.max(5, parseInt(value.MaxWords, 10) || 100));
  const spec = { Enable: !!value.Enable, MaxWords: words, Style: value.Style };
  if (value.Key && value.Key.trim()) spec.Key = value.Key.trim();
  if (value.Instructions && value.Instructions.trim()) spec.Instructions = value.Instructions.trim();
  return spec;
}

/** Render one Results value by JSON type. */
export function ResultValue({ value }) {
  const { t } = useTranslation();
  if (value === null || value === undefined) return <span className="muted">{t('common.null')}</span>;
  if (typeof value === 'boolean') return <StatusBadge kind="bool" value={value} label={value ? t('common.true') : t('common.false')} />;
  if (typeof value === 'number') return <span className="mono">{value}</span>;
  if (Array.isArray(value) && value.every((v) => typeof v !== 'object' || v === null)) {
    if (!value.length) return <span className="muted">{t('common.emptyList')}</span>;
    return (
      <span className="chip-list">
        {value.map((v, i) => (
          <span key={i} className="chip">
            {String(v)}
          </span>
        ))}
      </span>
    );
  }
  if (typeof value === 'object') return <JsonViewer value={value} maxHeight={200} />;
  return <span className="result-text">{String(value)}</span>;
}

/** Results map as a readable key/value list. */
export function ResultsList({ results }) {
  const { t } = useTranslation();
  const entries = Object.entries(results || {});
  if (!entries.length) return <div className="muted">{t('results.noResults')}</div>;
  return (
    <dl className="kv-list">
      {entries.map(([k, v]) => (
        <div key={k} className="kv-item">
          <dt className="mono">
            {k}
            <CopyButton value={typeof v === 'string' ? v : JSON.stringify(v)} label={t('results.copyValue', { key: k })} />
          </dt>
          <dd>
            <ResultValue value={v} />
          </dd>
        </div>
      ))}
    </dl>
  );
}

/** Fields[] table: validity, raw value, error, warnings. */
export function FieldsTable({ fields }) {
  const { t } = useTranslation();
  if (!fields || !fields.length) return <div className="muted">{t('results.noFields')}</div>;
  return (
    <div className="table-scroll">
      <table className="data-table">
        <thead>
          <tr>
            <th scope="col">{t('results.columns.key')}</th>
            <th scope="col">{t('results.columns.type')}</th>
            <th scope="col">{t('results.columns.value')}</th>
            <th scope="col">{t('results.columns.valid')}</th>
            <th scope="col">{t('results.columns.raw')}</th>
            <th scope="col">{t('results.columns.notes')}</th>
          </tr>
        </thead>
        <tbody>
          {fields.map((f) => (
            <tr key={f.Key}>
              <td className="mono cell-nowrap">{f.Key}</td>
              <td>{t('enums.fieldType.' + f.Type, { defaultValue: f.Type })}</td>
              <td>
                <ResultValue value={f.Value} />
              </td>
              <td>
                <StatusBadge kind="valid" value={f.Valid} />
              </td>
              <td className="mono cell-raw" title={f.RawValue || ''}>
                {f.RawValue}
              </td>
              <td>
                {f.Error ? <div className="text-danger">{f.Error}</div> : null}
                {(f.Warnings || []).map((w, i) => (
                  <div key={i} className="text-warning">
                    {w}
                  </div>
                ))}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** MediaProperties as a key/value grid. */
export function PropertiesGrid({ properties }) {
  const { t, locale } = useLocale();
  if (!properties) return <div className="muted">{t('results.noProperties')}</div>;
  const p = properties;
  const rows = [
    ['properties.mediaKind', p.MediaKind ? t('enums.mediaKind.' + p.MediaKind, { defaultValue: p.MediaKind }) : null],
    ['properties.contentType', p.ContentType],
    ['properties.format', p.Format || p.Container],
    ['properties.size', p.SizeBytes ? formatBytes(p.SizeBytes, locale) : null],
    ['properties.dimensions', p.Width && p.Height ? formatNumber(p.Width, locale) + ' × ' + formatNumber(p.Height, locale) : null],
    ['properties.duration', p.DurationMs ? formatDuration(p.DurationMs, locale) : null],
    ['properties.frameRate', p.FrameRate ? formatNumber(p.FrameRate, locale, { maximumFractionDigits: 3 }) : null],
    ['properties.bitRate', p.BitRate ? formatNumber(p.BitRate, locale) : null],
    ['properties.videoCodec', p.VideoCodec],
    ['properties.hasAudio', p.MediaKind === 'Video' ? (p.HasAudio ? t('common.yes') : t('common.no')) : null],
    ['properties.audioCodec', p.AudioCodec],
    ['properties.audioChannels', p.AudioChannels || p.Channels ? formatNumber(p.AudioChannels || p.Channels, locale) : null],
    ['properties.audioSampleRate', p.AudioSampleRate || p.SampleRate ? formatNumber(p.AudioSampleRate || p.SampleRate, locale) : null]
  ].filter(([, v]) => v !== null && v !== undefined && v !== '');
  const tags = Object.entries(p.Tags || {});
  return (
    <div>
      <dl className="kv-grid">
        {rows.map(([k, v]) => (
          <div key={k}>
            <dt>{t(k)}</dt>
            <dd>{v}</dd>
          </div>
        ))}
        {p.Sha256 ? (
          <div className="kv-grid-wide">
            <dt>{t('properties.sha256')}</dt>
            <dd className="mono break-all">
              {p.Sha256} <CopyButton value={p.Sha256} />
            </dd>
          </div>
        ) : null}
      </dl>
      {tags.length ? (
        <>
          <div className="section-label">{t('properties.tags')}</div>
          <dl className="kv-grid">
            {tags.map(([k, v]) => (
              <div key={k}>
                <dt className="mono">{k}</dt>
                <dd>{v}</dd>
              </div>
            ))}
          </dl>
        </>
      ) : null}
    </div>
  );
}

/** Warnings and errors lists from an analysis response. */
export function Problems({ warnings, errors }) {
  const { t } = useTranslation();
  if (!(warnings && warnings.length) && !(errors && errors.length)) return null;
  return (
    <div className="problems">
      {errors && errors.length ? (
        <div className="banner banner-danger" role="alert">
          <Icon name="alert" />
          <div className="banner-body">
            <div className="banner-title">{t('results.errors', { count: errors.length })}</div>
            <ul className="banner-list">
              {errors.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
            </ul>
            <div className="banner-hint">{t('results.errorsHint')}</div>
          </div>
        </div>
      ) : null}
      {warnings && warnings.length ? (
        <div className="banner banner-warning">
          <Icon name="alert" />
          <div className="banner-body">
            <div className="banner-title">{t('results.warnings', { count: warnings.length })}</div>
            <ul className="banner-list">
              {warnings.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
            </ul>
          </div>
        </div>
      ) : null}
    </div>
  );
}

/** Result tabs for an image analysis: Results, Fields, Telemetry, Properties, Raw JSON. */
export function AnalysisResultTabs({ response, filename = 'keyframe-result.json' }) {
  const { t } = useTranslation();
  const [tab, setTab] = useState('results');
  const invalid = (response.Fields || []).filter((f) => !f.Valid).length;
  const tabs = [
    { id: 'results', label: t('results.tabs.results') },
    { id: 'fields', label: t('results.tabs.fields'), badge: invalid ? invalid : null },
    { id: 'telemetry', label: t('results.tabs.telemetry') },
    { id: 'properties', label: t('results.tabs.properties') },
    { id: 'raw', label: t('results.tabs.raw') }
  ];
  return (
    <Tabs tabs={tabs} active={tab} onChange={setTab} label={t('results.title')}>
      {tab === 'results' ? <ResultsList results={response.Results} /> : null}
      {tab === 'fields' ? <FieldsTable fields={response.Fields} /> : null}
      {tab === 'telemetry' ? (
        <>
          <TelemetryTotals telemetry={response.Telemetry} />
          <TelemetryWaterfall telemetry={response.Telemetry} />
          <ModelCallTable telemetry={response.Telemetry} />
        </>
      ) : null}
      {tab === 'properties' ? <PropertiesGrid properties={response.Properties} /> : null}
      {tab === 'raw' ? <JsonViewer value={response} maxHeight="60vh" filename={filename} /> : null}
    </Tabs>
  );
}

/** Generated request snippets (cURL, fetch, Python, C#). Recomputed on every change. */
export function SnippetPanel({ method, url, body }) {
  const { t } = useTranslation();
  const [lang, setLang] = useState('curl');
  const snippets = useMemo(() => buildSnippets({ method, url, body }), [method, url, body]);
  return (
    <div className="snippet-panel">
      <Tabs size="sm" label={t('snippets.title')} active={lang} onChange={setLang} tabs={SNIPPET_LANGUAGES.map((l) => ({ id: l.id, label: t('snippets.languages.' + l.id) }))}>
        <JsonViewer value={snippets[lang]} maxHeight={320} />
        <p className="help-text">{t('snippets.keyNote')}</p>
      </Tabs>
    </div>
  );
}
