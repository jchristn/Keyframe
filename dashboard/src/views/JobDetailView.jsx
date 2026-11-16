import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatBytes, formatDateTime, formatDuration, formatNumber, formatTimestamp } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import StatusBadge from '../components/StatusBadge';
import CopyableId from '../components/CopyableId';
import ConfirmModal from '../components/ConfirmModal';
import ProgressBar from '../components/ProgressBar';
import Tabs from '../components/Tabs';
import JsonViewer, { JsonViewerModal } from '../components/JsonViewer';
import Icon from '../components/Icon';
import SceneTimeline, { TranscriptViewer } from '../components/SceneTimeline';
import TelemetryWaterfall, { ModelCallTable, TelemetryTotals } from '../components/TelemetryWaterfall';
import { FieldsTable, PropertiesGrid, Problems, ResultsList } from '../components/AnalysisParts';
import { ErrorBanner, LoadingState, Notice } from '../components/States';
import useInterval from '../hooks/useInterval';
import { downloadJson } from '../utils/media';
import { describeError } from '../utils/errors';
import { isActiveJob } from './JobsView';

/** Inspect one job: live progress, failure detail, scenes, transcript, telemetry, JSON. */
function JobDetailView() {
  const { id } = useParams();
  const { t, locale } = useLocale();
  const navigate = useNavigate();
  const { apiClient } = useAuth();
  const { notify } = useToast();
  const [job, setJob] = useState(null);
  const [jobError, setJobError] = useState(null);
  const [result, setResult] = useState(null);
  const [resultError, setResultError] = useState(null);
  const [resultLoading, setResultLoading] = useState(false);
  const [tab, setTab] = useState('overview');
  const [confirm, setConfirm] = useState(null);
  const [confirmError, setConfirmError] = useState(null);
  const [showJobJson, setShowJobJson] = useState(false);

  const loadJob = useCallback(async () => {
    try {
      const j = await apiClient.getJob(id);
      setJob(j);
      setJobError(null);
      return j;
    } catch (err) {
      setJobError(err);
      return null;
    }
  }, [apiClient, id]);

  const loadResult = useCallback(async () => {
    setResultLoading(true);
    setResultError(null);
    try {
      setResult(await apiClient.getJobResult(id));
    } catch (err) {
      setResultError(err);
    } finally {
      setResultLoading(false);
    }
  }, [apiClient, id]);

  useEffect(() => {
    setJob(null);
    setResult(null);
    loadJob();
  }, [loadJob]);

  const succeeded = job && job.Status === 'Succeeded';
  useEffect(() => {
    if (succeeded && !result && !resultLoading && !resultError) loadResult();
  }, [succeeded, result, resultLoading, resultError, loadResult]);

  useInterval(loadJob, job && isActiveJob(job) ? 1500 : null);

  const doCancel = async () => {
    try {
      setJob(await apiClient.cancelJob(id));
      notify('success', 'jobs.toast.canceled', { id });
      setConfirm(null);
    } catch (err) {
      setConfirmError(describeError(t, err));
    }
  };
  const doDelete = async () => {
    try {
      await apiClient.deleteJob(id);
      notify('success', 'jobs.toast.deleted', { count: 1 });
      navigate('/dashboard/jobs');
    } catch (err) {
      setConfirmError(describeError(t, err));
    }
  };

  if (!job && jobError) {
    return (
      <div className="view">
        <PageHeader title={t('jobDetail.title')} summary={id} />
        <ErrorBanner error={jobError} onRetry={loadJob} titleKey={jobError.status === 404 ? 'jobDetail.notFound' : null} />
        <Link to="/dashboard/jobs" className="button button-secondary">
          <Icon name="chevronLeft" />
          {t('jobDetail.back')}
        </Link>
      </div>
    );
  }
  if (!job) return <LoadingState />;

  const active = isActiveJob(job);
  const scenes = (result && result.Scenes) || [];
  const transcriptCount = result && result.Transcript ? (result.Transcript.Segments || []).length : 0;
  const tabs = [
    { id: 'overview', label: t('jobDetail.tabs.overview') },
    { id: 'scenes', label: t('jobDetail.tabs.scenes'), badge: result ? formatNumber(scenes.length, locale) : null },
    { id: 'transcript', label: t('jobDetail.tabs.transcript'), badge: result && result.Transcript ? formatNumber(transcriptCount, locale) : null },
    { id: 'telemetry', label: t('jobDetail.tabs.telemetry') },
    { id: 'raw', label: t('jobDetail.tabs.raw') }
  ];

  const meta = [
    ['jobDetail.meta.kind', t('enums.mediaKind.' + job.MediaKind, { defaultValue: job.MediaKind })],
    ['jobDetail.meta.principal', job.PrincipalName || t('common.none')],
    ['jobDetail.meta.source', job.SourceUrl ? <span className="mono break-all">{job.SourceUrl}</span> : t('jobs.upload')],
    ['jobDetail.meta.size', job.SizeBytes ? formatBytes(job.SizeBytes, locale) : t('common.notAvailable')],
    ['jobDetail.meta.duration', job.DurationMs ? formatDuration(job.DurationMs, locale) + ' (' + formatTimestamp(job.DurationMs, locale) + ')' : t('common.notAvailable')],
    ['jobDetail.meta.created', formatDateTime(job.CreatedUtc, locale)],
    ['jobDetail.meta.started', job.StartedUtc ? formatDateTime(job.StartedUtc, locale) : t('common.notYet')],
    ['jobDetail.meta.completed', job.CompletedUtc ? formatDateTime(job.CompletedUtc, locale) : t('common.notYet')]
  ];

  return (
    <div className="view">
      <PageHeader
        title={t('jobDetail.title')}
        summary={t('jobDetail.summary')}
        actions={
          <>
            <Link to="/dashboard/jobs" className="button button-secondary">
              <Icon name="chevronLeft" />
              {t('jobDetail.back')}
            </Link>
            <button type="button" className="button button-secondary" onClick={() => setShowJobJson(true)}>
              <Icon name="code" />
              {t('jobDetail.jobJson')}
            </button>
            {result ? (
              <button type="button" className="button button-secondary" onClick={() => downloadJson(result, job.Id + '-result.json')}>
                <Icon name="download" />
                {t('jobDetail.download')}
              </button>
            ) : null}
            {active ? (
              <button type="button" className="button button-danger" onClick={() => { setConfirmError(null); setConfirm('cancel'); }}>
                <Icon name="stop" />
                {t('jobs.cancel')}
              </button>
            ) : (
              <button type="button" className="button button-danger" onClick={() => { setConfirmError(null); setConfirm('delete'); }}>
                <Icon name="trash" />
                {t('common.delete')}
              </button>
            )}
          </>
        }
      >
        <div className="header-meta">
          <CopyableId value={job.Id} />
          <StatusBadge kind="job" value={job.Status} />
        </div>
      </PageHeader>

      {active ? (
        <section className="panel" aria-live="polite">
          <h2 className="panel-title">{t('jobDetail.progress')}</h2>
          <ProgressBar percent={job.Progress ? job.Progress.Percent : 0} label={job.Progress && job.Progress.Stage ? t('enums.progressStage.' + job.Progress.Stage, { defaultValue: job.Progress.Stage }) : null} />
          {job.Progress && job.Progress.Total ? (
            <p className="muted">{t('jobDetail.progressCount', { completed: formatNumber(job.Progress.Completed, locale), total: formatNumber(job.Progress.Total, locale) })}</p>
          ) : null}
          <p className="help-text">{t('jobDetail.liveNote')}</p>
        </section>
      ) : null}
      {job.Status === 'Failed' ? (
        <Notice tone="danger" icon="alert" title={t('jobDetail.failedTitle')}>
          <div className="mono break-all">{job.Error || t('jobDetail.noErrorDetail')}</div>
          <div className="banner-hint">{t('jobDetail.failedHint')}</div>
        </Notice>
      ) : null}
      {job.Status === 'Interrupted' ? (
        <Notice tone="warning" icon="alert" title={t('jobDetail.interruptedTitle')}>
          <div>{t('jobDetail.interruptedMessage')}</div>
          {job.Error ? <div className="mono">{job.Error}</div> : null}
        </Notice>
      ) : null}
      {job.Status === 'Canceled' ? <Notice title={t('jobDetail.canceledTitle')}>{t('jobDetail.canceledMessage')}</Notice> : null}
      {jobError ? <ErrorBanner error={jobError} onRetry={loadJob} /> : null}

      <section className="panel">
        <h2 className="panel-title">{t('jobDetail.details')}</h2>
        <dl className="kv-grid">
          {meta.map(([k, v]) => (
            <div key={k}>
              <dt>{t(k)}</dt>
              <dd>{v}</dd>
            </div>
          ))}
        </dl>
      </section>

      {succeeded ? (
        <section className="panel">
          {resultError ? <ErrorBanner error={resultError} onRetry={loadResult} /> : null}
          {resultLoading ? <LoadingState label={t('jobDetail.loadingResult')} /> : null}
          {result ? (
            <>
              <Problems warnings={result.Warnings} errors={result.Errors} />
              <Tabs tabs={tabs} active={tab} onChange={setTab} label={t('jobDetail.resultTabs')}>
                {tab === 'overview' ? (
                  <div className="stack">
                    <div className="section-label">{t('jobDetail.wholeResults')}</div>
                    <ResultsList results={result.Results} />
                    {result.Fields && result.Fields.length ? (
                      <>
                        <div className="section-label">{t('results.tabs.fields')}</div>
                        <FieldsTable fields={result.Fields} />
                      </>
                    ) : null}
                    <div className="section-label">{t('results.tabs.properties')}</div>
                    <PropertiesGrid properties={result.Properties} />
                  </div>
                ) : null}
                {tab === 'scenes' ? <SceneTimeline scenes={scenes} durationMs={result.Properties && result.Properties.DurationMs} /> : null}
                {tab === 'transcript' ? <TranscriptViewer transcript={result.Transcript} /> : null}
                {tab === 'telemetry' ? (
                  <>
                    <TelemetryTotals telemetry={result.Telemetry} />
                    <TelemetryWaterfall telemetry={result.Telemetry} />
                    <ModelCallTable telemetry={result.Telemetry} tableId="jobModelCalls" />
                  </>
                ) : null}
                {tab === 'raw' ? <JsonViewer value={result} maxHeight="65vh" filename={job.Id + '-result.json'} /> : null}
              </Tabs>
            </>
          ) : null}
        </section>
      ) : null}

      <JsonViewerModal open={showJobJson} onClose={() => setShowJobJson(false)} title={t('jobs.jsonTitle')} subtitle={job.Id} value={job} filename={job.Id + '.json'} />
      <ConfirmModal
        open={!!confirm}
        danger
        title={confirm === 'cancel' ? t('jobs.cancelTitle') : t('jobs.deleteTitle', { count: 1 })}
        message={confirm === 'cancel' ? t('jobs.cancelMessage', { id: job.Id }) : t('jobs.deleteMessage', { count: 1, id: job.Id })}
        confirmLabel={confirm === 'cancel' ? t('jobs.cancel') : t('common.delete')}
        error={confirmError}
        onCancel={() => setConfirm(null)}
        onConfirm={confirm === 'cancel' ? doCancel : doDelete}
      />
    </div>
  );
}

export default JobDetailView;
