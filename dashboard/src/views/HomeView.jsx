import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useHealth } from '../components/DashboardLayout';
import { useLocale } from '../i18n';
import { formatDuration, formatNumber, formatPercent, formatRelativeTime } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import ActivityChart from '../components/ActivityChart';
import ExternalServicesCard from '../components/ExternalServicesCard';
import StatusBadge from '../components/StatusBadge';
import { ErrorBanner, LoadingState, Notice } from '../components/States';
import Icon from '../components/Icon';
import useRemote from '../hooks/useRemote';
import useInterval from '../hooks/useInterval';
import usePersistedState from '../hooks/usePersistedState';
import { getRange, normalizeBuckets, rangeToParams } from '../utils/ranges';

function KpiCard({ label, value, sub = null, to = null, tone = null, icon }) {
  const body = (
    <>
      <div className="kpi-label">
        <Icon name={icon} />
        <span>{label}</span>
      </div>
      <div className={'kpi-value' + (tone ? ' text-' + tone : '')}>{value}</div>
      {sub ? <div className="kpi-sub">{sub}</div> : null}
    </>
  );
  return to ? (
    <Link to={to} className="kpi-card clickable">
      {body}
    </Link>
  ) : (
    <div className="kpi-card">{body}</div>
  );
}

/** Command center: health, KPIs, activity, attention items, recent jobs, CTAs, external services. */
function HomeView() {
  const { t, locale } = useLocale();
  const navigate = useNavigate();
  const { apiClient, serverUrl, principal } = useAuth();
  const { health, healthError, refreshHealth } = useHealth();
  const [rangeId, setRangeId] = usePersistedState('keyframe.home.range', 'day');
  const [params, setParams] = useState(() => rangeToParams(rangeId));

  const summary = useRemote(() => apiClient.getRequestHistorySummary(params), [apiClient, params]);
  const jobs = useRemote(() => apiClient.listJobs({ pageSize: 5 }), [apiClient]);
  const failedJobs = useRemote(() => apiClient.listJobs({ status: 'Failed', pageSize: 1 }), [apiClient]);
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient]);

  const refreshAll = useCallback(() => {
    setParams(rangeToParams(rangeId));
    jobs.reload();
    failedJobs.reload();
    endpoints.reload();
    refreshHealth();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rangeId, jobs.reload, failedJobs.reload, endpoints.reload, refreshHealth]);

  useInterval(() => {
    setParams(rangeToParams(rangeId));
    jobs.reload();
  }, 30000);

  const changeRange = (id) => {
    setRangeId(id);
    setParams(rangeToParams(id));
  };

  const range = getRange(rangeId);
  const buckets = useMemo(() => normalizeBuckets(summary.data, params, range.bucketCount), [summary.data, params, range.bucketCount]);
  const s = summary.data;
  const successRate = s && s.TotalCount ? s.TotalSuccess / s.TotalCount : null;
  const eps = endpoints.data || [];
  const activeEps = eps.filter((e) => e.Active);
  const unhealthy = activeEps.filter((e) => !e.Healthy);

  const attention = [];
  if (healthError) attention.push({ tone: 'danger', key: 'home.attention.unreachable', to: '/dashboard/server' });
  if (health && !health.FfmpegAvailable) attention.push({ tone: 'danger', key: 'home.attention.ffmpegMissing', to: '/dashboard/server' });
  unhealthy.forEach((e) => attention.push({ tone: 'warning', key: 'home.attention.endpointUnhealthy', params: { name: e.Name, message: e.HealthMessage || '' }, to: '/dashboard/endpoints' }));
  if (eps.length && !activeEps.length) attention.push({ tone: 'warning', key: 'home.attention.noActiveEndpoints', to: '/dashboard/endpoints' });
  if (failedJobs.data && failedJobs.data.totalCount > 0)
    attention.push({ tone: 'warning', key: 'home.attention.failedJobs', params: { count: failedJobs.data.totalCount }, to: '/dashboard/jobs?status=Failed' });
  if (s && s.TotalFailure > 0)
    attention.push({ tone: 'info', key: 'home.attention.failedRequests', params: { count: s.TotalFailure }, to: '/dashboard/request-history' });
  if (principal && !principal.AuthenticationRequired) attention.push({ tone: 'warning', key: 'home.attention.authDisabled', to: '/dashboard/settings' });

  return (
    <div className="view">
      <PageHeader
        title={t('home.title')}
        summary={t('home.summary')}
        actions={
          <button type="button" className="button button-secondary" onClick={refreshAll}>
            <Icon name="refresh" />
            {t('common.refresh')}
          </button>
        }
      />

      <section className="cta-row" aria-label={t('home.ctaLabel')}>
        <Link to="/dashboard/analyze/image" className="cta-card">
          <Icon name="image" size={20} />
          <span>
            <strong>{t('home.cta.image')}</strong>
            <span className="muted">{t('home.cta.imageSub')}</span>
          </span>
        </Link>
        <Link to="/dashboard/analyze/video" className="cta-card">
          <Icon name="video" size={20} />
          <span>
            <strong>{t('home.cta.video')}</strong>
            <span className="muted">{t('home.cta.videoSub')}</span>
          </span>
        </Link>
        <Link to="/dashboard/api-explorer" className="cta-card">
          <Icon name="explorer" size={20} />
          <span>
            <strong>{t('home.cta.explorer')}</strong>
            <span className="muted">{t('home.cta.explorerSub')}</span>
          </span>
        </Link>
      </section>

      <section className="kpi-grid" aria-label={t('home.kpiLabel')}>
        <KpiCard icon="activity" label={t('home.kpi.requests', { range: t('chart.ranges.' + rangeId) })} value={s ? formatNumber(s.TotalCount, locale) : '...'} to="/dashboard/request-history" />
        <KpiCard
          icon="check"
          label={t('home.kpi.successRate')}
          value={successRate === null ? t('common.notAvailable') : formatPercent(successRate, locale)}
          tone={successRate !== null && successRate < 0.9 ? 'warning' : null}
          sub={s ? t('home.kpi.failures', { count: s.TotalFailure, value: formatNumber(s.TotalFailure, locale) }) : null}
        />
        <KpiCard icon="zap" label={t('home.kpi.avgLatency')} value={s ? formatDuration(s.AverageDurationMs, locale) : '...'} />
        <KpiCard
          icon="jobs"
          label={t('home.kpi.jobs')}
          value={health ? formatNumber(health.ActiveJobs, locale) : '...'}
          sub={health ? t('home.kpi.queued', { value: formatNumber(health.QueuedJobs, locale) }) : null}
          to="/dashboard/jobs"
        />
        <KpiCard
          icon="endpoints"
          label={t('home.kpi.endpoints')}
          value={endpoints.data ? t('home.kpi.healthyOf', { healthy: activeEps.length - unhealthy.length, total: activeEps.length }) : '...'}
          tone={unhealthy.length ? 'danger' : null}
          to="/dashboard/endpoints"
        />
        <KpiCard
          icon="server"
          label={t('home.kpi.server')}
          value={healthError ? t('status.unreachable') : health ? t('enums.health.' + health.Status, { defaultValue: health.Status }) : '...'}
          tone={healthError ? 'danger' : health && health.Status !== 'Healthy' ? 'warning' : 'success'}
          sub={health ? (health.FfmpegAvailable ? t('home.kpi.ffmpegAvailable') : t('home.kpi.ffmpegMissing')) : null}
          to="/dashboard/server"
        />
      </section>

      <section className="panel" aria-labelledby="attention-title">
        <div className="panel-header">
          <h2 id="attention-title" className="panel-title">
            {t('home.attention.title')}
          </h2>
        </div>
        {attention.length ? (
          <ul className="attention-list">
            {attention.map((a, i) => (
              <li key={i} className={'attention-item tone-' + a.tone}>
                <Icon name={a.tone === 'info' ? 'info' : 'alert'} />
                <span>{t(a.key, a.params)}</span>
                <button type="button" className="button button-link button-sm" onClick={() => navigate(a.to)}>
                  {t('home.attention.open')}
                </button>
              </li>
            ))}
          </ul>
        ) : (
          <p className="muted">{t('home.attention.none')}</p>
        )}
      </section>

      {summary.error ? <ErrorBanner error={summary.error} onRetry={summary.reload} /> : null}
      <ActivityChart
        title={t('home.activityTitle')}
        buckets={buckets}
        bucketMinutes={params.bucketMinutes}
        rangeId={rangeId}
        onRangeChange={changeRange}
        onRefresh={() => setParams(rangeToParams(rangeId))}
        loading={summary.loading}
        loadedAt={summary.loadedAt}
        onBucketClick={(b) => navigate('/dashboard/request-history?fromUtc=' + encodeURIComponent(b.startUtc) + '&toUtc=' + encodeURIComponent(b.endUtc))}
      />
      {s && s.TotalCount === 0 && !summary.loading ? (
        <Notice title={t('home.noTraffic.title')}>
          <span>{t('home.noTraffic.message')}</span>
        </Notice>
      ) : null}

      <section className="panel" aria-labelledby="recent-jobs-title">
        <div className="panel-header">
          <h2 id="recent-jobs-title" className="panel-title">
            {t('home.recentJobs')}
          </h2>
          <Link to="/dashboard/jobs" className="button button-link button-sm">
            {t('home.viewAllJobs')}
          </Link>
        </div>
        {jobs.error ? <ErrorBanner error={jobs.error} onRetry={jobs.reload} /> : null}
        {jobs.loading && !jobs.data ? <LoadingState compact /> : null}
        {jobs.data && jobs.data.items.length === 0 ? (
          <p className="muted">
            {t('home.noJobs')} <Link to="/dashboard/analyze/video">{t('home.cta.video')}</Link>
          </p>
        ) : null}
        {jobs.data && jobs.data.items.length ? (
          <ul className="recent-list">
            {jobs.data.items.map((j) => (
              <li key={j.Id}>
                <Link to={'/dashboard/jobs/' + encodeURIComponent(j.Id)} className="recent-item">
                  <code className="mono">{j.Id}</code>
                  <StatusBadge kind="job" value={j.Status} />
                  <span className="muted">{formatRelativeTime(j.CreatedUtc, locale)}</span>
                </Link>
              </li>
            ))}
          </ul>
        ) : null}
      </section>

      <ExternalServicesCard serverUrl={serverUrl} />
    </div>
  );
}

export default HomeView;
