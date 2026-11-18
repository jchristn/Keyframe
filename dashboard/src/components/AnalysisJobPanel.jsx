import { useCallback, useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatDuration, formatNumber } from '../i18n/formatters';
import ProgressBar from './ProgressBar';
import StatusBadge from './StatusBadge';
import CopyableId from './CopyableId';
import Tabs from './Tabs';
import JsonViewer from './JsonViewer';
import Icon from './Icon';
import { ErrorBanner, LoadingState, Notice } from './States';
import { FieldsTable, ListeningCard, MeasurementsGrid, Problems, PropertiesGrid, ResultsList } from './AnalysisParts';
import SectionTimeline from './SectionTimeline';
import SceneTimeline, { TranscriptViewer } from './SceneTimeline';
import { SegmentSourceProvider } from './SegmentDownloads';
import TelemetryWaterfall, { ModelCallTable, TelemetryTotals } from './TelemetryWaterfall';
import useInterval from '../hooks/useInterval';
import { describeError } from '../utils/errors';
import { isActiveJob } from '../views/JobsView';

const POLL_MS = 1500;

/**
 * Job state for the analyze pages: submit a video or audio job, poll it in place, load its result, cancel it.
 * Image analysis is synchronous and does not use this.
 */
export function useAnalysisJob() {
  const { t } = useLocale();
  const { apiClient } = useAuth();
  const { notify } = useToast();
  const [job, setJob] = useState(null);
  const [result, setResult] = useState(null);
  const [resultError, setResultError] = useState(null);
  const [canceling, setCanceling] = useState(false);

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

  /** Start tracking a submit response: a queued JobResponse, or a full result when the server waited. */
  const track = useCallback(
    (data, mediaKind) => {
      setResult(null);
      setResultError(null);
      if (data && data.Telemetry) {
        setJob({ Id: data.RequestId || '', Status: data.Success ? 'Succeeded' : 'Failed', MediaKind: mediaKind });
        setResult(data);
        return;
      }
      setJob(data);
      if (data && data.Status === 'Succeeded') loadResult(data.Id);
    },
    [loadResult]
  );

  const reset = useCallback(() => {
    setJob(null);
    setResult(null);
    setResultError(null);
  }, []);

  const cancel = useCallback(async () => {
    if (!job) return;
    setCanceling(true);
    try {
      setJob(await apiClient.cancelJob(job.Id));
      notify('success', 'jobs.toast.canceled', { id: job.Id });
    } catch (err) {
      notify('error', 'analysisJob.cancelFailed', { message: describeError(t, err) });
    } finally {
      setCanceling(false);
    }
  }, [apiClient, job, notify, t]);

  return { job, result, resultError, canceling, track, reset, cancel, loadResult, active: !!job && isActiveJob(job) };
}

/** Live job card shared by the video and audio pages: status, progress, cancel, and the finished result in tabs. */
function AnalysisJobPanel({ job, result, resultError, onCancel, onRetryResult, canceling, file = null }) {
  const { t, locale } = useLocale();
  const isVideo = job.MediaKind === 'Video';
  const [tab, setTab] = useState(isVideo ? 'scenes' : 'transcript');
  useEffect(() => {
    setTab(isVideo ? 'scenes' : 'transcript');
  }, [job.Id, isVideo]);

  const active = isActiveJob(job);
  const scenes = result && result.Scenes ? result.Scenes : [];
  const segments = result && result.Transcript ? (result.Transcript.Segments || []).length : 0;
  const invalid = result ? (result.Fields || []).filter((f) => !f.Valid).length : 0;
  const sections = result && Array.isArray(result.Sections) ? result.Sections : null;
  const tabs = [
    isVideo ? { id: 'scenes', label: t('jobDetail.tabs.scenes'), badge: result ? formatNumber(scenes.length, locale) : null } : null,
    { id: 'transcript', label: t('jobDetail.tabs.transcript'), badge: result && result.Transcript ? formatNumber(segments, locale) : null },
    !isVideo ? { id: 'sections', label: t('jobDetail.tabs.sections'), badge: sections ? formatNumber(sections.length, locale) : null } : null,
    !isVideo ? { id: 'measurements', label: t('jobDetail.tabs.measurements') } : null,
    { id: 'results', label: t('results.tabs.results') },
    { id: 'fields', label: t('results.tabs.fields'), badge: invalid || null },
    { id: 'telemetry', label: t('results.tabs.telemetry') },
    { id: 'properties', label: t('results.tabs.properties') },
    { id: 'raw', label: t('results.tabs.raw') }
  ].filter(Boolean);

  return (
    <section className="panel analyze-result" aria-live="polite" aria-labelledby="analysis-job-title">
      <div className="panel-header">
        <h2 id="analysis-job-title" className="panel-title">
          {t('analysisJob.title')}
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
          {t('analysisJob.openJob')}
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
          <p className="help-text">{t('analysisJob.polling')}</p>
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
          <ListeningCard listening={result.Listening} />
          <Tabs label={t('results.title')} active={tab} onChange={setTab} tabs={tabs}>
            <SegmentSourceProvider job={job} knownFile={file}>
              {tab === 'scenes' ? <SceneTimeline scenes={scenes} durationMs={result.Properties && result.Properties.DurationMs} /> : null}
              {tab === 'transcript' ? <TranscriptViewer transcript={result.Transcript} /> : null}
              {tab === 'sections' ? <SectionTimeline sections={sections} durationMs={result.Properties && result.Properties.DurationMs} measurements={result.Measurements} listening={result.Listening} /> : null}
            </SegmentSourceProvider>
            {tab === 'measurements' ? <MeasurementsGrid measurements={result.Measurements} failed={!!(result.Errors && result.Errors.length)} /> : null}
            {tab === 'results' ? <ResultsList results={result.Results} /> : null}
            {tab === 'fields' ? <FieldsTable fields={result.Fields} /> : null}
            {tab === 'telemetry' ? (
              <>
                <TelemetryTotals telemetry={result.Telemetry} />
                <TelemetryWaterfall telemetry={result.Telemetry} />
                <ModelCallTable telemetry={result.Telemetry} tableId="analysisJobModelCalls" />
              </>
            ) : null}
            {tab === 'properties' ? <PropertiesGrid properties={result.Properties} /> : null}
            {tab === 'raw' ? <JsonViewer value={result} maxHeight="60vh" filename={(job.Id || 'analysis') + '-result.json'} /> : null}
          </Tabs>
        </>
      ) : null}
    </section>
  );
}

export default AnalysisJobPanel;
