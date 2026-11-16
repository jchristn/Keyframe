import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatDuration, formatNumber } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import FieldBuilder from '../components/FieldBuilder';
import ContextEditor, { createContextRow } from '../components/ContextEditor';
import PresetBar from '../components/PresetBar';
import ProgressBar from '../components/ProgressBar';
import StatusBadge from '../components/StatusBadge';
import CopyableId from '../components/CopyableId';
import Tabs from '../components/Tabs';
import JsonViewer from '../components/JsonViewer';
import Icon from '../components/Icon';
import { EmptyState, ErrorBanner, LoadingState, Notice } from '../components/States';
import { FieldsTable, MediaInput, ModelPicker, Problems, PropertiesGrid, ResultsList, SnippetPanel, SummarizeOptions, toSummarizeSpec } from '../components/AnalysisParts';
import { TranscriptViewer } from '../components/SceneTimeline';
import { SegmentSourceProvider } from '../components/SegmentDownloads';
import TelemetryWaterfall, { ModelCallTable, TelemetryTotals } from '../components/TelemetryWaterfall';
import useRemote from '../hooks/useRemote';
import useInterval from '../hooks/useInterval';
import { createField, fromFieldSpec, toContextItems, toFieldSpec } from '../utils/fields';
import { STORAGE_KEYS } from '../utils/storage';
import { describeError } from '../utils/errors';
import { DEFAULT_SUMMARIZE, buildMediaSource, toModelSelection, validateAnalysisForm } from './AnalyzeImageView';
import { isActiveJob } from './JobsView';

const LANGUAGE_PATTERN = /^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$/;
const POLL_MS = 1500;
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

