import { useId, useMemo, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import PageHeader from '../components/PageHeader';
import FieldBuilder from '../components/FieldBuilder';
import ContextEditor, { createContextRow } from '../components/ContextEditor';
import PresetBar from '../components/PresetBar';
import Icon from '../components/Icon';
import { ErrorBanner } from '../components/States';
import { MediaInput, ModelPicker, SnippetPanel, SummarizeOptions, toSummarizeSpec } from '../components/AnalysisParts';
import useRemote from '../hooks/useRemote';
import { createField, fromFieldSpec, toContextItems, toFieldSpec, validateFields } from '../utils/fields';
import { STORAGE_KEYS } from '../utils/storage';
import { DEFAULT_SUMMARIZE, buildMediaSource, toModelSelection, validateAnalysisForm } from './AnalyzeImageView';

const LANGUAGE_PATTERN = /^[A-Za-z]{2,3}(-[A-Za-z0-9]{2,8})?$/;

const DEFAULT_SCENES = { Enable: true, Threshold: '0.30', MinSceneMs: '2000', MaxSceneMs: '60000', MaxScenes: '200', FramesPerScene: '3' };
const DEFAULT_TRANSCRIBE = { Enable: true, Endpoint: '', Model: '', Language: '', Prompt: '' };

function clampNumber(value, min, max, fallback, integer = true) {
  const n = integer ? parseInt(value, 10) : parseFloat(value);
  if (!Number.isFinite(n)) return fallback;
  return Math.min(max, Math.max(min, n));
}

function NumberInput({ label, value, onChange, min, max, step = 1, help = null, disabled, error = null }) {
  const id = useId();
  const { t } = useLocale();
  return (
    <div className="form-field form-field-sm">
      <label htmlFor={id} className="field-label">
        {label}
      </label>
      <input id={id} type="number" min={min} max={max} step={step} value={value} onChange={(e) => onChange(e.target.value)} disabled={disabled} aria-invalid={!!error || undefined} />
      {help ? <p className="help-text">{help}</p> : null}
      {error ? <div className="field-error">{t(error)}</div> : null}
    </div>
  );
}

/** Video analysis: same builder as images plus scenes and transcription; submits a job. */
function AnalyzeVideoView() {
  const { t } = useLocale();
  const navigate = useNavigate();
  const { apiClient, serverUrl } = useAuth();
  const { notify } = useToast();
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient]);
  const idBase = useId();

  const [media, setMedia] = useState({ mode: 'url', file: null, url: '' });
  const [context, setContext] = useState([]);
  const [model, setModel] = useState({ Endpoint: '', Name: '' });
  const [textModel, setTextModel] = useState({ Endpoint: '', Name: '' });
  const [summarize, setSummarize] = useState({ ...DEFAULT_SUMMARIZE, MaxWords: 200 });
  const [extractEnable, setExtractEnable] = useState(true);
  const [fields, setFields] = useState(() => [createField('StringList', { Key: 'topics', Prompt: t('samples.video.topics'), MaxItems: '8' })]);
  const [scenes, setScenes] = useState(DEFAULT_SCENES);
  const [sceneSummarize, setSceneSummarize] = useState({ ...DEFAULT_SUMMARIZE, MaxWords: 40 });
  const [sceneExtractEnable, setSceneExtractEnable] = useState(false);
  const [sceneFields, setSceneFields] = useState(() => [createField('Boolean', { Key: 'showsText', Prompt: t('samples.video.showsText') })]);
  const [transcribe, setTranscribe] = useState(DEFAULT_TRANSCRIBE);
  const [sampleFrames, setSampleFrames] = useState('8');
  const [includeKeyframeData, setIncludeKeyframeData] = useState(true);
  const [includeProperties, setIncludeProperties] = useState(true);
  const [showErrors, setShowErrors] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState(null);

  const base = validateAnalysisForm({ media, summarize, extractEnable, fields, context });
  const sceneFieldErrors = scenes.Enable && sceneExtractEnable ? validateFields(sceneFields, sceneSummarize.Enable ? sceneSummarize.Key : null) : { byUid: {}, count: 0 };
  const sceneRangeError =
    scenes.Enable && clampNumber(scenes.MinSceneMs, 0, 600000, 2000) >= clampNumber(scenes.MaxSceneMs, 1000, 3600000, 60000) ? 'analyzeVideo.errors.sceneRange' : null;
  const languageError = transcribe.Enable && transcribe.Language && !LANGUAGE_PATTERN.test(transcribe.Language) ? 'analyzeVideo.errors.language' : null;
  const sceneNoFields = scenes.Enable && sceneExtractEnable && !sceneFields.length ? 'analyze.errors.noFields' : null;
  const noFunction = !summarize.Enable && !extractEnable && !scenes.Enable && !transcribe.Enable;
  const hasErrors = base.any || sceneFieldErrors.count > 0 || !!sceneRangeError || !!languageError || !!sceneNoFields || noFunction;

  const requestBody = useMemo(() => {
    const body = {
      Video: media.mode === 'url' ? { Url: media.url.trim() } : { Data: media.file ? '<base64 of ' + media.file.name + '>' : '<base64>' },
      SampleFrames: clampNumber(sampleFrames, 1, 64, 8),
      IncludeKeyframeData: includeKeyframeData,
      IncludeProperties: includeProperties,
      Functions: {
        Summarize: toSummarizeSpec(summarize),
        Extract: { Enable: extractEnable, Fields: extractEnable ? fields.map(toFieldSpec) : [] },
        Scenes: {
          Enable: scenes.Enable,
          Detection: {
            Threshold: clampNumber(scenes.Threshold, 0.01, 1, 0.3, false),
            MinSceneMs: clampNumber(scenes.MinSceneMs, 0, 600000, 2000),
            MaxSceneMs: clampNumber(scenes.MaxSceneMs, 1000, 3600000, 60000),
            MaxScenes: clampNumber(scenes.MaxScenes, 1, 2000, 200)
          },
          FramesPerScene: clampNumber(scenes.FramesPerScene, 1, 10, 3),
          Summarize: toSummarizeSpec(sceneSummarize),
          Extract: { Enable: scenes.Enable && sceneExtractEnable, Fields: scenes.Enable && sceneExtractEnable ? sceneFields.map(toFieldSpec) : [] }
        },
        Transcribe: { Enable: transcribe.Enable }
      }
    };
    if (transcribe.Enable) {
      if (transcribe.Endpoint) body.Functions.Transcribe.Endpoint = transcribe.Endpoint;
      if (transcribe.Model.trim()) body.Functions.Transcribe.Model = transcribe.Model.trim();
      if (transcribe.Language.trim()) body.Functions.Transcribe.Language = transcribe.Language.trim();
      if (transcribe.Prompt.trim()) body.Functions.Transcribe.Prompt = transcribe.Prompt.trim();
    }
    const ctx = toContextItems(context);
    if (ctx.length) body.Context = ctx;
    const m = toModelSelection(model);
    if (m) body.Model = m;
    const tm = toModelSelection(textModel);
    if (tm) body.TextModel = tm;
    return body;
  }, [media, sampleFrames, includeKeyframeData, includeProperties, summarize, extractEnable, fields, scenes, sceneSummarize, sceneExtractEnable, sceneFields, transcribe, context, model, textModel]);

  const submit = async () => {
    setShowErrors(true);
    if (hasErrors) return;
    setSubmitting(true);
    setError(null);
    try {
      const body = { ...requestBody, Video: await buildMediaSource(media) };
      const result = await apiClient.analyzeVideo(body);
      const job = result.data || {};
      const id = job.Id;
      notify('success', 'analyzeVideo.toast.submitted', { id });
      navigate('/dashboard/jobs/' + encodeURIComponent(id));
    } catch (err) {
      setError(err);
    } finally {
      setSubmitting(false);
    }
  };

  const presetConfig = () => ({
    context: context.map(({ Label, Text }) => ({ Label, Text })),
    model,
    textModel,
    summarize,
    extractEnable,
    fields: fields.map(toFieldSpec),
    scenes,
    sceneSummarize,
    sceneExtractEnable,
    sceneFields: sceneFields.map(toFieldSpec),
    transcribe,
    sampleFrames,
    includeKeyframeData,
    includeProperties
  });
  const loadPreset = (c) => {
    setContext((c.context || []).map((r) => createContextRow(r.Label, r.Text)));
    setModel(c.model || { Endpoint: '', Name: '' });
    setTextModel(c.textModel || { Endpoint: '', Name: '' });
    setSummarize({ ...DEFAULT_SUMMARIZE, ...(c.summarize || {}) });
    setExtractEnable(c.extractEnable !== false);
    setFields((c.fields || []).map(fromFieldSpec));
    setScenes({ ...DEFAULT_SCENES, ...(c.scenes || {}) });
    setSceneSummarize({ ...DEFAULT_SUMMARIZE, ...(c.sceneSummarize || {}) });
    setSceneExtractEnable(!!c.sceneExtractEnable);
    setSceneFields((c.sceneFields || []).map(fromFieldSpec));
    setTranscribe({ ...DEFAULT_TRANSCRIBE, ...(c.transcribe || {}) });
    setSampleFrames(c.sampleFrames || '8');
    setIncludeKeyframeData(c.includeKeyframeData !== false);
    setIncludeProperties(c.includeProperties !== false);
    setShowErrors(false);
  };

  const transcriptionEndpoints = (endpoints.data || []).filter((e) => e.Kind === 'Transcription' && e.Active);
  const disabled = submitting;

  return (
    <div className="view view-narrow">
      <PageHeader title={t('analyzeVideo.title')} summary={t('analyzeVideo.summary')} actions={<PresetBar storageKey={STORAGE_KEYS.videoPresets} current={presetConfig} onLoad={loadPreset} />} />
      <form
        className="analyze-form"
        onSubmit={(e) => {
          e.preventDefault();
          submit();
        }}
        noValidate
      >
        {error ? <ErrorBanner error={error} onRetry={submit} titleKey={error.status === 429 ? 'analyzeVideo.errors.queueFull' : null} /> : null}
        <section className="panel">
          <h2 className="panel-title">{t('analyze.sections.media')}</h2>
          <MediaInput kind="video" value={media} onChange={setMedia} error={showErrors ? base.media : null} disabled={disabled} />
        </section>
        <section className="panel">
          <h2 className="panel-title">{t('analyze.sections.model')}</h2>
          {endpoints.error ? <ErrorBanner error={endpoints.error} onRetry={endpoints.reload} /> : null}
          <ModelPicker endpoints={endpoints.data} value={model} onChange={setModel} labelKey="model.visionEndpoint" disabled={disabled} />
          <ModelPicker endpoints={endpoints.data} value={textModel} onChange={setTextModel} labelKey="model.textEndpoint" disabled={disabled} />
          <p className="help-text">{t('model.textHelp')}</p>
        </section>
        <section className="panel">
          <h2 className="panel-title">{t('analyze.sections.context')}</h2>
          <ContextEditor rows={context} onChange={setContext} errors={showErrors ? base.context : []} disabled={disabled} />
        </section>

        <section className="panel">
          <h2 className="panel-title">{t('analyzeVideo.sections.whole')}</h2>
          <p className="help-text">{t('analyzeVideo.wholeHelp')}</p>
          <SummarizeOptions value={summarize} onChange={setSummarize} disabled={disabled} keyError={showErrors ? base.summaryKey : null} />
          <label className="toggle-row">
            <input type="checkbox" checked={extractEnable} onChange={(e) => setExtractEnable(e.target.checked)} disabled={disabled} />
            <span>{t('extract.enable')}</span>
          </label>
          {extractEnable ? (
            <>
              <FieldBuilder fields={fields} onChange={setFields} errors={showErrors ? base.fields.byUid : {}} disabled={disabled} />
              {showErrors && base.functions ? <div className="field-error">{t(base.functions)}</div> : null}
            </>
          ) : null}
          {!scenes.Enable ? (
            <NumberInput label={t('analyzeVideo.sampleFrames')} value={sampleFrames} onChange={setSampleFrames} min={1} max={64} help={t('analyzeVideo.sampleFramesHelp')} disabled={disabled} />
          ) : null}
        </section>

        <section className="panel">
          <h2 className="panel-title">{t('analyzeVideo.sections.scenes')}</h2>
          <label className="toggle-row">
            <input type="checkbox" checked={scenes.Enable} onChange={(e) => setScenes({ ...scenes, Enable: e.target.checked })} disabled={disabled} />
            <span>{t('analyzeVideo.scenesEnable')}</span>
          </label>
          {scenes.Enable ? (
            <>
              <div className="form-row form-row-wrap">
                <NumberInput label={t('analyzeVideo.threshold')} value={scenes.Threshold} onChange={(v) => setScenes({ ...scenes, Threshold: v })} min={0.01} max={1} step={0.01} help={t('analyzeVideo.thresholdHelp')} disabled={disabled} />
                <NumberInput label={t('analyzeVideo.minSceneMs')} value={scenes.MinSceneMs} onChange={(v) => setScenes({ ...scenes, MinSceneMs: v })} min={0} max={600000} step={100} disabled={disabled} error={showErrors ? sceneRangeError : null} />
                <NumberInput label={t('analyzeVideo.maxSceneMs')} value={scenes.MaxSceneMs} onChange={(v) => setScenes({ ...scenes, MaxSceneMs: v })} min={1000} max={3600000} step={1000} disabled={disabled} />
                <NumberInput label={t('analyzeVideo.maxScenes')} value={scenes.MaxScenes} onChange={(v) => setScenes({ ...scenes, MaxScenes: v })} min={1} max={2000} help={t('analyzeVideo.maxScenesHelp')} disabled={disabled} />
                <NumberInput label={t('analyzeVideo.framesPerScene')} value={scenes.FramesPerScene} onChange={(v) => setScenes({ ...scenes, FramesPerScene: v })} min={1} max={10} disabled={disabled} />
              </div>
              <div className="section-label">{t('analyzeVideo.sceneSummary')}</div>
              <SummarizeOptions value={sceneSummarize} onChange={setSceneSummarize} disabled={disabled} />
              <label className="toggle-row">
                <input type="checkbox" checked={sceneExtractEnable} onChange={(e) => setSceneExtractEnable(e.target.checked)} disabled={disabled} />
                <span>{t('analyzeVideo.sceneExtract')}</span>
              </label>
              {sceneExtractEnable ? <FieldBuilder fields={sceneFields} onChange={setSceneFields} errors={showErrors ? sceneFieldErrors.byUid : {}} disabled={disabled} /> : null}
              {showErrors && sceneNoFields ? <div className="field-error">{t(sceneNoFields)}</div> : null}
              <label className="checkbox-row">
                <input type="checkbox" checked={includeKeyframeData} onChange={(e) => setIncludeKeyframeData(e.target.checked)} disabled={disabled} />
                <span>{t('analyzeVideo.includeKeyframeData')}</span>
              </label>
            </>
          ) : null}
        </section>

        <section className="panel">
          <h2 className="panel-title">{t('analyzeVideo.sections.transcribe')}</h2>
          <label className="toggle-row">
            <input type="checkbox" checked={transcribe.Enable} onChange={(e) => setTranscribe({ ...transcribe, Enable: e.target.checked })} disabled={disabled} />
            <span>{t('analyzeVideo.transcribeEnable')}</span>
          </label>
          {transcribe.Enable ? (
            <div className="form-row form-row-wrap">
              <div className="form-field">
                <label htmlFor={idBase + 'tep'} className="field-label">
                  {t('analyzeVideo.transcribeEndpoint')}
                </label>
                <select id={idBase + 'tep'} value={transcribe.Endpoint} onChange={(e) => setTranscribe({ ...transcribe, Endpoint: e.target.value })} disabled={disabled}>
                  <option value="">{t('model.useDefault')}</option>
                  {transcriptionEndpoints.map((e) => (
                    <option key={e.Name} value={e.Name}>
                      {e.Name}
                    </option>
                  ))}
                </select>
              </div>
              <div className="form-field">
                <label htmlFor={idBase + 'tmodel'} className="field-label">
                  {t('model.name')}
                </label>
                <input id={idBase + 'tmodel'} className="mono" value={transcribe.Model} onChange={(e) => setTranscribe({ ...transcribe, Model: e.target.value })} disabled={disabled} placeholder={t('model.namePlaceholder')} />
              </div>
              <div className="form-field form-field-sm">
                <label htmlFor={idBase + 'tlang'} className="field-label">
                  {t('analyzeVideo.language')}
                </label>
                <input id={idBase + 'tlang'} className="mono" value={transcribe.Language} onChange={(e) => setTranscribe({ ...transcribe, Language: e.target.value })} disabled={disabled} placeholder={t('analyzeVideo.languagePlaceholder')} maxLength={12} aria-invalid={(showErrors && !!languageError) || undefined} />
                {showErrors && languageError ? <div className="field-error">{t(languageError)}</div> : null}
              </div>
              <div className="form-field field-grid-wide">
                <label htmlFor={idBase + 'tprompt'} className="field-label">
                  {t('analyzeVideo.transcribePrompt')}
                </label>
                <textarea id={idBase + 'tprompt'} rows={2} value={transcribe.Prompt} onChange={(e) => setTranscribe({ ...transcribe, Prompt: e.target.value })} disabled={disabled} maxLength={4000} placeholder={t('analyzeVideo.transcribePromptPlaceholder')} />
              </div>
            </div>
          ) : null}
          <label className="checkbox-row">
            <input type="checkbox" checked={includeProperties} onChange={(e) => setIncludeProperties(e.target.checked)} disabled={disabled} />
            <span>{t('analyze.includeProperties')}</span>
          </label>
        </section>

        <div className="form-actions sticky-actions">
          {showErrors && hasErrors ? (
            <span className="field-error" role="alert">
              {noFunction ? t('analyzeVideo.errors.noFunction') : t('analyze.errors.fixFields')}
            </span>
          ) : null}
          <button type="submit" className="button button-primary" disabled={submitting}>
            <Icon name="play" />
            {submitting ? t('analyzeVideo.submitting') : t('analyzeVideo.submit')}
          </button>
        </div>
        <details className="panel details-panel">
          <summary>{t('snippets.title')}</summary>
          <SnippetPanel method="POST" url={serverUrl + '/v1.0/video'} body={requestBody} />
        </details>
      </form>
    </div>
  );
}

export default AnalyzeVideoView;
