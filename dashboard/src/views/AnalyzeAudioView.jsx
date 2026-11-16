import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import PageHeader from '../components/PageHeader';
import FieldBuilder from '../components/FieldBuilder';
import ContextEditor, { createContextRow } from '../components/ContextEditor';
import PresetBar from '../components/PresetBar';
import Icon from '../components/Icon';
import { EmptyState, ErrorBanner } from '../components/States';
import { MediaInput, ModelPicker, SnippetPanel, SummarizeOptions, toSummarizeSpec } from '../components/AnalysisParts';
import AnalysisJobPanel, { useAnalysisJob } from '../components/AnalysisJobPanel';
import useRemote from '../hooks/useRemote';
import { createField, fromFieldSpec, toContextItems, toFieldSpec } from '../utils/fields';
import { STORAGE_KEYS } from '../utils/storage';
import { DEFAULT_SUMMARIZE, buildMediaSource, toModelSelection, validateAnalysisForm } from './AnalyzeImageView';

const LANGUAGE_PATTERN = /^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$/;
const EMPTY_MODEL = { Endpoint: '', Name: '' };
const DEFAULT_TRANSCRIBE = { Enable: true, Language: '', Prompt: '' };

/** Build the AudioAnalysisRequest body (contract section 1). Media is a placeholder until submit. */
export function buildAudioRequest({ media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields }) {
  const transcribeOn = !!transcribe.Enable;
  const body = {
    Audio: media.mode === 'url' ? { Url: media.url.trim() } : { Data: media.file ? '<base64 of ' + media.file.name + '>' : '<base64>' },
    Wait: false,
    Functions: {
      Transcribe: { Enable: transcribeOn },
      Summarize: transcribeOn ? toSummarizeSpec(summarize) : { ...toSummarizeSpec(summarize), Enable: false },
      Extract: { Enable: transcribeOn && extractEnable, Mode: mode, Fields: transcribeOn && extractEnable ? fields.map(toFieldSpec) : [] }
    }
  };
  if (transcribeOn && transcribe.Language.trim()) body.Functions.Transcribe.Language = transcribe.Language.trim();
  if (transcribeOn && transcribe.Prompt.trim()) body.Functions.Transcribe.Prompt = transcribe.Prompt.trim();
  const ctx = toContextItems(context);
  if (ctx.length) body.Context = ctx;
  const tm = toModelSelection(transcriptionModel);
  if (tm) body.TranscriptionModel = tm;
  const xm = toModelSelection(textModel);
  if (xm) body.TextModel = xm;
  return body;
}

