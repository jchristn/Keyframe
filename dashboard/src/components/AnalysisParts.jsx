import { useEffect, useId, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import Tabs from './Tabs';
import JsonViewer from './JsonViewer';
import StatusBadge from './StatusBadge';
import CopyButton from './CopyButton';
import TelemetryWaterfall, { ModelCallTable, TelemetryTotals } from './TelemetryWaterfall';
import SectionTimeline from './SectionTimeline';
import { useLocale } from '../i18n';
import { formatBytes, formatDuration, formatList, formatNumber, formatPercent, formatTimestamp } from '../i18n/formatters';
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

/** True when an endpoint has never been health-checked (the server treats it as usable). */
export function isUncheckedEndpoint(endpoint) {
  return !!endpoint && !endpoint.Healthy && !endpoint.LastCheckedUtc;
}

/** Health pill for an endpoint: healthy, unhealthy, or not checked yet. */
export function EndpointHealthBadge({ endpoint }) {
  const { t } = useTranslation();
  if (!endpoint) return null;
  if (isUncheckedEndpoint(endpoint)) return <StatusBadge tone="neutral" label={t('status.notChecked')} />;
  return <StatusBadge kind="health" value={!!endpoint.Healthy} />;
}

/**
 * Endpoint and model override picker. Lists active endpoints of one kind, optionally narrowed by
 * filter (for example audio-capable endpoints). showHealth labels every option with its health.
 */
export function ModelPicker({ endpoints, value, onChange, labelKey = 'model.endpoint', disabled = false, kind = 'Completion', filter = null, showHealth = false, help = null }) {
  const { t } = useTranslation();
  const epId = useId();
  const nameId = useId();
  const options = (endpoints || []).filter((e) => e.Kind === kind && e.Active && (!filter || filter(e)));
  const selected = options.find((e) => e.Name === value.Endpoint);
  const health = (e) => {
    if (showHealth) return ' · ' + (isUncheckedEndpoint(e) ? t('status.notChecked') : e.Healthy ? t('status.healthy') : t('status.unhealthy'));
    return e.Healthy ? '' : ' · ' + t('status.unhealthy');
  };
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
              {e.Name} ({t('enums.provider.' + e.Provider, { defaultValue: e.Provider })}){health(e)}
            </option>
          ))}
        </select>
        {help ? <p className="help-text">{help}</p> : null}
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

