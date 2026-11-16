import { useCallback, useId, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatBytes, formatCompact, formatDateTime, formatDecimal, formatDuration, formatNumber, formatPercent, formatRelativeTime } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import KpiCard from '../components/KpiCard';
import DataTable from '../components/DataTable';
import Pagination from '../components/Pagination';
import StatusBadge from '../components/StatusBadge';
import CopyableId from '../components/CopyableId';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import { JsonViewerModal } from '../components/JsonViewer';
import Icon from '../components/Icon';
import TimeSeriesChart, { ChartLegend } from '../components/charts/TimeSeriesChart';
import TelemetryWaterfall, { ModelCallTable } from '../components/TelemetryWaterfall';
import { EmptyState, ErrorBanner, LoadingState, Notice } from '../components/States';
import useRemote from '../hooks/useRemote';
import useInterval from '../hooks/useInterval';
import usePersistedState from '../hooks/usePersistedState';
import { STORAGE_KEYS } from '../utils/storage';
import { isoToLocalInput, localInputToIso } from '../utils/ranges';
import { describeError } from '../utils/errors';

export const ANALYTICS_RANGES = [
  { id: '1h', ms: 3600000 },
  { id: '24h', ms: 86400000 },
  { id: '7d', ms: 7 * 86400000 },
  { id: '30d', ms: 30 * 86400000 },
  { id: 'custom', ms: null }
];
export const MEDIA_KINDS = ['Image', 'Video', 'Audio'];
const BUCKET_MS = { minute: 60000, hour: 3600000, day: 86400000 };
const REFRESH_MS = 60000;

// Every StatBlock distribution, with how its values are formatted.
export const METRICS = [
  { key: 'AnalysisMs', type: 'duration' },
  { key: 'QueueWaitMs', type: 'duration' },
  { key: 'ModelCallMs', type: 'duration' },
  { key: 'TimeToFirstTokenMs', type: 'duration' },
  { key: 'GenerationMs', type: 'duration' },
  { key: 'TokensPerSecond', type: 'rate' },
  { key: 'TokensPerAnalysis', type: 'count' },
  { key: 'RealtimeFactor', type: 'ratio' },
  { key: 'MediaDurationMs', type: 'duration' },
  { key: 'MediaBytes', type: 'bytes' }
];
export const STATS = ['Avg', 'P50', 'P90', 'P95', 'P99'];
const STAT_COLORS = ['var(--chart-1)', 'var(--chart-2)', 'var(--chart-3)', 'var(--chart-4)', 'var(--chart-5)'];

/** Resolve the time window from URL state. */
export function analyticsWindow(rangeId, from, to, now = Date.now()) {
  const range = ANALYTICS_RANGES.find((r) => r.id === rangeId) || ANALYTICS_RANGES[1];
  if (range.id === 'custom' && from && to) return { fromUtc: from, toUtc: to };
  const end = now;
  return { fromUtc: new Date(end - (range.ms || 86400000)).toISOString(), toUtc: new Date(end).toISOString() };
}

function useMetricFormatter() {
  const { t, locale } = useLocale();
  return useCallback(
    (type, v, compact = false) => {
      if (v === null || v === undefined || Number.isNaN(Number(v))) return t('common.notAvailable');
      if (type === 'duration') return formatDuration(v, locale);
      if (type === 'bytes') return formatBytes(v, locale);
      if (type === 'rate') return t('units.tokensPerSecond', { value: formatDecimal(v, locale, 1) });
      if (type === 'ratio') return t('units.realtimeFactor', { value: formatDecimal(v, locale, 2) });
      if (type === 'percent') return formatPercent(v, locale, 1);
      return compact ? formatCompact(v, locale) : formatNumber(v, locale, { maximumFractionDigits: 1 });
    },
    [t, locale]
  );
}

function distValue(block, metric, stat) {
  const d = block && block[metric];
  return d && typeof d[stat] === 'number' ? d[stat] : null;
}

/** Percentile cells for one Distribution. */
function DistCells({ dist, type }) {
  const fmt = useMetricFormatter();
  const { locale } = useLocale();
  return (
    <>
      <td className="align-right mono">{dist ? formatNumber(dist.Count, locale) : ''}</td>
      {['Avg', 'P50', 'P90', 'P95', 'P99', 'Max'].map((s) => (
        <td key={s} className="align-right mono cell-nowrap">
          {dist ? fmt(type, dist[s]) : fmt(type, null)}
        </td>
      ))}
    </>
  );
}