/** Audio analysis: transcription, then summary and typed fields over the transcript. Runs as a job. */
function AnalyzeAudioView() {
  const { t } = useLocale();
  const { apiClient, serverUrl } = useAuth();
  const { notify } = useToast();
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient]);
  const idBase = useId();
  const resultRef = useRef(null);

  const [media, setMedia] = useState({ mode: 'upload', file: null, url: '' });
  const [context, setContext] = useState([]);
  const [transcriptionModel, setTranscriptionModel] = useState(EMPTY_MODEL);
  const [textModel, setTextModel] = useState(EMPTY_MODEL);
  const [transcribe, setTranscribe] = useState(DEFAULT_TRANSCRIBE);
  const [summarize, setSummarize] = useState({ ...DEFAULT_SUMMARIZE, MaxWords: 150 });
  const [extractEnable, setExtractEnable] = useState(false);
  const [mode, setMode] = useState('Combined');
  const [fields, setFields] = useState(() => [createField('Integer', { Key: 'speakerCount', Prompt: t('samples.audio.speakerCount') })]);
  const [showErrors, setShowErrors] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);
  const { job, result, resultError, canceling, track, cancel, loadResult, active } = useAnalysisJob();

  const transcribeOn = transcribe.Enable;
  const base = validateAnalysisForm({ media, summarize: transcribeOn ? summarize : { ...summarize, Enable: false }, extractEnable: transcribeOn && extractEnable, fields, context });
  const languageError = transcribeOn && transcribe.Language && !LANGUAGE_PATTERN.test(transcribe.Language) ? 'analyzeVideo.errors.language' : null;
  const noFunction = !transcribeOn;
  const hasErrors = base.any || !!languageError || noFunction;

  const requestBody = useMemo(
    () => buildAudioRequest({ media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields }),
    [media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields]
  );

  useEffect(() => {
    if (job && resultRef.current && resultRef.current.scrollIntoView) resultRef.current.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [job && job.Id]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = async () => {
    setShowErrors(true);
    if (hasErrors) return;
    setSubmitting(true);
    setError(null);
    try {
      const body = { ...requestBody, Audio: await buildMediaSource(media) };
      const response = await apiClient.analyzeAudio(body);
      const data = response.data || {};
      if (!(response.status === 200 && data.MediaKind)) notify('success', 'analyzeAudio.toast.submitted', { id: data.Id });
      track(data, 'Audio');
    } catch (err) {
      setError(err);
    } finally {
      setSubmitting(false);
    }
  };

  const presetConfig = () => ({
    context: context.map(({ Label, Text }) => ({ Label, Text })),
    transcriptionModel,
    textModel,
    transcribe,
    summarize,
    extractEnable,
    mode,
    fields: fields.map(toFieldSpec)
  });
  const loadPreset = (c) => {
    setContext((c.context || []).map((r) => createContextRow(r.Label, r.Text)));
    setTranscriptionModel(c.transcriptionModel || EMPTY_MODEL);
    setTextModel(c.textModel || EMPTY_MODEL);
    setTranscribe({ ...DEFAULT_TRANSCRIBE, ...(c.transcribe || {}) });
    setSummarize({ ...DEFAULT_SUMMARIZE, ...(c.summarize || {}) });
    setExtractEnable(!!c.extractEnable);
    setMode(c.mode || 'Combined');
    setFields((c.fields || []).map(fromFieldSpec));
    setShowErrors(false);
  };

  const disabled = submitting;
  return (
    <div className="view view-narrow">
      <PageHeader title={t('analyzeAudio.title')} summary={t('analyzeAudio.summary')} actions={<PresetBar storageKey={STORAGE_KEYS.audioPresets} current={presetConfig} onLoad={loadPreset} />} />
      <div className="analyze-layout">
        <form
          className="analyze-form"
          onSubmit={(e) => {
            e.preventDefault();
            submit();
          }}
          noValidate
        >
          {error ? <ErrorBanner error={error} onRetry={submit} titleKey={error.status === 429 ? 'analyzeVideo.errors.queueFull' : error.status === 404 ? 'analyzeAudio.errors.notSupported' : null} /> : null}
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.media')}</h2>
            <MediaInput kind="audio" value={media} onChange={setMedia} error={showErrors ? base.media : null} disabled={disabled} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.model')}</h2>
            {endpoints.error ? <ErrorBanner error={endpoints.error} onRetry={endpoints.reload} /> : null}
            <ModelPicker endpoints={endpoints.data} value={transcriptionModel} onChange={setTranscriptionModel} labelKey="model.transcriptionEndpoint" kind="Transcription" disabled={disabled} />
            <ModelPicker endpoints={endpoints.data} value={textModel} onChange={setTextModel} labelKey="model.textEndpoint" disabled={disabled} />
            <p className="help-text">{t('analyzeAudio.modelHelp')}</p>
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.context')}</h2>
            <ContextEditor rows={context} onChange={setContext} errors={showErrors ? base.context : []} disabled={disabled} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyzeVideo.sections.transcribe')}</h2>
            <label className="toggle-row">
              <input type="checkbox" checked={transcribe.Enable} onChange={(e) => setTranscribe({ ...transcribe, Enable: e.target.checked })} disabled={disabled} />
              <span>{t('analyzeVideo.transcribeEnable')}</span>
            </label>
            {transcribe.Enable ? (
              <div className="form-row">
                <div className="form-field">
                  <label htmlFor={idBase + 'lang'} className="field-label">
                    {t('analyzeVideo.language')}
                  </label>
                  <input
                    id={idBase + 'lang'}
                    className="mono"
                    value={transcribe.Language}
                    onChange={(e) => setTranscribe({ ...transcribe, Language: e.target.value })}
                    disabled={disabled}
                    placeholder={t('analyzeVideo.languagePlaceholder')}
                    maxLength={12}
                    aria-invalid={(showErrors && !!languageError) || undefined}
                  />
                  <p className="help-text">{t('analyzeAudio.languageHelp')}</p>
                  {showErrors && languageError ? <div className="field-error">{t(languageError)}</div> : null}
                </div>
                <div className="form-field">
                  <label htmlFor={idBase + 'prompt'} className="field-label">
                    {t('analyzeVideo.transcribePrompt')}
                  </label>
                  <input
                    id={idBase + 'prompt'}
                    value={transcribe.Prompt}
                    onChange={(e) => setTranscribe({ ...transcribe, Prompt: e.target.value })}
                    disabled={disabled}
                    maxLength={4000}
                    placeholder={t('analyzeVideo.transcribePromptPlaceholder')}
                  />
                </div>
              </div>
            ) : (
              <p className="help-text text-warning">{t('analyzeAudio.transcribeRequired')}</p>
            )}
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.summary')}</h2>
            <p className="help-text">{t('analyzeAudio.summaryHelp')}</p>
            <SummarizeOptions value={summarize} onChange={setSummarize} disabled={disabled || !transcribeOn} keyError={showErrors ? base.summaryKey : null} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.fields')}</h2>
            <div className="option-group">
              <label className="toggle-row">
                <input type="checkbox" checked={extractEnable} onChange={(e) => setExtractEnable(e.target.checked)} disabled={disabled || !transcribeOn} />
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
                <FieldBuilder fields={fields} onChange={setFields} errors={showErrors ? base.fields.byUid : {}} disabled={disabled || !transcribeOn} />
                {showErrors && base.functions ? <div className="field-error">{t(base.functions)}</div> : null}
              </>
            ) : null}
          </section>
          <div className="form-actions sticky-actions">
            {showErrors && hasErrors ? (
              <span className="field-error" role="alert">
                {noFunction ? t('analyzeAudio.transcribeRequired') : t('analyze.errors.fixFields')}
              </span>
            ) : null}
            <button type="submit" className="button button-primary" disabled={submitting || active}>
              <Icon name="play" />
              {submitting ? t('analyzeVideo.submitting') : t('analyzeAudio.submit')}
            </button>
          </div>
          <details className="panel details-panel">
            <summary>{t('snippets.title')}</summary>
            <SnippetPanel method="POST" url={serverUrl + '/v1.0/audio'} body={requestBody} />
          </details>
        </form>
        <div ref={resultRef}>
          {job ? (
            <AnalysisJobPanel job={job} file={media.mode === 'upload' ? media.file : null} result={result} resultError={resultError} onCancel={cancel} canceling={canceling} onRetryResult={() => loadResult(job.Id)} />
          ) : (
            <section className="panel">
              <EmptyState icon="audio" title={t('analyzeAudio.emptyTitle')} message={t('analyzeAudio.emptyMessage')} />
            </section>
          )}
        </div>
      </div>
    </div>
  );
}

export default AnalyzeAudioView;
