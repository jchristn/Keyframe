import { useMemo, useRef, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatDuration } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import FieldBuilder from '../components/FieldBuilder';
import ContextEditor, { createContextRow } from '../components/ContextEditor';
import PresetBar from '../components/PresetBar';
import StatusBadge from '../components/StatusBadge';
import Icon from '../components/Icon';
import { ErrorBanner, EmptyState, LoadingState } from '../components/States';
import { AnalysisResultTabs, MediaInput, ModelPicker, Problems, SnippetPanel, SummarizeOptions, toSummarizeSpec } from '../components/AnalysisParts';
import useRemote from '../hooks/useRemote';
import { KEY_PATTERN, createField, fromFieldSpec, toContextItems, toFieldSpec, validateContext, validateFields } from '../utils/fields';
import { isHttpUrl, readFileAsBase64 } from '../utils/media';
import { STORAGE_KEYS } from '../utils/storage';

export const DEFAULT_SUMMARIZE = { Enable: true, Key: 'Summary', MaxWords: 100, Style: 'Paragraph', Instructions: '' };

function defaultFields(t) {
  return [
    createField('Boolean', { Key: 'hasPeople', Prompt: t('samples.image.hasPeople') }),
    createField('StringList', { Key: 'objects', Prompt: t('samples.image.objects'), MaxItems: '8' }),
    createField('Enum', {
      Key: 'setting',
      Prompt: t('samples.image.setting'),
      Options: [t('samples.image.indoors'), t('samples.image.outdoors'), t('samples.image.unknown')]
    })
  ];
}

/** Validate the shared parts of an analysis form. Returns { media, summaryKey, fields, context, any }. */
export function validateAnalysisForm({ media, summarize, extractEnable, fields, context }) {
  const out = { media: null, summaryKey: null, fields: { byUid: {}, count: 0 }, context: [], functions: null };
  if (media.mode === 'upload' && !media.file) out.media = 'media.errors.fileRequired';
  if (media.mode === 'url' && !isHttpUrl(media.url)) out.media = 'media.errors.urlInvalid';
  if (summarize.Enable && !KEY_PATTERN.test((summarize.Key || '').trim())) out.summaryKey = 'summarize.keyInvalid';
  if (extractEnable) {
    out.fields = validateFields(fields, summarize.Enable ? summarize.Key : null);
    if (!fields.length) out.functions = 'analyze.errors.noFields';
  }
  out.context = validateContext(context);
  out.any = !!(out.media || out.summaryKey || out.fields.count || out.context.length || out.functions);
  return out;
}

export async function buildMediaSource(media) {
  if (media.mode === 'url') return { Url: media.url.trim() };
  const data = await readFileAsBase64(media.file);
  const source = { Data: data };
  if (media.file.type && media.file.type.includes('/')) source.MediaType = media.file.type;
  return source;
}

export function toModelSelection(model) {
  if (!model.Endpoint && !model.Name.trim()) return null;
  const out = {};
  if (model.Endpoint) out.Endpoint = model.Endpoint;
  if (model.Name.trim()) out.Name = model.Name.trim();
  return out;
}

