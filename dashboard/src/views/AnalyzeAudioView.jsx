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
import {
  AudioQualityOptions,
  EMPTY_RENDITION,
  EndpointHealthBadge,
  MediaInput,
  ModelPicker,
  NumberField,
  SnippetPanel,
  SummarizeOptions,
  clampNumber,
  isUncheckedEndpoint,
  toRenditionSpec,
  toSummarizeSpec
} from '../components/AnalysisParts';
import AnalysisJobPanel, { useAnalysisJob } from '../components/AnalysisJobPanel';
import useRemote from '../hooks/useRemote';
import { KEY_PATTERN, createField, fromFieldSpec, toContextItems, toFieldSpec, validateFields } from '../utils/fields';
import { STORAGE_KEYS } from '../utils/storage';
import { DEFAULT_SUMMARIZE, buildMediaSource, toModelSelection, validateAnalysisForm } from './AnalyzeImageView';

const LANGUAGE_PATTERN = /^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$/;
const EMPTY_MODEL = { Endpoint: '', Name: '' };
const DEFAULT_TRANSCRIBE = { Enable: true, Language: '', Prompt: '' };
export const DEFAULT_LISTEN = { Enable: true, Endpoint: '', Name: '', SampleClips: '4', ClipSeconds: '30' };
export const DEFAULT_SECTIONS = { Enable: false, SilenceDb: '-40', MinSilenceMs: '1500', MinSectionMs: '20000', MaxSectionMs: '600000', MaxSections: '100' };

/** Section detection clamped exactly like SectionDetectionSpec's setters. */
export function toSectionDetection(sections) {
  return {
    SilenceDb: clampNumber(sections.SilenceDb, -90, -10, -40, false),
    MinSilenceMs: clampNumber(sections.MinSilenceMs, 100, 30000, 1500),
    MinSectionMs: clampNumber(sections.MinSectionMs, 0, 600000, 20000),
    MaxSectionMs: clampNumber(sections.MaxSectionMs, 10000, 3600000, 600000),
    MaxSections: clampNumber(sections.MaxSections, 1, 1000, 100)
  };
}

/**
 * Build the AudioAnalysisRequest body. Media is a placeholder until submit. Summarize, Extract, and
 * the per-section functions need evidence, so they are sent only when Transcribe or Listen is on.
 */