/** Parse and clamp a form value the way the server's setter does. Returns fallback when it is not a number. */
export function clampNumber(value, min, max, fallback, integer = true) {
  const n = integer ? parseInt(value, 10) : parseFloat(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}

/** Labeled number input that clamps to [min, max] when it loses focus. */
export function NumberField({ label, value, onChange, min, max, step = 1, help = null, disabled = false, error = null, placeholder = '' }) {
  const id = useId();
  const { t } = useTranslation();
  return (
    <div className="form-field form-field-sm">
      <label htmlFor={id} className="field-label">
        {label}
      </label>
      <input
        id={id}
        type="number"
        min={min}
        max={max}
        step={step}
        value={value}
        placeholder={placeholder}
        onChange={(e) => onChange(e.target.value)}
        onBlur={() => {
          if (value === '' || value === null || value === undefined) return;
          const n = Number(value);
          if (!Number.isFinite(n)) return;
          const clamped = Math.min(max, Math.max(min, n));
          if (clamped !== n) onChange(String(clamped));
        }}
        disabled={disabled}
        aria-invalid={!!error || undefined}
      />
      {help ? <p className="help-text">{help}</p> : null}
      {error ? <div className="field-error">{t(error)}</div> : null}
    </div>
  );
}

export const LISTEN_FORMATS = ['Mp3', 'Wav', 'Flac'];
export const LISTEN_SAMPLE_RATES = [8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000];
export const EMPTY_RENDITION = { Format: '', SampleRate: '', Channels: '', BitrateKbps: '' };

/** Wire AudioRenditionSpec from the editable rendition; only members the user set. Null when all are server defaults. */
export function toRenditionSpec(value) {
  const v = value || EMPTY_RENDITION;
  const out = {};
  if (LISTEN_FORMATS.includes(v.Format)) out.Format = v.Format;
  if (v.SampleRate !== '' && v.SampleRate !== null && v.SampleRate !== undefined) out.SampleRate = clampNumber(v.SampleRate, 8000, 48000, 16000);
  if (v.Channels !== '' && v.Channels !== null && v.Channels !== undefined) out.Channels = clampNumber(v.Channels, 1, 2, 1);
  if (v.Format === 'Mp3' && v.BitrateKbps !== '' && v.BitrateKbps !== null && v.BitrateKbps !== undefined && Number.isFinite(parseInt(v.BitrateKbps, 10))) {
    out.BitrateKbps = clampNumber(v.BitrateKbps, 16, 320, 64);
  }
  return Object.keys(out).length ? out : null;
}

/**
 * Collapsed "Audio quality" group for the audio sent to a listening model (Listen.Rendition or
 * Scenes.Audio.Rendition). Every control starts at "Server default"; bitrate applies to Mp3 only.
 * defaults: the server's Media.Listen* values when known, shown next to "Server default".
 */
export function AudioQualityOptions({ value, onChange, disabled = false, defaults = null, flacRejected = false }) {
  const { t, locale } = useLocale();
  const id = useId();
  const v = value || EMPTY_RENDITION;
  const set = (prop) => (e) => onChange({ ...v, [prop]: e.target.value });
  const serverDefault = (shown) => (shown === null || shown === undefined || shown === '' ? t('analyze.audioQuality.serverDefault') : t('analyze.audioQuality.serverDefaultValue', { value: shown }));
  const d = defaults || {};
  const changed = !!toRenditionSpec(v);
  return (
    <details className="option-details">
      <summary>
        {t('analyze.audioQuality.title')}
        {changed ? <span className="badge badge-accent badge-xs">{t('analyze.audioQuality.custom')}</span> : null}
      </summary>
      <p className="help-text">{t('analyze.audioQuality.help')}</p>
      <div className="form-row form-row-wrap audio-quality-grid">
        <div className="form-field form-field-sm">
          <label htmlFor={id + 'fmt'} className="field-label">
            {t('analyze.audioQuality.format')}
          </label>
          <select id={id + 'fmt'} value={v.Format} onChange={set('Format')} disabled={disabled}>
            <option value="">{serverDefault(d.ListenFormat ? t('enums.listenFormat.' + d.ListenFormat, { defaultValue: d.ListenFormat }) : null)}</option>
            {LISTEN_FORMATS.map((f) => (
              <option key={f} value={f}>
                {t('enums.listenFormat.' + f)}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field form-field-sm">
          <label htmlFor={id + 'rate'} className="field-label">
            {t('analyze.audioQuality.sampleRate')}
          </label>
          <select id={id + 'rate'} value={v.SampleRate} onChange={set('SampleRate')} disabled={disabled}>
            <option value="">{serverDefault(d.ListenSampleRate ? formatSampleRate(t, d.ListenSampleRate, locale) : null)}</option>
            {LISTEN_SAMPLE_RATES.map((r) => (
              <option key={r} value={String(r)}>
                {formatSampleRate(t, r, locale)}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field form-field-sm">
          <label htmlFor={id + 'ch'} className="field-label">
            {t('analyze.audioQuality.channels')}
          </label>
          <select id={id + 'ch'} value={v.Channels} onChange={set('Channels')} disabled={disabled}>
            <option value="">{serverDefault(d.ListenChannels ? formatChannels(t, d.ListenChannels) : null)}</option>
            <option value="1">{formatChannels(t, 1)}</option>
            <option value="2">{formatChannels(t, 2)}</option>
          </select>
        </div>
        <NumberField
          label={t('analyze.audioQuality.bitrate')}
          value={v.Format === 'Mp3' ? v.BitrateKbps : ''}
          onChange={(x) => onChange({ ...v, BitrateKbps: x })}
          min={16}
          max={320}
          placeholder={serverDefault(d.ListenBitrateKbps ? formatNumber(d.ListenBitrateKbps, locale) : null)}
          disabled={disabled || v.Format !== 'Mp3'}
          help={v.Format === 'Mp3' ? t('analyze.audioQuality.bitrateHelp') : t('analyze.audioQuality.bitrateMp3Only')}
        />
      </div>
      {v.Format === 'Flac' && flacRejected ? <p className="help-text text-warning">{t('analyze.audioQuality.flacRejected')}</p> : null}
    </details>
  );
}

/** "16 kHz" style label for a sample rate in Hz. */
export function formatSampleRate(t, hz, locale) {
  return t('units.kilohertz', { value: formatNumber(Number(hz) / 1000, locale, { maximumFractionDigits: 3 }) });
}

/** "Mono", "Stereo", or a channel count. */
export function formatChannels(t, channels) {
  const n = Number(channels);
  if (n === 1) return t('listening.mono');
  if (n === 2) return t('listening.stereo');
  return t('listening.channels', { count: n });
}

/** One-line description of an effective rendition: format, sample rate, channels, and (Mp3) bitrate. */
export function describeRendition(t, rendition, locale) {
  if (!rendition) return '';
  const parts = [];
  if (rendition.Format) parts.push(t('enums.listenFormat.' + rendition.Format, { defaultValue: rendition.Format }));
  if (rendition.SampleRate) parts.push(formatSampleRate(t, rendition.SampleRate, locale));
  if (rendition.Channels) parts.push(formatChannels(t, rendition.Channels));
  if (rendition.BitrateKbps && (!rendition.Format || rendition.Format === 'Mp3')) parts.push(formatNumber(rendition.BitrateKbps, locale, { style: 'unit', unit: 'kilobit-per-second' }));
  return formatList(parts, locale, 'unit');
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

const MEASUREMENT_ROWS = [
  ['IntegratedLufs', 'lufs'],
  ['LoudnessRangeLu', 'lu'],
  ['TruePeakDbtp', 'dbtp'],
  ['PeakDbfs', 'dbfs'],
  ['RmsDbfs', 'dbfs'],
  ['CrestFactorDb', 'db'],
  ['PeakCount', 'count'],
  ['ClippingLikely', 'bool'],
  ['SilenceRatio', 'percent'],
  ['SilenceCount', 'count'],
  ['LeadingSilenceMs', 'ms'],
  ['TrailingSilenceMs', 'ms'],
  ['Channels', 'channels'],
  ['StereoWidthDb', 'db'],
  ['EffectivelyMono', 'bool']
];

/** Format one measurement value with its unit. */
export function formatMeasurement(t, locale, key, unit, value) {
  if (value === null || value === undefined) return key === 'StereoWidthDb' ? t('measurements.monoSource') : t('common.notAvailable');
  if (unit === 'bool') return value ? t('common.yes') : t('common.no');
  if (unit === 'percent') return formatPercent(value, locale, 1);
  if (unit === 'ms') return formatDuration(value, locale);
  if (unit === 'count') return formatNumber(value, locale);
  if (unit === 'channels') return formatChannels(t, value);
  return t('units.' + unit, { value: formatNumber(value, locale, { maximumFractionDigits: 1 }) });
}

/**
 * Deterministic audio measurements as a key/value grid with copy buttons. Missing measurements
 * get an empty state that says whether Measure was off or failed.
 */
export function MeasurementsGrid({ measurements, failed = false }) {
  const { t, locale } = useLocale();
  if (!measurements) {
    return failed ? (
      <div className="banner banner-warning" role="status">
        <Icon name="alert" />
        <div className="banner-body">
          <div className="banner-title">{t('measurements.missingTitle')}</div>
          <div>{t('measurements.missingMessage')}</div>
        </div>
      </div>
    ) : (
      <p className="muted">{t('measurements.none')}</p>
    );
  }
  return (
    <div className="stack">
      <p className="help-text">{t('measurements.help')}</p>
      <dl className="kv-list measurements-grid">
        {MEASUREMENT_ROWS.map(([key, unit]) => {
          const raw = measurements[key];
          const text = formatMeasurement(t, locale, key, unit, raw);
          return (
            <div key={key} className="kv-item">
              <dt>
                {t('measurements.fields.' + key)}
                {raw !== null && raw !== undefined ? <CopyButton value={String(raw)} label={t('measurements.copy', { name: t('measurements.fields.' + key) })} /> : null}
              </dt>
              <dd>
                {unit === 'bool' && raw !== null && raw !== undefined ? (
                  <StatusBadge tone={key === 'ClippingLikely' && raw ? 'warning' : raw ? 'success' : 'neutral'} label={text} />
                ) : (
                  <span className="mono">{text}</span>
                )}
              </dd>
            </div>
          );
        })}
      </dl>
    </div>
  );
}

/** True when the Listening mode is a degraded one that deserves a warning badge. */
export function isDegradedListening(listening) {
  return !!listening && listening.Mode !== 'Full' && listening.Mode !== 'Scenes';
}

/** Listening badge: icon plus text, warning tone whenever the mode is not Full or Scenes. */
export function ListeningBadge({ listening }) {
  const { t } = useTranslation();
  if (!listening) return null;
  const degraded = isDegradedListening(listening);
  return (
    <span className={'badge ' + (degraded ? 'badge-warning' : 'badge-success')} data-testid="listening-badge">
      <Icon name={degraded ? 'alert' : 'check'} size={12} />
      {t('enums.listeningMode.' + listening.Mode, { defaultValue: String(listening.Mode) })}
    </span>
  );
}

/**
 * Whether, how, and through which model the audio was heard. Shown on every audio result and on
 * video results with scenes, so a transcript-only answer is never mistaken for a listened one.
 */
export function ListeningCard({ listening }) {
  const { t, locale } = useLocale();
  const titleId = useId();
  if (!listening) return null;
  const clips = listening.Clips || [];
  const total = Number(listening.DurationMs || 0);
  const heard = Number(listening.HeardMs || 0);
  const monoOrNone = (v) => (v ? <span className="mono">{v}</span> : t('common.none'));
  const rows = [
    ['listening.mode', t('enums.listeningMode.' + listening.Mode, { defaultValue: String(listening.Mode) })],
    listening.Reason ? ['listening.reason', t('enums.listenReason.' + listening.Reason, { defaultValue: String(listening.Reason) })] : null,
    ['listening.endpoint', monoOrNone(listening.Endpoint)],
    ['listening.model', monoOrNone(listening.Model)],
    ['listening.rendition', describeRendition(t, listening.Rendition, locale) || t('common.notAvailable')],
    [
      'listening.heard',
      t('listening.heardOf', {
        heard: formatTimestamp(heard, locale),
        total: formatTimestamp(total, locale),
        percent: formatPercent(total ? heard / total : 0, locale, 0)
      })
    ],
    ['listening.clipCount', formatNumber(clips.length, locale)]
  ].filter(Boolean);
  return (
    <section className={'listening-card' + (isDegradedListening(listening) ? ' degraded' : '')} aria-labelledby={titleId} data-testid="listening-card">
      <div className="listening-card-header">
        <Icon name="headphones" />
        <h3 id={titleId} className="panel-subtitle">
          {t('listening.title')}
        </h3>
        <ListeningBadge listening={listening} />
        <span className="muted">{listening.Listened ? t('listening.listened') : t('listening.notListened')}</span>
      </div>
      {listening.Reason ? <p className={'help-text' + (listening.Reason === 'EndpointUnhealthy' || listening.Reason === 'NoEndpointConfigured' || listening.Reason === 'EndpointLacksAudioInput' ? ' text-warning' : '')}>{t('enums.listenReasonHelp.' + listening.Reason, { defaultValue: '' })}</p> : null}
      {listening.Listened && listening.Mode !== 'Full' && listening.Mode !== 'Scenes' ? <p className="help-text text-warning">{t('listening.modeHelp.' + listening.Mode, { defaultValue: '' })}</p> : null}
      <dl className="kv-grid">
        {rows.map(([k, v]) => (
          <div key={k}>
            <dt>{t(k)}</dt>
            <dd>{v}</dd>
          </div>
        ))}
      </dl>
      {clips.length ? (
        <details className="listening-clips" open={clips.length <= 8}>
          <summary>{t('listening.clips', { count: clips.length })}</summary>
          <div className="table-scroll">
            <table className="data-table data-table-compact">
              <thead>
                <tr>
                  <th scope="col">{t('listening.columns.scope')}</th>
                  <th scope="col">{t('listening.columns.start')}</th>
                  <th scope="col">{t('listening.columns.end')}</th>
                  <th scope="col">{t('listening.columns.length')}</th>
                  <th scope="col">{t('listening.columns.size')}</th>
                </tr>
              </thead>
              <tbody>
                {clips.map((c, i) => (
                  <tr key={i}>
                    <td>
                      {c.Index === null || c.Index === undefined
                        ? t('enums.listenScope.' + c.Scope, { defaultValue: String(c.Scope) })
                        : t('listening.scopeIndex', { scope: t('enums.listenScope.' + c.Scope, { defaultValue: String(c.Scope) }), n: c.Index + 1 })}
                    </td>
                    <td className="mono">{formatTimestamp(c.StartMs, locale)}</td>
                    <td className="mono">{formatTimestamp(c.EndMs, locale)}</td>
                    <td className="mono">{formatDuration(Math.max(0, c.EndMs - c.StartMs), locale)}</td>
                    <td className="mono">{formatBytes(c.SizeBytes, locale)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </details>
      ) : null}
    </section>
  );
}

/**
 * Result tabs for a synchronous analysis: Results, Fields, Telemetry, Properties, Raw JSON, plus
 * Measurements and Sections when the response carries them and a Listening card above the tabs.
 */
export function AnalysisResultTabs({ response, filename = 'keyframe-result.json' }) {
  const { t } = useTranslation();
  const [tab, setTab] = useState('results');
  const invalid = (response.Fields || []).filter((f) => !f.Valid).length;
  const sections = Array.isArray(response.Sections) ? response.Sections : null;
  const tabs = [
    { id: 'results', label: t('results.tabs.results') },
    { id: 'fields', label: t('results.tabs.fields'), badge: invalid ? invalid : null },
    response.Measurements !== undefined ? { id: 'measurements', label: t('results.tabs.measurements') } : null,
    sections ? { id: 'sections', label: t('results.tabs.sections'), badge: sections.length } : null,
    { id: 'telemetry', label: t('results.tabs.telemetry') },
    { id: 'properties', label: t('results.tabs.properties') },
    { id: 'raw', label: t('results.tabs.raw') }
  ].filter(Boolean);
  return (
    <>
      <ListeningCard listening={response.Listening} />
      <Tabs tabs={tabs} active={tab} onChange={setTab} label={t('results.title')}>
        {tab === 'results' ? <ResultsList results={response.Results} /> : null}
        {tab === 'fields' ? <FieldsTable fields={response.Fields} /> : null}
        {tab === 'measurements' ? <MeasurementsGrid measurements={response.Measurements} failed={!!(response.Errors && response.Errors.length)} /> : null}
        {tab === 'sections' ? <SectionTimeline sections={sections} durationMs={response.Properties && response.Properties.DurationMs} measurements={response.Measurements} listening={response.Listening} /> : null}
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
    </>
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