/** Live job card: status, progress, cancel, and the finished result in tabs. */
function AudioJobPanel({ job, result, resultError, onCancel, onRetryResult, canceling, file = null }) {
  const { t, locale } = useLocale();
  const [tab, setTab] = useState('transcript');
  const active = isActiveJob(job);
  const segments = result && result.Transcript ? (result.Transcript.Segments || []).length : 0;
  const invalid = result ? (result.Fields || []).filter((f) => !f.Valid).length : 0;
  return (
    <section className="panel analyze-result" aria-live="polite" aria-labelledby="audio-job-title">
      <div className="panel-header">
        <h2 id="audio-job-title" className="panel-title">
          {t('analyzeAudio.jobTitle')}
        </h2>
        <span className="panel-actions">
          <StatusBadge kind="job" value={job.Status} />
          {result && result.Telemetry ? <span className="muted">{formatDuration(result.Telemetry.TotalMs, locale)}</span> : null}
        </span>
      </div>
      <div className="header-meta">
        <CopyableId value={job.Id} />
        <Link to={'/dashboard/jobs/' + encodeURIComponent(job.Id)} className="button button-secondary button-sm">
          <Icon name="external" />
          {t('analyzeAudio.openJob')}
        </Link>
        {active ? (
          <button type="button" className="button button-danger button-sm" onClick={onCancel} disabled={canceling}>
            <Icon name="stop" />
            {t('jobs.cancel')}
          </button>
        ) : null}
      </div>
      {active ? (
        <>
          <ProgressBar percent={job.Progress ? job.Progress.Percent : 0} label={job.Progress && job.Progress.Stage ? t('enums.progressStage.' + job.Progress.Stage, { defaultValue: job.Progress.Stage }) : null} />
          <p className="help-text">{t('analyzeAudio.polling')}</p>
        </>
      ) : null}
      {job.Status === 'Failed' ? (
        <Notice tone="danger" icon="alert" title={t('jobDetail.failedTitle')}>
          <div className="mono break-all">{job.Error || t('jobDetail.noErrorDetail')}</div>
        </Notice>
      ) : null}
      {job.Status === 'Canceled' ? <Notice title={t('jobDetail.canceledTitle')}>{t('jobDetail.canceledMessage')}</Notice> : null}
      {job.Status === 'Interrupted' ? <Notice tone="warning" icon="alert" title={t('jobDetail.interruptedTitle')}>{t('jobDetail.interruptedMessage')}</Notice> : null}
      {resultError ? <ErrorBanner error={resultError} onRetry={onRetryResult} /> : null}
      {job.Status === 'Succeeded' && !result && !resultError ? <LoadingState label={t('jobDetail.loadingResult')} /> : null}
      {result ? (
        <>
          <Problems warnings={result.Warnings} errors={result.Errors} />
          <Tabs
            label={t('results.title')}
            active={tab}
            onChange={setTab}
            tabs={[
              { id: 'transcript', label: t('jobDetail.tabs.transcript'), badge: result.Transcript ? formatNumber(segments, locale) : null },
              { id: 'results', label: t('results.tabs.results') },
              { id: 'fields', label: t('results.tabs.fields'), badge: invalid || null },
              { id: 'telemetry', label: t('results.tabs.telemetry') },
              { id: 'properties', label: t('results.tabs.properties') },
              { id: 'raw', label: t('results.tabs.raw') }
            ]}
          >
            {tab === 'transcript' ? (
              <SegmentSourceProvider job={job} knownFile={file}>
                <TranscriptViewer transcript={result.Transcript} />
              </SegmentSourceProvider>
            ) : null}
            {tab === 'results' ? <ResultsList results={result.Results} /> : null}
            {tab === 'fields' ? <FieldsTable fields={result.Fields} /> : null}
            {tab === 'telemetry' ? (
              <>
                <TelemetryTotals telemetry={result.Telemetry} />
                <TelemetryWaterfall telemetry={result.Telemetry} />
                <ModelCallTable telemetry={result.Telemetry} tableId="audioModelCalls" />
              </>
            ) : null}
            {tab === 'properties' ? <PropertiesGrid properties={result.Properties} /> : null}
            {tab === 'raw' ? <JsonViewer value={result} maxHeight="60vh" filename={job.Id + '-result.json'} /> : null}
          </Tabs>
        </>
      ) : null}
    </section>
  );
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
  const [job, setJob] = useState(null);
  const [result, setResult] = useState(null);
  const [resultError, setResultError] = useState(null);
  const [canceling, setCanceling] = useState(false);

  const transcribeOn = transcribe.Enable;
  const base = validateAnalysisForm({ media, summarize: transcribeOn ? summarize : { ...summarize, Enable: false }, extractEnable: transcribeOn && extractEnable, fields, context });
  const languageError = transcribeOn && transcribe.Language && !LANGUAGE_PATTERN.test(transcribe.Language) ? 'analyzeVideo.errors.language' : null;
  const noFunction = !transcribeOn;
  const hasErrors = base.any || !!languageError || noFunction;

  const requestBody = useMemo(
    () => buildAudioRequest({ media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields }),
    [media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields]
  );

  const loadResult = useCallback(
    async (id) => {
      setResultError(null);
      try {
        setResult(await apiClient.getJobResult(id));
      } catch (err) {
        setResultError(err);
      }
    },
    [apiClient]
  );

  const poll = useCallback(async () => {
    if (!job) return;
    try {
      const next = await apiClient.getJob(job.Id);
      setJob(next);
      if (next.Status === 'Succeeded') loadResult(next.Id);
    } catch (err) {
      setResultError(err);
    }
  }, [apiClient, job, loadResult]);
  useInterval(poll, job && isActiveJob(job) ? POLL_MS : null);

  useEffect(() => {
    if (job && resultRef.current && resultRef.current.scrollIntoView) resultRef.current.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, [job && job.Id]); // eslint-disable-line react-hooks/exhaustive-deps

  const submit = async () => {
    setShowErrors(true);
    if (hasErrors) return;
    setSubmitting(true);
    setError(null);
    setResult(null);
    setResultError(null);
    try {
      const body = { ...requestBody, Audio: await buildMediaSource(media) };
      const response = await apiClient.analyzeAudio(body);
      const data = response.data || {};
      if (response.status === 200 && data.MediaKind) {
        // A synchronous result (Wait). Show it without polling.
        setJob({ Id: data.RequestId || '', Status: data.Success ? 'Succeeded' : 'Failed', MediaKind: 'Audio' });
        setResult(data);
      } else {
        setJob(data);
        notify('success', 'analyzeAudio.toast.submitted', { id: data.Id });
        if (data.Status === 'Succeeded') loadResult(data.Id);
      }
    } catch (err) {
      setError(err);
    } finally {
      setSubmitting(false);
    }
  };

  const cancel = async () => {
    if (!job) return;
    setCanceling(true);
    try {
      setJob(await apiClient.cancelJob(job.Id));
      notify('success', 'jobs.toast.canceled', { id: job.Id });
    } catch (err) {
      notify('error', 'analyzeAudio.toast.cancelFailed', { message: describeError(t, err) });
    } finally {
      setCanceling(false);
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
            <button type="submit" className="button button-primary" disabled={submitting || (job && isActiveJob(job))}>
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
            <AudioJobPanel job={job} file={media.mode === 'upload' ? media.file : null} result={result} resultError={resultError} onCancel={cancel} canceling={canceling} onRetryResult={() => loadResult(job.Id)} />
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