export function buildAudioRequest({
  media,
  context,
  transcriptionModel,
  textModel,
  transcribe,
  summarize,
  extractEnable,
  mode,
  fields,
  listen = DEFAULT_LISTEN,
  rendition = EMPTY_RENDITION,
  measure = true,
  sections = DEFAULT_SECTIONS,
  sectionSummarize = null,
  sectionExtractEnable = false,
  sectionFields = []
}) {
  const transcribeOn = !!transcribe.Enable;
  const listenOn = !!listen.Enable;
  const evidence = transcribeOn || listenOn;
  const body = {
    Audio: media.mode === 'url' ? { Url: media.url.trim() } : { Data: media.file ? '<base64 of ' + media.file.name + '>' : '<base64>' },
    Wait: false,
    Listen: { Enable: listenOn },
    Functions: {
      Transcribe: { Enable: transcribeOn },
      Summarize: evidence ? toSummarizeSpec(summarize) : { ...toSummarizeSpec(summarize), Enable: false },
      Extract: { Enable: evidence && extractEnable, Mode: mode, Fields: evidence && extractEnable ? fields.map(toFieldSpec) : [] },
      Measure: { Enable: !!measure },
      Sections: { Enable: !!sections.Enable }
    }
  };
  if (listenOn) {
    const lm = toModelSelection({ Endpoint: listen.Endpoint || '', Name: listen.Name || '' });
    if (lm) body.Listen.Model = lm;
    const r = toRenditionSpec(rendition);
    if (r) body.Listen.Rendition = r;
    body.Listen.SampleClips = clampNumber(listen.SampleClips, 1, 32, 4);
    body.Listen.ClipSeconds = clampNumber(listen.ClipSeconds, 5, 300, 30);
  }
  if (sections.Enable) {
    const sectionSpec = sectionSummarize ? toSummarizeSpec(sectionSummarize) : { Enable: false };
    const sectionExtract = evidence && sectionExtractEnable;
    body.Functions.Sections = {
      Enable: true,
      Detection: toSectionDetection(sections),
      Summarize: evidence ? sectionSpec : { ...sectionSpec, Enable: false },
      Extract: { Enable: sectionExtract, Fields: sectionExtract ? sectionFields.map(toFieldSpec) : [] }
    };
  }
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

/**
 * What the dashboard can tell about the listening endpoint before submit: ok, unchecked, unhealthy,
 * none (no audio-capable endpoint), noDefault (nothing chosen and no default configured), or
 * unknown (the default is not visible without the admin key). The server decides again when the job runs.
 */
export function listeningStatus({ endpoints, chosen, defaultName, defaultKnown }) {
  if (!endpoints) return { state: 'loading', endpoint: null };
  const audio = endpoints.filter((e) => e.Kind === 'Completion' && e.Active && e.SupportsAudioInput);
  const find = (name) => audio.find((e) => String(e.Name).toLowerCase() === String(name || '').toLowerCase()) || null;
  const health = (e) => (e.Healthy ? 'ok' : isUncheckedEndpoint(e) ? 'unchecked' : 'unhealthy');
  if (!audio.length) return { state: 'none', endpoint: null };
  if (chosen) {
    const e = find(chosen);
    return e ? { state: health(e), endpoint: e } : { state: 'none', endpoint: null };
  }
  if (defaultKnown) {
    const e = find(defaultName);
    return e ? { state: health(e), endpoint: e } : { state: 'noDefault', endpoint: null };
  }
  if (audio.every((e) => !e.Healthy && !isUncheckedEndpoint(e))) return { state: 'allUnhealthy', endpoint: null };
  return { state: 'unknown', endpoint: null };
}

/** Audio analysis: listening, transcription, measurements, sections, summary, and typed fields. Runs as a job. */
function AnalyzeAudioView() {
  const { t } = useLocale();
  const { apiClient, serverUrl, isAdmin } = useAuth();
  const { notify } = useToast();
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient]);
  // The default listening endpoint and the Media.Listen* defaults are only visible with the admin key.
  const settings = useRemote(() => apiClient.getSettings(), [apiClient], { enabled: !!isAdmin });
  const idBase = useId();
  const resultRef = useRef(null);

  const [media, setMedia] = useState({ mode: 'upload', file: null, url: '' });
  const [context, setContext] = useState([]);
  const [transcriptionModel, setTranscriptionModel] = useState(EMPTY_MODEL);
  const [textModel, setTextModel] = useState(EMPTY_MODEL);
  const [listen, setListen] = useState(DEFAULT_LISTEN);
  const [rendition, setRendition] = useState(EMPTY_RENDITION);
  const [transcribe, setTranscribe] = useState(DEFAULT_TRANSCRIBE);
  const [measure, setMeasure] = useState(true);
  const [summarize, setSummarize] = useState({ ...DEFAULT_SUMMARIZE, MaxWords: 150 });
  const [extractEnable, setExtractEnable] = useState(false);
  const [mode, setMode] = useState('Combined');
  const [fields, setFields] = useState(() => [createField('Integer', { Key: 'speakerCount', Prompt: t('samples.audio.speakerCount') })]);
  const [sections, setSections] = useState(DEFAULT_SECTIONS);
  const [sectionSummarize, setSectionSummarize] = useState({ ...DEFAULT_SUMMARIZE, MaxWords: 60 });
  const [sectionExtractEnable, setSectionExtractEnable] = useState(false);
  const [sectionFields, setSectionFields] = useState(() => [createField('String', { Key: 'topic', Prompt: t('samples.audio.sectionTopic') })]);
  const [showErrors, setShowErrors] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);
  const { job, result, resultError, canceling, track, cancel, loadResult, active } = useAnalysisJob();

  const transcribeOn = transcribe.Enable;
  const listenOn = listen.Enable;
  const evidence = transcribeOn || listenOn;
  const base = validateAnalysisForm({ media, summarize: evidence ? summarize : { ...summarize, Enable: false }, extractEnable: evidence && extractEnable, fields, context });
  const languageError = transcribeOn && transcribe.Language && !LANGUAGE_PATTERN.test(transcribe.Language) ? 'analyzeVideo.errors.language' : null;
  const detection = toSectionDetection(sections);
  const sectionRangeError = sections.Enable && detection.MaxSectionMs < detection.MinSectionMs ? 'analyzeAudio.errors.sectionRange' : null;
  const sectionFunctions = sections.Enable && evidence;
  const sectionFieldErrors = sectionFunctions && sectionExtractEnable ? validateFields(sectionFields, sectionSummarize.Enable ? sectionSummarize.Key : null) : { byUid: {}, count: 0 };
  const sectionNoFields = sectionFunctions && sectionExtractEnable && !sectionFields.length ? 'analyze.errors.noFields' : null;
  const sectionKeyError = sectionFunctions && sectionSummarize.Enable && !KEY_PATTERN.test((sectionSummarize.Key || '').trim()) ? 'summarize.keyInvalid' : null;
  const noFunction = !transcribeOn && !measure && !sections.Enable && !(evidence && (summarize.Enable || extractEnable));
  const hasErrors = base.any || !!languageError || noFunction || !!sectionRangeError || sectionFieldErrors.count > 0 || !!sectionNoFields || !!sectionKeyError;

  const settingsData = settings.data && settings.data.Settings ? settings.data.Settings : null;
  const mediaDefaults = settingsData ? settingsData.Media : null;
  const listenState = listeningStatus({
    endpoints: endpoints.data,
    chosen: listen.Endpoint,
    defaultName: settingsData && settingsData.Inference ? settingsData.Inference.DefaultListening : null,
    defaultKnown: !!settingsData
  });
  const listenEndpoint = listenState.endpoint;
  const falling = ['none', 'noDefault', 'unhealthy', 'allUnhealthy'].includes(listenState.state);
  const flacRejected = !listenEndpoint || listenEndpoint.Provider === 'OpenAiCompatible';

  const requestBody = useMemo(
    () => buildAudioRequest({ media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields, listen, rendition, measure, sections, sectionSummarize, sectionExtractEnable, sectionFields }),
    [media, context, transcriptionModel, textModel, transcribe, summarize, extractEnable, mode, fields, listen, rendition, measure, sections, sectionSummarize, sectionExtractEnable, sectionFields]
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
    listen,
    rendition,
    transcribe,
    measure,
    summarize,
    extractEnable,
    mode,
    fields: fields.map(toFieldSpec),
    sections,
    sectionSummarize,
    sectionExtractEnable,
    sectionFields: sectionFields.map(toFieldSpec)
  });
  const loadPreset = (c) => {
    setContext((c.context || []).map((r) => createContextRow(r.Label, r.Text)));
    setTranscriptionModel(c.transcriptionModel || EMPTY_MODEL);
    setTextModel(c.textModel || EMPTY_MODEL);
    setListen({ ...DEFAULT_LISTEN, ...(c.listen || {}) });
    setRendition({ ...EMPTY_RENDITION, ...(c.rendition || {}) });
    setTranscribe({ ...DEFAULT_TRANSCRIBE, ...(c.transcribe || {}) });
    setMeasure(c.measure !== false);
    setSummarize({ ...DEFAULT_SUMMARIZE, ...(c.summarize || {}) });
    setExtractEnable(!!c.extractEnable);
    setMode(c.mode || 'Combined');
    setFields((c.fields || []).map(fromFieldSpec));
    setSections({ ...DEFAULT_SECTIONS, ...(c.sections || {}) });
    setSectionSummarize({ ...DEFAULT_SUMMARIZE, MaxWords: 60, ...(c.sectionSummarize || {}) });
    setSectionExtractEnable(!!c.sectionExtractEnable);
    setSectionFields((c.sectionFields || []).map(fromFieldSpec));
    setShowErrors(false);
  };

  const listenHelp = () => {
    const name = listenEndpoint ? listenEndpoint.Name : listen.Endpoint;
    switch (listenState.state) {
      case 'ok':
        return <p className="help-text">{t('analyzeAudio.listen.status.ok', { name, model: listen.Name.trim() || listenEndpoint.Model || t('model.namePlaceholder') })}</p>;
      case 'unchecked':
        return <p className="help-text">{t('analyzeAudio.listen.status.unchecked', { name })}</p>;
      case 'unknown':
        return <p className="help-text">{t('analyzeAudio.listen.status.unknown')}</p>;
      case 'loading':
        return null;
      default:
        return (
          <div className="banner banner-warning" role="status" data-testid="listen-fallback">
            <Icon name="alert" />
            <div className="banner-body">
              <div className="banner-title">{t('analyzeAudio.listen.fallbackTitle')}</div>
              <div>{t('analyzeAudio.listen.status.' + listenState.state, { name })}</div>
              <div className="banner-hint">{t('analyzeAudio.listen.firstStartHint')}</div>
              {!transcribeOn ? <div className="banner-hint">{t('analyzeAudio.listen.noTranscriptHint')}</div> : null}
            </div>
          </div>
        );
    }
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
          <section className="panel" data-testid="listen-panel">
            <h2 className="panel-title">{t('analyzeAudio.sections.listen')}</h2>
            <label className="toggle-row">
              <input type="checkbox" checked={listen.Enable} onChange={(e) => setListen({ ...listen, Enable: e.target.checked })} disabled={disabled} />
              <span>{t('analyzeAudio.listen.enable')}</span>
            </label>
            <p className="help-text">{t('analyzeAudio.listen.help')}</p>
            {listen.Enable ? (
              <>
                {endpoints.error ? <ErrorBanner error={endpoints.error} onRetry={endpoints.reload} /> : null}
                <ModelPicker
                  endpoints={endpoints.data}
                  value={{ Endpoint: listen.Endpoint, Name: listen.Name }}
                  onChange={(m) => setListen({ ...listen, Endpoint: m.Endpoint, Name: m.Name })}
                  labelKey="model.listeningEndpoint"
                  filter={(e) => !!e.SupportsAudioInput}
                  showHealth
                  disabled={disabled}
                />
                <div className="listen-status">
                  {listenEndpoint ? <EndpointHealthBadge endpoint={listenEndpoint} /> : null}
                  {listenHelp()}
                </div>
                <div className="form-row form-row-wrap">
                  <NumberField label={t('analyzeAudio.listen.sampleClips')} value={listen.SampleClips} onChange={(v) => setListen({ ...listen, SampleClips: v })} min={1} max={32} disabled={disabled} />
                  <NumberField label={t('analyzeAudio.listen.clipSeconds')} value={listen.ClipSeconds} onChange={(v) => setListen({ ...listen, ClipSeconds: v })} min={5} max={300} disabled={disabled} />
                </div>
                <p className="help-text">{t('analyzeAudio.listen.excerptHelp')}</p>
                <AudioQualityOptions value={rendition} onChange={setRendition} disabled={disabled} defaults={mediaDefaults} flacRejected={flacRejected} />
              </>
            ) : (
              <p className="help-text">{t('analyzeAudio.listen.offHelp')}</p>
            )}
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.model')}</h2>
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
            ) : listen.Enable ? (
              <p className="help-text">{t('analyzeAudio.transcribeOffListening')}</p>
            ) : (
              <p className="help-text text-warning">{t('analyzeAudio.transcribeRequired')}</p>
            )}
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyzeAudio.sections.measure')}</h2>
            <label className="toggle-row">
              <input type="checkbox" checked={measure} onChange={(e) => setMeasure(e.target.checked)} disabled={disabled} />
              <span>{t('analyzeAudio.measure.enable')}</span>
            </label>
            <p className="help-text">{t('analyzeAudio.measure.help')}</p>
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.summary')}</h2>
            <p className="help-text">{listenOn ? t('analyzeAudio.summaryHelpListen') : t('analyzeAudio.summaryHelp')}</p>
            <SummarizeOptions value={summarize} onChange={setSummarize} disabled={disabled || !evidence} keyError={showErrors ? base.summaryKey : null} />
          </section>
          <section className="panel">
            <h2 className="panel-title">{t('analyze.sections.fields')}</h2>
            <div className="option-group">
              <label className="toggle-row">
                <input type="checkbox" checked={extractEnable} onChange={(e) => setExtractEnable(e.target.checked)} disabled={disabled || !evidence} />
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
            {!evidence ? <p className="help-text text-warning">{t('analyzeAudio.transcribeRequired')}</p> : null}
            {extractEnable ? (
              <>
                <p className="help-text">{t('enums.extractModeHelp.' + mode)}</p>
                <FieldBuilder fields={fields} onChange={setFields} errors={showErrors ? base.fields.byUid : {}} disabled={disabled || !evidence} />
                {showErrors && base.functions ? <div className="field-error">{t(base.functions)}</div> : null}
              </>
            ) : null}
          </section>
          <section className="panel" data-testid="sections-panel">
            <h2 className="panel-title">{t('analyzeAudio.sections.sections')}</h2>
            <label className="toggle-row">
              <input type="checkbox" checked={sections.Enable} onChange={(e) => setSections({ ...sections, Enable: e.target.checked })} disabled={disabled} />
              <span>{t('analyzeAudio.sectionsPanel.enable')}</span>
            </label>
            <p className="help-text">{t('analyzeAudio.sectionsPanel.help')}</p>
            {sections.Enable ? (
              <>
                <div className="form-row form-row-wrap">
                  <NumberField label={t('analyzeAudio.sectionsPanel.silenceDb')} value={sections.SilenceDb} onChange={(v) => setSections({ ...sections, SilenceDb: v })} min={-90} max={-10} help={t('analyzeAudio.sectionsPanel.silenceDbHelp')} disabled={disabled} />
                  <NumberField label={t('analyzeAudio.sectionsPanel.minSilenceMs')} value={sections.MinSilenceMs} onChange={(v) => setSections({ ...sections, MinSilenceMs: v })} min={100} max={30000} step={100} disabled={disabled} />
                  <NumberField label={t('analyzeAudio.sectionsPanel.minSectionMs')} value={sections.MinSectionMs} onChange={(v) => setSections({ ...sections, MinSectionMs: v })} min={0} max={600000} step={1000} disabled={disabled} error={showErrors ? sectionRangeError : null} />
                  <NumberField label={t('analyzeAudio.sectionsPanel.maxSectionMs')} value={sections.MaxSectionMs} onChange={(v) => setSections({ ...sections, MaxSectionMs: v })} min={10000} max={3600000} step={1000} disabled={disabled} />
                  <NumberField label={t('analyzeAudio.sectionsPanel.maxSections')} value={sections.MaxSections} onChange={(v) => setSections({ ...sections, MaxSections: v })} min={1} max={1000} help={t('analyzeAudio.sectionsPanel.maxSectionsHelp')} disabled={disabled} />
                </div>
                <div className="section-label">{t('analyzeAudio.sectionsPanel.summary')}</div>
                <SummarizeOptions value={sectionSummarize} onChange={setSectionSummarize} disabled={disabled || !evidence} keyError={showErrors ? sectionKeyError : null} />
                <label className="toggle-row">
                  <input type="checkbox" checked={sectionExtractEnable} onChange={(e) => setSectionExtractEnable(e.target.checked)} disabled={disabled || !evidence} />
                  <span>{t('analyzeAudio.sectionsPanel.extract')}</span>
                </label>
                {sectionExtractEnable ? <FieldBuilder fields={sectionFields} onChange={setSectionFields} errors={showErrors ? sectionFieldErrors.byUid : {}} disabled={disabled || !evidence} /> : null}
                {showErrors && sectionNoFields ? <div className="field-error">{t(sectionNoFields)}</div> : null}
                <p className="help-text">{t('analyzeAudio.sectionsPanel.reduceHelp')}</p>
              </>
            ) : null}
          </section>
          <div className="form-actions sticky-actions">
            {showErrors && hasErrors ? (
              <span className="field-error" role="alert">
                {noFunction ? t('analyzeAudio.errors.noFunction') : t('analyze.errors.fixFields')}
              </span>
            ) : null}
            {listenOn && falling && !showErrors ? (
              <span className="text-warning listen-fallback-inline">
                <Icon name="alert" />
                {t('analyzeAudio.listen.willFallBack')}
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