/** Distribution table: one row per metric with count, average, and percentiles. */
function DistributionTable({ block }) {
  const { t } = useLocale();
  return (
    <div className="table-scroll">
      <table className="data-table">
        <thead>
          <tr>
            <th scope="col">{t('analytics.columns.metric')}</th>
            <th scope="col" className="align-right">{t('analytics.columns.samples')}</th>
            {['avg', 'p50', 'p90', 'p95', 'p99', 'max'].map((s) => (
              <th key={s} scope="col" className="align-right">
                {t('analytics.stats.' + s)}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {METRICS.map((m) => (
            <tr key={m.key}>
              <th scope="row" className="cell-label">
                {t('analytics.metrics.' + m.key)}
              </th>
              <DistCells dist={block && block[m.key]} type={m.type} />
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

/** Proportional meter for breakdown tables. */
function Meter({ value, max }) {
  const pct = max > 0 && value ? Math.max(2, Math.min(100, (value / max) * 100)) : 0;
  return (
    <span className="meter" aria-hidden="true">
      <span className="meter-fill" style={{ width: pct + '%' }} />
    </span>
  );
}

/** Detail modal for one analysis (95% of the viewport): metadata, per-call waterfall, call table, JSON, delete. */
function AnalysisDrawer({ id, onClose, onDeleted }) {
  const { t, locale } = useLocale();
  const { apiClient } = useAuth();
  const { notify } = useToast();
  const fmt = useMetricFormatter();
  const detail = useRemote(() => (id ? apiClient.getAnalysis(id) : Promise.resolve(null)), [apiClient, id]);
  const [showJson, setShowJson] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [confirmError, setConfirmError] = useState(null);
  const r = detail.data;

  const telemetry = useMemo(() => {
    if (!r) return null;
    return {
      StartUtc: r.StartedUtc,
      TotalMs: r.AnalysisMs,
      Stages: r.Stages || [],
      ModelCalls: (r.Calls || []).map((c) => ({ ...c, Attempts: c.Attempt }))
    };
  }, [r]);

  const doDelete = async () => {
    try {
      await apiClient.deleteAnalysis(id);
      notify('success', 'analytics.toast.deleted', { id });
      setConfirm(false);
      onDeleted();
    } catch (err) {
      setConfirmError(describeError(t, err));
    }
  };

  const model = (endpoint, name) => (endpoint ? endpoint + (name ? ' · ' + name : '') : null);
  const meta = r
    ? [
        ['kind', t('enums.mediaKind.' + r.MediaKind, { defaultValue: r.MediaKind })],
        ['principal', r.PrincipalName || t('common.none')],
        ['started', formatDateTime(r.StartedUtc, locale)],
        ['completed', formatDateTime(r.CompletedUtc, locale)],
        ['analysisTime', fmt('duration', r.AnalysisMs)],
        ['queueWait', fmt('duration', r.QueueWaitMs)],
        ['mediaDuration', r.MediaDurationMs ? fmt('duration', r.MediaDurationMs) : t('common.notAvailable')],
        ['mediaSize', r.MediaBytes ? fmt('bytes', r.MediaBytes) : t('common.notAvailable')],
        ['realtimeFactor', fmt('ratio', r.RealtimeFactor)],
        ['visionModel', model(r.VisionEndpoint, r.VisionModel)],
        ['textModel', model(r.TextEndpoint, r.TextModel)],
        ['transcriptionModel', model(r.TranscriptionEndpoint, r.TranscriptionModel)],
        ['modelCalls', t('analytics.detail.callsValue', { calls: formatNumber(r.ModelCalls || 0, locale), failed: formatNumber(r.FailedModelCalls || 0, locale) })],
        ['retries', t('analytics.detail.retriesValue', { retries: formatNumber(r.Retries || 0, locale), repairs: formatNumber(r.RepairAttempts || 0, locale) })],
        ['promptTokens', formatNumber(r.PromptTokens || 0, locale)],
        ['completionTokens', formatNumber(r.CompletionTokens || 0, locale)],
        ['cachedTokens', formatNumber(r.CachedTokens || 0, locale)],
        ['reasoningTokens', formatNumber(r.ReasoningTokens || 0, locale)],
        ['totalTokens', formatNumber(r.TotalTokens || 0, locale)],
        ['inputImages', t('analytics.detail.imagesValue', { count: formatNumber(r.InputImages || 0, locale), size: formatBytes(r.InputImageBytes || 0, locale) })],
        ['inputAudio', formatBytes(r.InputAudioBytes || 0, locale)],
        ['scenes', formatNumber(r.Scenes || 0, locale)],
        ['transcriptCharacters', formatNumber(r.TranscriptCharacters || 0, locale)],
        ['fields', t('analytics.detail.fieldsValue', { count: formatNumber(r.FieldCount || 0, locale), invalid: formatNumber(r.InvalidFields || 0, locale) })]
      ].filter(([, v]) => v !== null && v !== undefined)
    : [];

  return (
    <Modal
      open={!!id}
      onClose={onClose}
      size="full"
      title={t('analytics.detail.title')}
      subtitle={r ? t('analytics.detail.subtitle', { kind: t('enums.mediaKind.' + r.MediaKind, { defaultValue: r.MediaKind }), time: formatRelativeTime(r.StartedUtc, locale) }) : null}
      headerExtra={
        r ? (
          <div className="header-meta">
            <CopyableId value={r.Id} />
            <StatusBadge kind="custom" tone={r.Success ? 'success' : 'danger'} label={r.Success ? t('status.succeeded') : t('status.failed')} />
          </div>
        ) : null
      }
      footer={
        r ? (
          <>
            {r.JobId ? (
              <Link to={'/dashboard/jobs/' + encodeURIComponent(r.JobId)} className="button button-secondary">
                <Icon name="jobs" />
                {t('analytics.detail.openJob')}
              </Link>
            ) : null}
            <button type="button" className="button button-secondary" onClick={() => setShowJson(true)}>
              <Icon name="code" />
              {t('common.viewJson')}
            </button>
            <button type="button" className="button button-danger" onClick={() => { setConfirmError(null); setConfirm(true); }}>
              <Icon name="trash" />
              {t('common.delete')}
            </button>
          </>
        ) : null
      }
    >
      {detail.loading && !r ? <LoadingState /> : null}
      {detail.error ? <ErrorBanner error={detail.error} onRetry={detail.reload} titleKey={detail.error.status === 404 ? 'analytics.detail.notFound' : null} /> : null}
      {r ? (
        <div className="stack">
          {r.Error ? (
            <Notice tone="danger" icon="alert" title={t('analytics.detail.error')}>
              <div className="mono break-all">{r.Error}</div>
            </Notice>
          ) : null}
          <dl className="kv-grid">
            {meta.map(([k, v]) => (
              <div key={k}>
                <dt>{t('analytics.detail.fields.' + k)}</dt>
                <dd>{v}</dd>
              </div>
            ))}
          </dl>
          <div className="section-label">{t('analytics.detail.timeline')}</div>
          <p className="help-text">{t('analytics.detail.timelineHelp')}</p>
          <TelemetryWaterfall telemetry={telemetry} />
          <div className="section-label">{t('analytics.detail.calls')}</div>
          <ModelCallTable telemetry={telemetry} tableId="analyticsCalls" />
        </div>
      ) : null}
      <JsonViewerModal open={showJson} onClose={() => setShowJson(false)} title={t('analytics.detail.jsonTitle')} subtitle={id} value={r} filename={(id || 'analysis') + '.json'} />
      <ConfirmModal
        open={confirm}
        danger
        title={t('analytics.detail.deleteTitle')}
        message={t('analytics.detail.deleteMessage', { id })}
        confirmLabel={t('common.delete')}
        error={confirmError}
        onCancel={() => setConfirm(false)}
        onConfirm={doDelete}
      />
    </Modal>
  );
}

/** Performance and telemetry over time for image, video, and audio analysis. */
function AnalyticsView() {
  const { t, locale } = useLocale();
  const { apiClient, isAdmin } = useAuth();
  const fmt = useMetricFormatter();
  const ids = useId();
  const [search, setSearch] = useSearchParams();
  const [anchor, setAnchor] = useState(() => Date.now());
  const [metric, setMetric] = usePersistedState('keyframe.analytics.metric', 'AnalysisMs');
  const [hiddenStats, setHiddenStats] = useState(['Avg', 'P90']);
  const [pageSize, setPageSize] = usePersistedState(STORAGE_KEYS.pageSize('analyses'), 25);
  const [detailId, setDetailId] = useState(null);
  const [jsonRecord, setJsonRecord] = useState(null);
  const [bucketWindow, setBucketWindow] = useState(null);

  const f = {
    range: search.get('range') || '24h',
    from: search.get('from') || '',
    to: search.get('to') || '',
    mediaKind: search.get('mediaKind') || '',
    endpoint: search.get('endpoint') || '',
    model: search.get('model') || '',
    success: search.get('success') || ''
  };
  const page = Math.max(1, parseInt(search.get('page') || '1', 10) || 1);
  const [modelDraft, setModelDraft] = useState(f.model);

  const setFilter = (patch) => {
    const next = new URLSearchParams(search);
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    if (!('page' in patch)) next.delete('page');
    setSearch(next, { replace: true });
  };

  const win = useMemo(() => analyticsWindow(f.range, f.from, f.to, anchor), [f.range, f.from, f.to, anchor]);
  const summaryQuery = { ...win, mediaKind: f.mediaKind, endpoint: f.endpoint, model: f.model };
  const summaryKey = JSON.stringify(summaryQuery);
  const summary = useRemote(() => apiClient.getAnalyticsSummary(summaryQuery), [apiClient, summaryKey], { enabled: isAdmin });
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient], { enabled: isAdmin });

  const listWindow = bucketWindow || win;
  const listQuery = { fromUtc: listWindow.fromUtc, toUtc: listWindow.toUtc, mediaKind: f.mediaKind, success: f.success, pageNumber: page, pageSize };
  const listKey = JSON.stringify(listQuery);
  const list = useRemote(() => apiClient.listAnalyses(listQuery), [apiClient, listKey], { enabled: isAdmin });

  const refresh = () => {
    if (f.range !== 'custom') setAnchor(Date.now());
    else {
      summary.reload();
      list.reload();
    }
  };
  useInterval(() => f.range !== 'custom' && !detailId && setAnchor(Date.now()), isAdmin ? REFRESH_MS : null);

  const s = summary.data;
  const totals = (s && s.Totals) || null;
  const bucket = (s && s.Bucket) || 'hour';
  const buckets = useMemo(() => ((s && s.Buckets) || []).map((b) => ({ startUtc: b.StartUtc, stats: b.Stats || {} })), [s]);
  const metricDef = METRICS.find((m) => m.key === metric) || METRICS[0];

  const volumeSeries = [
    { key: 'ok', label: t('analytics.series.succeeded'), color: 'var(--chart-success)', values: buckets.map((b) => b.stats.Succeeded || 0) },
    { key: 'fail', label: t('analytics.series.failed'), color: 'var(--chart-failure)', values: buckets.map((b) => b.stats.Failed || 0) }
  ];
  const latencySeries = STATS.map((st, i) => ({
    key: st,
    label: t('analytics.stats.' + st.toLowerCase()),
    color: STAT_COLORS[i],
    hidden: hiddenStats.includes(st),
    values: buckets.map((b) => distValue(b.stats, metric, st))
  }));
  const tokenSeries = [
    { key: 'prompt', label: t('analytics.series.promptTokens'), color: 'var(--chart-1)', values: buckets.map((b) => b.stats.PromptTokens || 0) },
    { key: 'completion', label: t('analytics.series.completionTokens'), color: 'var(--chart-2)', values: buckets.map((b) => b.stats.CompletionTokens || 0) }
  ];
  const successSeries = [
    { key: 'rate', label: t('analytics.series.successRate'), color: 'var(--chart-success)', values: buckets.map((b) => (b.stats.Count ? b.stats.SuccessRate : null)) }
  ];

  const onVolumeClick = (b) => {
    const start = new Date(b.startUtc).getTime();
    setBucketWindow({ fromUtc: b.startUtc, toUtc: new Date(start + (BUCKET_MS[bucket] || 3600000)).toISOString() });
    setFilter({ page: '' });
  };

  if (!isAdmin) {
    return (
      <div className="view">
        <PageHeader title={t('analytics.title')} summary={t('analytics.summary')} />
        <EmptyState icon="lock" title={t('analytics.notAdminTitle')} message={t('analytics.notAdminMessage')} />
      </div>
    );
  }

  const unavailable = summary.error && (summary.error.status === 404 || summary.error.status === 405);
  const byModel = (s && s.ByModel) || [];
  const byStage = (s && s.ByStage) || [];
  const byPurpose = (s && s.ByPurpose) || [];
  const stageMax = byStage.reduce((m, r) => Math.max(m, distValue(r, 'DurationMs', 'P50') || 0), 0);
  const modelNames = [...new Set(byModel.map((m) => m.Model).filter(Boolean))];
  const hasData = totals && totals.Count > 0;

  const filterBar = (
    <section className="panel analytics-filters" aria-label={t('analytics.filters.label')}>
      <div className="filter-bar">
        <div className="form-field form-field-xs">
          <span className="field-label">{t('analytics.filters.range')}</span>
          <div className="segmented" role="group" aria-label={t('analytics.filters.range')}>
            {ANALYTICS_RANGES.map((r) => (
              <button
                key={r.id}
                type="button"
                className={'segment' + (f.range === r.id ? ' active' : '')}
                aria-pressed={f.range === r.id}
                onClick={() => {
                  setBucketWindow(null);
                  if (r.id === 'custom') {
                    const w = analyticsWindow(f.range, f.from, f.to, Date.now());
                    setFilter({ range: 'custom', from: w.fromUtc, to: w.toUtc });
                  } else {
                    setAnchor(Date.now());
                    setFilter({ range: r.id === '24h' ? '' : r.id, from: '', to: '' });
                  }
                }}
              >
                {t('analytics.ranges.' + r.id)}
              </button>
            ))}
          </div>
        </div>
        {f.range === 'custom' ? (
          <>
            <div className="form-field form-field-xs">
              <label htmlFor={ids + 'from'} className="field-label">
                {t('filters.from')}
              </label>
              <input id={ids + 'from'} type="datetime-local" value={isoToLocalInput(f.from)} onChange={(e) => setFilter({ from: localInputToIso(e.target.value) })} />
            </div>
            <div className="form-field form-field-xs">
              <label htmlFor={ids + 'to'} className="field-label">
                {t('filters.to')}
              </label>
              <input id={ids + 'to'} type="datetime-local" value={isoToLocalInput(f.to)} onChange={(e) => setFilter({ to: localInputToIso(e.target.value) })} />
            </div>
          </>
        ) : null}
        <div className="form-field form-field-xs">
          <label htmlFor={ids + 'kind'} className="field-label">
            {t('analytics.filters.mediaKind')}
          </label>
          <select id={ids + 'kind'} className="select-compact" value={f.mediaKind} onChange={(e) => setFilter({ mediaKind: e.target.value })}>
            <option value="">{t('common.any')}</option>
            {MEDIA_KINDS.map((k) => (
              <option key={k} value={k}>
                {t('enums.mediaKind.' + k)}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field form-field-xs">
          <label htmlFor={ids + 'ep'} className="field-label">
            {t('analytics.filters.endpoint')}
          </label>
          <select id={ids + 'ep'} className="select-compact" value={f.endpoint} onChange={(e) => setFilter({ endpoint: e.target.value })}>
            <option value="">{t('common.any')}</option>
            {((endpoints.data || []).map((e) => e.Name)).concat(f.endpoint && !(endpoints.data || []).some((e) => e.Name === f.endpoint) ? [f.endpoint] : []).map((n) => (
              <option key={n} value={n}>
                {n}
              </option>
            ))}
          </select>
        </div>
        <form
          className="form-field form-field-xs"
          onSubmit={(e) => {
            e.preventDefault();
            setFilter({ model: modelDraft.trim() });
          }}
        >
          <label htmlFor={ids + 'model'} className="field-label">
            {t('analytics.filters.model')}
          </label>
          <input
            id={ids + 'model'}
            className="mono"
            list={ids + 'models'}
            value={modelDraft}
            onChange={(e) => setModelDraft(e.target.value)}
            onBlur={() => modelDraft.trim() !== f.model && setFilter({ model: modelDraft.trim() })}
            placeholder={t('analytics.filters.modelPlaceholder')}
          />
          <datalist id={ids + 'models'}>
            {modelNames.map((m) => (
              <option key={m} value={m} />
            ))}
          </datalist>
        </form>
        <div className="filter-bar-end">
          {summary.loadedAt ? <span className="muted filter-note">{t('common.updatedAt', { time: formatDateTime(summary.loadedAt, locale, { timeStyle: 'medium' }) })}</span> : null}
          <button type="button" className="button button-secondary button-sm" onClick={refresh} disabled={summary.loading}>
            <Icon name="refresh" className={summary.loading ? 'spin' : ''} />
            {t('common.refresh')}
          </button>
        </div>
      </div>
      <p className="help-text">
        {t('analytics.windowNote', {
          from: formatDateTime(win.fromUtc, locale, { dateStyle: 'medium', timeStyle: 'short' }),
          to: formatDateTime(win.toUtc, locale, { dateStyle: 'medium', timeStyle: 'short' }),
          bucket: t('analytics.buckets.' + bucket)
        })}
      </p>
    </section>
  );

  const listColumns = [
    { key: 'StartedUtc', label: t('analytics.list.started'), pinned: true, nowrap: true, render: (r) => <span title={formatDateTime(r.StartedUtc, locale)}>{formatRelativeTime(r.StartedUtc, locale)}</span> },
    { key: 'MediaKind', label: t('analytics.list.kind'), sortable: true, render: (r) => t('enums.mediaKind.' + r.MediaKind, { defaultValue: r.MediaKind }) },
    { key: 'Success', label: t('analytics.list.status'), pinned: true, render: (r) => <StatusBadge kind="custom" tone={r.Success ? 'success' : 'danger'} label={r.Success ? t('status.succeeded') : t('status.failed')} /> },
    { key: 'AnalysisMs', label: t('analytics.list.analysisTime'), sortable: true, align: 'right', nowrap: true, render: (r) => fmt('duration', r.AnalysisMs) },
    { key: 'QueueWaitMs', label: t('analytics.list.queueWait'), sortable: true, align: 'right', nowrap: true, render: (r) => fmt('duration', r.QueueWaitMs) },
    { key: 'MediaDurationMs', label: t('analytics.list.mediaDuration'), sortable: true, align: 'right', nowrap: true, render: (r) => (r.MediaDurationMs ? fmt('duration', r.MediaDurationMs) : '') },
    { key: 'MediaBytes', label: t('analytics.list.mediaSize'), sortable: true, align: 'right', nowrap: true, defaultHidden: true, render: (r) => (r.MediaBytes ? fmt('bytes', r.MediaBytes) : '') },
    { key: 'RealtimeFactor', label: t('analytics.list.realtimeFactor'), sortable: true, align: 'right', nowrap: true, render: (r) => (typeof r.RealtimeFactor === 'number' ? fmt('ratio', r.RealtimeFactor) : '') },
    {
      key: 'Model',
      label: t('analytics.list.model'),
      mono: true,
      render: (r) => r.VisionModel || r.TranscriptionModel || r.TextModel || r.VisionEndpoint || r.TranscriptionEndpoint || r.TextEndpoint || ''
    },
    { key: 'ModelCalls', label: t('analytics.list.calls'), sortable: true, align: 'right', render: (r) => formatNumber(r.ModelCalls || 0, locale) },
    { key: 'TotalTokens', label: t('analytics.list.tokens'), sortable: true, align: 'right', render: (r) => formatNumber(r.TotalTokens || 0, locale) },
    { key: 'PrincipalName', label: t('analytics.list.principal'), sortable: true, defaultHidden: true },
    { key: 'Id', label: t('analytics.list.id'), nowrap: true, defaultHidden: true, render: (r) => <CopyableId value={r.Id} /> }
  ];
  const rowActions = (r) => [
    { id: 'view', label: t('common.view'), icon: 'eye', onClick: () => setDetailId(r.Id) },
    { id: 'json', label: t('common.viewJson'), icon: 'code', onClick: () => setJsonRecord(r) }
  ];

  const modelColumns = [
    { key: 'Endpoint', label: t('analytics.byModel.endpoint'), pinned: true, sortable: true, mono: true },
    { key: 'Provider', label: t('analytics.byModel.provider'), sortable: true, render: (m) => t('enums.provider.' + m.Provider, { defaultValue: m.Provider }) },
    { key: 'Model', label: t('analytics.byModel.model'), sortable: true, mono: true },
    { key: 'Calls', label: t('analytics.byModel.calls'), sortable: true, align: 'right', render: (m) => formatNumber(m.Calls || 0, locale) },
    { key: 'Failed', label: t('analytics.byModel.failed'), sortable: true, align: 'right', render: (m) => <span className={m.Failed ? 'text-danger' : ''}>{formatNumber(m.Failed || 0, locale)}</span> },
    { key: 'DurP50', label: t('analytics.byModel.durationP50'), sortable: true, align: 'right', nowrap: true, sortValue: (m) => distValue(m, 'DurationMs', 'P50'), render: (m) => fmt('duration', distValue(m, 'DurationMs', 'P50')) },
    { key: 'DurP95', label: t('analytics.byModel.durationP95'), sortable: true, align: 'right', nowrap: true, sortValue: (m) => distValue(m, 'DurationMs', 'P95'), render: (m) => fmt('duration', distValue(m, 'DurationMs', 'P95')) },
    { key: 'TtftP50', label: t('analytics.byModel.ttftP50'), sortable: true, align: 'right', nowrap: true, sortValue: (m) => distValue(m, 'TimeToFirstTokenMs', 'P50'), render: (m) => fmt('duration', distValue(m, 'TimeToFirstTokenMs', 'P50')) },
    { key: 'TtftP95', label: t('analytics.byModel.ttftP95'), sortable: true, align: 'right', nowrap: true, defaultHidden: true, sortValue: (m) => distValue(m, 'TimeToFirstTokenMs', 'P95'), render: (m) => fmt('duration', distValue(m, 'TimeToFirstTokenMs', 'P95')) },
    { key: 'GenP50', label: t('analytics.byModel.generationP50'), sortable: true, align: 'right', nowrap: true, defaultHidden: true, sortValue: (m) => distValue(m, 'GenerationMs', 'P50'), render: (m) => fmt('duration', distValue(m, 'GenerationMs', 'P50')) },
    { key: 'TpsP50', label: t('analytics.byModel.tokensPerSecondP50'), sortable: true, align: 'right', nowrap: true, sortValue: (m) => distValue(m, 'TokensPerSecond', 'P50'), render: (m) => fmt('rate', distValue(m, 'TokensPerSecond', 'P50')) },
    { key: 'PromptTokens', label: t('analytics.byModel.promptTokens'), sortable: true, align: 'right', render: (m) => formatNumber(m.PromptTokens || 0, locale) },
    { key: 'CompletionTokens', label: t('analytics.byModel.completionTokens'), sortable: true, align: 'right', render: (m) => formatNumber(m.CompletionTokens || 0, locale) }
  ];

  return (
    <div className="view view-wide">
      <PageHeader title={t('analytics.title')} summary={t('analytics.summary')} />
      {filterBar}
      {unavailable ? (
        <EmptyState icon="analytics" title={t('analytics.unavailableTitle')} message={t('analytics.unavailableMessage')} />
      ) : (
        <>
          {summary.error ? <ErrorBanner error={summary.error} onRetry={summary.reload} /> : null}
          {summary.loading && !s ? <LoadingState label={t('analytics.loading')} /> : null}
          {s && !hasData ? (
            <EmptyState icon="analytics" title={t('analytics.emptyTitle')} message={t('analytics.emptyMessage')}>
              <Link to="/dashboard/analyze/image" className="button button-secondary button-sm">
                <Icon name="image" />
                {t('home.cta.image')}
              </Link>
              <Link to="/dashboard/analyze/audio" className="button button-secondary button-sm">
                <Icon name="audio" />
                {t('home.cta.audio')}
              </Link>
            </EmptyState>
          ) : null}
          {s && hasData ? (
            <>
              <section className="kpi-grid" aria-label={t('analytics.kpi.label')}>
                <KpiCard icon="activity" label={t('analytics.kpi.analyses')} value={formatNumber(totals.Count, locale)} sub={t('analytics.kpi.analysesSub', { ok: formatNumber(totals.Succeeded, locale), failed: formatNumber(totals.Failed, locale) })} />
                <KpiCard
                  icon="check"
                  label={t('analytics.kpi.successRate')}
                  value={formatPercent(totals.SuccessRate, locale, 1)}
                  tone={totals.SuccessRate < 0.9 ? 'danger' : totals.SuccessRate < 0.99 ? 'warning' : 'success'}
                />
                <KpiCard icon="clock" label={t('analytics.kpi.analysisP50')} value={fmt('duration', distValue(totals, 'AnalysisMs', 'P50'))} sub={t('analytics.kpi.p95', { value: fmt('duration', distValue(totals, 'AnalysisMs', 'P95')) })} />
                <KpiCard icon="jobs" label={t('analytics.kpi.queueP95')} value={fmt('duration', distValue(totals, 'QueueWaitMs', 'P95'))} sub={t('analytics.kpi.avg', { value: fmt('duration', distValue(totals, 'QueueWaitMs', 'Avg')) })} />
                <KpiCard icon="zap" label={t('analytics.kpi.ttftP50')} value={fmt('duration', distValue(totals, 'TimeToFirstTokenMs', 'P50'))} sub={t('analytics.kpi.p95', { value: fmt('duration', distValue(totals, 'TimeToFirstTokenMs', 'P95')) })} />
                <KpiCard icon="gauge" label={t('analytics.kpi.tokensPerSecond')} value={fmt('rate', distValue(totals, 'TokensPerSecond', 'P50'))} sub={t('analytics.kpi.generationP50', { value: fmt('duration', distValue(totals, 'GenerationMs', 'P50')) })} />
                <KpiCard
                  icon="endpoints"
                  label={t('analytics.kpi.modelCalls')}
                  value={formatNumber(totals.ModelCalls, locale)}
                  tone={totals.FailedModelCalls ? 'warning' : null}
                  sub={t('analytics.kpi.modelCallsSub', { failed: formatNumber(totals.FailedModelCalls, locale), retries: formatNumber(totals.Retries, locale) })}
                />
                <KpiCard icon="code" label={t('analytics.kpi.totalTokens')} value={formatCompact(totals.TotalTokens, locale)} title={formatNumber(totals.TotalTokens, locale)} sub={t('analytics.kpi.perAnalysis', { value: fmt('count', distValue(totals, 'TokensPerAnalysis', 'Avg')) })} />
              </section>

              <section className="media-kind-grid" aria-label={t('analytics.byKind.label')}>
                {MEDIA_KINDS.map((k) => {
                  const b = s.ByMediaKind && s.ByMediaKind[k];
                  const active = !f.mediaKind || f.mediaKind === k;
                  return (
                    <div key={k} className={'panel media-kind-card' + (active ? '' : ' dim')}>
                      <div className="panel-header">
                        <h2 className="panel-title">
                          <Icon name={k === 'Image' ? 'image' : k === 'Video' ? 'video' : 'audio'} />
                          {t('enums.mediaKind.' + k)}
                        </h2>
                        <span className="muted">{t('analytics.byKind.count', { count: b ? b.Count : 0, value: formatNumber(b ? b.Count : 0, locale) })}</span>
                      </div>
                      {b && b.Count ? (
                        <dl className="mini-stats">
                          <div>
                            <dt>{t('analytics.kpi.successRate')}</dt>
                            <dd>{formatPercent(b.SuccessRate, locale, 1)}</dd>
                          </div>
                          <div>
                            <dt>{t('analytics.byKind.analysisP50')}</dt>
                            <dd>{fmt('duration', distValue(b, 'AnalysisMs', 'P50'))}</dd>
                          </div>
                          <div>
                            <dt>{t('analytics.byKind.analysisP95')}</dt>
                            <dd>{fmt('duration', distValue(b, 'AnalysisMs', 'P95'))}</dd>
                          </div>
                          {k !== 'Image' ? (
                            <div>
                              <dt>{t('analytics.byKind.realtimeP50')}</dt>
                              <dd>{fmt('ratio', distValue(b, 'RealtimeFactor', 'P50'))}</dd>
                            </div>
                          ) : (
                            <div>
                              <dt>{t('analytics.byKind.imagesIn')}</dt>
                              <dd>{formatNumber(b.InputImages || 0, locale)}</dd>
                            </div>
                          )}
                          <div>
                            <dt>{k === 'Image' ? t('analytics.byKind.mediaSizeP50') : t('analytics.byKind.mediaDurationP50')}</dt>
                            <dd>{k === 'Image' ? fmt('bytes', distValue(b, 'MediaBytes', 'P50')) : fmt('duration', distValue(b, 'MediaDurationMs', 'P50'))}</dd>
                          </div>
                          <div>
                            <dt>{t('analytics.byKind.tokens')}</dt>
                            <dd>{formatCompact(b.TotalTokens || 0, locale)}</dd>
                          </div>
                        </dl>
                      ) : (
                        <p className="muted">{t('analytics.byKind.none')}</p>
                      )}
                    </div>
                  );
                })}
              </section>

              <section className="panel chart-panel">
                <div className="panel-header">
                  <h2 className="panel-title">{t('analytics.charts.volume')}</h2>
                  <span className="muted">{t('analytics.charts.volumeHint')}</span>
                </div>
                <ChartLegend series={volumeSeries} />
                <TimeSeriesChart
                  type="bar"
                  integer
                  buckets={buckets}
                  series={volumeSeries}
                  bucket={bucket}
                  height={440}
                  formatValue={(v) => formatCompact(v, locale)}
                  ariaLabel={t('analytics.charts.volumeAria', { count: totals.Count })}
                  onBucketClick={onVolumeClick}
                />
              </section>

              <section className="panel chart-panel">
                <div className="panel-header">
                  <h2 className="panel-title">{t('analytics.charts.latency')}</h2>
                  <div className="panel-actions">
                    <label htmlFor={ids + 'metric'} className="sr-only">
                      {t('analytics.charts.metric')}
                    </label>
                    <select id={ids + 'metric'} className="select-compact" value={metric} onChange={(e) => setMetric(e.target.value)}>
                      {METRICS.map((m) => (
                        <option key={m.key} value={m.key}>
                          {t('analytics.metrics.' + m.key)}
                        </option>
                      ))}
                    </select>
                  </div>
                </div>
                <ChartLegend series={latencySeries} onToggle={(k) => setHiddenStats((h) => (h.includes(k) ? h.filter((x) => x !== k) : [...h, k]))} />
                <TimeSeriesChart
                  buckets={buckets}
                  series={latencySeries}
                  bucket={bucket}
                  height={520}
                  formatValue={(v) => fmt(metricDef.type, v, true)}
                  ariaLabel={t('analytics.charts.latencyAria', { metric: t('analytics.metrics.' + metric) })}
                  emptyText={t('analytics.charts.noSamples')}
                />
              </section>

              <section className="panel chart-panel">
                <div className="panel-header">
                  <h2 className="panel-title">{t('analytics.charts.tokens')}</h2>
                </div>
                <ChartLegend series={tokenSeries} />
                <TimeSeriesChart type="bar" integer buckets={buckets} series={tokenSeries} bucket={bucket} height={400} ariaLabel={t('analytics.charts.tokens')} />
              </section>

              <section className="panel chart-panel">
                <div className="panel-header">
                  <h2 className="panel-title">{t('analytics.charts.successRate')}</h2>
                </div>
                <ChartLegend series={successSeries} />
                <TimeSeriesChart buckets={buckets} series={successSeries} bucket={bucket} height={400} formatValue={(v) => formatPercent(v, locale, 0)} ariaLabel={t('analytics.charts.successRate')} />
              </section>

              <section className="panel">
                <div className="panel-header">
                  <h2 className="panel-title">{t('analytics.distributions.title')}</h2>
                </div>
                <p className="help-text">{t('analytics.distributions.help')}</p>
                <DistributionTable block={totals} />
              </section>

              <section className="panel">
                <h2 className="panel-title">{t('analytics.totals.title')}</h2>
                <dl className="stat-strip">
                  {[
                    ['promptTokens', formatNumber(totals.PromptTokens, locale)],
                    ['completionTokens', formatNumber(totals.CompletionTokens, locale)],
                    ['cachedTokens', formatNumber(totals.CachedTokens, locale)],
                    ['reasoningTokens', formatNumber(totals.ReasoningTokens, locale)],
                    ['totalTokens', formatNumber(totals.TotalTokens, locale)],
                    ['tokensPerAnalysisP50', fmt('count', distValue(totals, 'TokensPerAnalysis', 'P50'))],
                    ['tokensPerAnalysisP95', fmt('count', distValue(totals, 'TokensPerAnalysis', 'P95'))],
                    ['modelCalls', formatNumber(totals.ModelCalls, locale)],
                    ['failedModelCalls', formatNumber(totals.FailedModelCalls, locale)],
                    ['retries', formatNumber(totals.Retries, locale)],
                    ['repairAttempts', formatNumber(totals.RepairAttempts, locale)],
                    ['inputImages', formatNumber(totals.InputImages, locale)],
                    ['inputImageBytes', formatBytes(totals.InputImageBytes, locale)],
                    ['inputAudioBytes', formatBytes(totals.InputAudioBytes, locale)],
                    ['scenes', formatNumber(totals.Scenes, locale)],
                    ['transcriptCharacters', formatNumber(totals.TranscriptCharacters, locale)]
                  ].map(([k, v]) => (
                    <div key={k} className="stat-strip-item">
                      <dt>{t('analytics.totals.' + k)}</dt>
                      <dd className="mono">{v}</dd>
                    </div>
                  ))}
                </dl>
              </section>

              <section className="analytics-section">
                <h2 className="section-heading">{t('analytics.byModel.title')}</h2>
                <DataTable
                  tableId="analyticsByModel"
                  columns={modelColumns}
                  rows={byModel}
                  rowKey={(m) => m.Endpoint + '|' + (m.Model || '')}
                  sortNote={t('table.sortLocal')}
                  emptyTitle={t('analytics.byModel.empty')}
                  onRowClick={(m) => {
                    setModelDraft(m.Model || '');
                    setFilter({ endpoint: m.Endpoint, model: m.Model || '' });
                  }}
                />
              </section>

              <section className="panel">
                  <h2 className="panel-title">{t('analytics.byStage.title')}</h2>
                  {byStage.length ? (
                    <div className="table-scroll">
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th scope="col">{t('analytics.byStage.stage')}</th>
                            <th scope="col" className="align-right">{t('analytics.columns.samples')}</th>
                            <th scope="col" className="align-right">{t('analytics.stats.p50')}</th>
                            <th scope="col" className="align-right">{t('analytics.stats.p95')}</th>
                            <th scope="col" className="align-right">{t('analytics.stats.max')}</th>
                            <th scope="col">
                              <span className="sr-only">{t('analytics.byStage.share')}</span>
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {byStage.map((r) => (
                            <tr key={r.Stage}>
                              <th scope="row" className="cell-label">
                                {t('enums.stage.' + r.Stage, { defaultValue: r.Stage })}
                              </th>
                              <td className="align-right mono">{formatNumber((r.DurationMs && r.DurationMs.Count) || 0, locale)}</td>
                              <td className="align-right mono cell-nowrap">{fmt('duration', distValue(r, 'DurationMs', 'P50'))}</td>
                              <td className="align-right mono cell-nowrap">{fmt('duration', distValue(r, 'DurationMs', 'P95'))}</td>
                              <td className="align-right mono cell-nowrap">{fmt('duration', distValue(r, 'DurationMs', 'Max'))}</td>
                              <td className="cell-meter">
                                <Meter value={distValue(r, 'DurationMs', 'P50')} max={stageMax} />
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <p className="muted">{t('analytics.byStage.empty')}</p>
                  )}
                </section>
                <section className="panel">
                  <h2 className="panel-title">{t('analytics.byPurpose.title')}</h2>
                  {byPurpose.length ? (
                    <div className="table-scroll">
                      <table className="data-table">
                        <thead>
                          <tr>
                            <th scope="col">{t('analytics.byPurpose.purpose')}</th>
                            <th scope="col" className="align-right">{t('analytics.byPurpose.calls')}</th>
                            <th scope="col" className="align-right">{t('analytics.byPurpose.failed')}</th>
                            <th scope="col" className="align-right">{t('analytics.byPurpose.durationP50')}</th>
                            <th scope="col" className="align-right">{t('analytics.byPurpose.durationP95')}</th>
                            <th scope="col" className="align-right">{t('analytics.byPurpose.ttftP50')}</th>
                          </tr>
                        </thead>
                        <tbody>
                          {byPurpose.map((r) => (
                            <tr key={r.Purpose}>
                              <th scope="row" className="cell-label">
                                {t('enums.purpose.' + r.Purpose, { defaultValue: r.Purpose })}
                              </th>
                              <td className="align-right mono">{formatNumber(r.Calls || 0, locale)}</td>
                              <td className={'align-right mono' + (r.Failed ? ' text-danger' : '')}>{formatNumber(r.Failed || 0, locale)}</td>
                              <td className="align-right mono cell-nowrap">{fmt('duration', distValue(r, 'DurationMs', 'P50'))}</td>
                              <td className="align-right mono cell-nowrap">{fmt('duration', distValue(r, 'DurationMs', 'P95'))}</td>
                              <td className="align-right mono cell-nowrap">{fmt('duration', distValue(r, 'TimeToFirstTokenMs', 'P50'))}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  ) : (
                    <p className="muted">{t('analytics.byPurpose.empty')}</p>
                  )}
                </section>
            </>
          ) : null}

          <section className="analytics-section">
            <h2 className="section-heading">{t('analytics.list.title')}</h2>
            {bucketWindow ? (
              <Notice
                title={t('analytics.list.bucketTitle')}
                actions={
                  <button type="button" className="button button-secondary button-sm" onClick={() => setBucketWindow(null)}>
                    {t('analytics.list.bucketClear')}
                  </button>
                }
              >
                {t('analytics.list.bucketMessage', {
                  from: formatDateTime(bucketWindow.fromUtc, locale, { dateStyle: 'medium', timeStyle: 'short' }),
                  to: formatDateTime(bucketWindow.toUtc, locale, { timeStyle: 'short' })
                })}
              </Notice>
            ) : null}
            <DataTable
              tableId="analyses"
              columns={listColumns}
              rows={(list.data && list.data.items) || []}
              loading={list.loading}
              error={list.error && !unavailable ? list.error : null}
              onRetry={list.reload}
              onRowClick={(r) => setDetailId(r.Id)}
              rowActions={rowActions}
              rowClassName={(r) => (r.Success ? '' : 'row-danger')}
              sortNote={t('table.sortPage')}
              toolbar={
                <div className="filter-bar">
                  <div className="form-field form-field-xs">
                    <label htmlFor={ids + 'success'} className="field-label">
                      {t('analytics.list.statusFilter')}
                    </label>
                    <select id={ids + 'success'} className="select-compact" value={f.success} onChange={(e) => setFilter({ success: e.target.value })}>
                      <option value="">{t('common.any')}</option>
                      <option value="true">{t('status.succeeded')}</option>
                      <option value="false">{t('status.failed')}</option>
                    </select>
                  </div>
                  <span className="muted filter-note">{t('analytics.list.filterNote')}</span>
                </div>
              }
              emptyTitle={t('analytics.list.empty')}
              emptyMessage={t('analytics.list.emptyMessage')}
              pagination={
                <Pagination
                  pageNumber={page}
                  pageSize={pageSize}
                  totalCount={(list.data && list.data.totalCount) || 0}
                  onPageChange={(p) => setFilter({ page: p > 1 ? String(p) : '' })}
                  onPageSizeChange={(sz) => {
                    setPageSize(sz);
                    setFilter({ page: '' });
                  }}
                  onRefresh={list.reload}
                  loading={list.loading}
                />
              }
            />
          </section>
        </>
      )}
      <AnalysisDrawer
        id={detailId}
        onClose={() => setDetailId(null)}
        onDeleted={() => {
          setDetailId(null);
          list.reload();
          summary.reload();
        }}
      />
      <JsonViewerModal open={!!jsonRecord} onClose={() => setJsonRecord(null)} title={t('analytics.detail.jsonTitle')} subtitle={jsonRecord && jsonRecord.Id} value={jsonRecord} filename={(jsonRecord && jsonRecord.Id) + '.json'} />
    </div>
  );
}

export default AnalyticsView;