/** Interactive image analysis: media, context, model, summary, typed fields, results. */
function AnalyzeImageView() {
  const { t, locale } = useLocale();
  const { apiClient, serverUrl } = useAuth();
  const { notify } = useToast();
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient]);

  const [media, setMedia] = useState({ mode: 'upload', file: null, url: '' });
  const [context, setContext] = useState([]);
  const [model, setModel] = useState({ Endpoint: '', Name: '' });
  const [summarize, setSummarize] = useState(DEFAULT_SUMMARIZE);
  const [extractEnable, setExtractEnable] = useState(true);
  const [mode, setMode] = useState('Combined');
  const [fields, setFields] = useState(() => defaultFields(t));
  const [includeProperties, setIncludeProperties] = useState(true);
  const [showErrors, setShowErrors] = useState(false);
  const [running, setRunning] = useState(false);
  const [response, setResponse] = useState(null);
  const [error, setError] = useState(null);
  const abortRef = useRef(null);

  const validation = validateAnalysisForm({ media, summarize, extractEnable, fields, context });
  const noFunction = !summarize.Enable && !extractEnable;

  const requestBody = useMemo(() => {
    const body = {
      Image: media.mode === 'url' ? { Url: media.url.trim() } : { Data: media.file ? '<base64 of ' + media.file.name + '>' : '<base64>' },
      Functions: {
        Summarize: toSummarizeSpec(summarize),
        Extract: { Enable: extractEnable, Mode: mode, Fields: extractEnable ? fields.map(toFieldSpec) : [] }
      },
      IncludeProperties: includeProperties
    };
    const ctx = toContextItems(context);
    if (ctx.length) body.Context = ctx;
    const m = toModelSelection(model);
    if (m) body.Model = m;
    return body;
  }, [media, summarize, extractEnable, mode, fields, includeProperties, context, model]);

  const run = async () => {
    setShowErrors(true);
    if (validation.any || noFunction) return;
    setRunning(true);
    setError(null);
    const controller = new AbortController();
    abortRef.current = controller;
    try {
      const body = { ...requestBody, Image: await buildMediaSource(media) };
      const result = await apiClient.analyzeImage(body, controller.signal);
      setResponse(result);
      notify(result.Success ? 'success' : 'error', result.Success ? 'analyze.toast.done' : 'analyze.toast.partial', {
        duration: formatDuration(result.Telemetry && result.Telemetry.TotalMs, locale)
      });
    } catch (err) {
      if (err && err.name === 'AbortError') notify('info', 'analyze.toast.canceled');
      else setError(err);
    } finally {
      setRunning(false);
      abortRef.current = null;
    }
  };

  const presetConfig = () => ({
    context: context.map(({ Label, Text }) => ({ Label, Text })),
    model,
    summarize,
    extractEnable,
    mode,
    fields: fields.map(toFieldSpec),
    includeProperties
  });
  const loadPreset = (c) => {
    setContext((c.context || []).map((r) => createContextRow(r.Label, r.Text)));
    setModel(c.model || { Endpoint: '', Name: '' });
    setSummarize({ ...DEFAULT_SUMMARIZE, ...(c.summarize || {}) });
    setExtractEnable(c.extractEnable !== false);
    setMode(c.mode || 'Combined');
    setFields((c.fields || []).map(fromFieldSpec));
    setIncludeProperties(c.includeProperties !== false);
    setShowErrors(false);
  };

  return (
    <div className="view">
      <PageHeader title={t('analyzeImage.title')} summary={t('analyzeImage.summary')} actions={<PresetBar storageKey={STORAGE_KEYS.imagePresets} current={presetConfig} onLoad={loadPreset} />} />
      <div className="analyze-layout">
        <form
          className="analyze-form"
          onSubmit={(e) => {
            e.preventDefault();
            run();
          }}
          noValidate
        >
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.media')}</h2>
            <MediaInput kind="image" value={media} onChange={setMedia} error={showErrors ? validation.media : null} disabled={running} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.context')}</h2>
            <ContextEditor rows={context} onChange={setContext} errors={showErrors ? validation.context : []} disabled={running} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.model')}</h2>
            {endpoints.error ? <ErrorBanner error={endpoints.error} onRetry={endpoints.reload} /> : null}
            <ModelPicker endpoints={endpoints.data} value={model} onChange={setModel} disabled={running} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.summary')}</h2>
            <SummarizeOptions value={summarize} onChange={setSummarize} disabled={running} keyError={showErrors ? validation.summaryKey : null} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.fields')}</h2>
            <div className="option-group">
              <label className="toggle-row">
                <input type="checkbox" checked={extractEnable} onChange={(e) => setExtractEnable(e.target.checked)} disabled={running} />
                <span>{t('extract.enable')}</span>
              </label>
              {extractEnable ? (
                <div className="segmented" role="group" aria-label={t('extract.mode')}>
                  {['Combined', 'PerField'].map((m) => (
                    <button key={m} type="button" className={'segment' + (mode === m ? ' active' : '')} aria-pressed={mode === m} onClick={() => setMode(m)} title={t('enums.extractModeHelp.' + m)}>
                      {t('enums.extractMode.' + m)}
                    </button>
                  ))}
                </div>
              ) : null}
            </div>
            {extractEnable ? (
              <>
                <p className="help-text">{t('enums.extractModeHelp.' + mode)}</p>
                <FieldBuilder fields={fields} onChange={setFields} errors={showErrors ? validation.fields.byUid : {}} disabled={running} />
                {showErrors && validation.functions ? <div className="field-error">{t(validation.functions)}</div> : null}
              </>
            ) : null}
            <label className="checkbox-row">
              <input type="checkbox" checked={includeProperties} onChange={(e) => setIncludeProperties(e.target.checked)} disabled={running} />
              <span>{t('analyze.includeProperties')}</span>
            </label>
          </section>
          <div className="form-actions sticky-actions">
            {showErrors && (validation.any || noFunction) ? (
              <span className="field-error" role="alert">
                {noFunction ? t('analyze.errors.noFunction') : t('analyze.errors.fixFields')}
              </span>
            ) : null}
            {running ? (
              <button type="button" className="button button-secondary" onClick={() => abortRef.current && abortRef.current.abort()}>
                <Icon name="stop" />
                {t('common.cancel')}
              </button>
            ) : null}
            <button type="submit" className="button button-primary" disabled={running}>
              <Icon name="play" />
              {running ? t('analyze.running') : t('analyze.run')}
            </button>
          </div>
          <details className="panel details-panel">
            <summary>{t('snippets.title')}</summary>
            <SnippetPanel method="POST" url={serverUrl + '/v1.0/image'} body={requestBody} />
          </details>
        </form>

        <section className="panel analyze-result" aria-labelledby="result-title" aria-busy={running}>
          <div className="panel-header">
            <h2 id="result-title" className="panel-title">
              {t('results.title')}
            </h2>
            {response ? (
              <span className="panel-actions">
                <StatusBadge kind="custom" tone={response.Success ? 'success' : 'danger'} label={response.Success ? t('status.succeeded') : t('status.partial')} />
                <span className="muted">{formatDuration(response.Telemetry && response.Telemetry.TotalMs, locale)}</span>
              </span>
            ) : null}
          </div>
          {error ? <ErrorBanner error={error} onRetry={run} /> : null}
          {running ? <LoadingState label={t('analyze.running')} /> : null}
          {!running && !response && !error ? <EmptyState icon="image" title={t('analyzeImage.emptyTitle')} message={t('analyzeImage.emptyMessage')} /> : null}
          {response && !running ? (
            <>
              <Problems warnings={response.Warnings} errors={response.Errors} />
              <AnalysisResultTabs response={response} filename={(response.Id || 'keyframe-image') + '.json'} />
            </>
          ) : null}
        </section>
      </div>
    </div>
  );
}

export default AnalyzeImageView;
