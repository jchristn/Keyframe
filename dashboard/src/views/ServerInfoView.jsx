import { useAuth } from '../context/AuthContext';
import { useHealth } from '../components/DashboardLayout';
import { useLocale } from '../i18n';
import { formatDateTime, formatDuration, formatNumber } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import StatusBadge from '../components/StatusBadge';
import CopyButton from '../components/CopyButton';
import Icon from '../components/Icon';
import { ErrorBanner, LoadingState } from '../components/States';
import { APP_VERSION, REPOSITORY_URL } from '../routes';

/** Server info: health, version, auth context, and copyable server URLs. */
function ServerInfoView() {
  const { t, locale } = useLocale();
  const { serverUrl, principal, isAdmin } = useAuth();
  const { health, healthError, refreshHealth } = useHealth();

  const urls = [
    ['server.urls.base', serverUrl],
    ['server.urls.health', serverUrl + '/v1.0/health'],
    ['server.urls.openapi', serverUrl + '/openapi.json'],
    ['server.urls.swagger', serverUrl + '/swagger'],
    ['server.urls.whoami', serverUrl + '/v1.0/whoami'],
    ['server.urls.dashboard', window.location.origin]
  ];

  return (
    <div className="view">
      <PageHeader
        title={t('server.title')}
        summary={t('server.summary')}
        actions={
          <button type="button" className="button button-secondary" onClick={refreshHealth}>
            <Icon name="refresh" />
            {t('common.refresh')}
          </button>
        }
      />
      {healthError ? <ErrorBanner error={healthError} onRetry={refreshHealth} /> : null}
      <div className="two-col">
        <section className="panel">
          <h2 className="panel-title">{t('server.health')}</h2>
          {!health && !healthError ? <LoadingState compact /> : null}
          {health ? (
            <dl className="kv-grid">
              <div>
                <dt>{t('server.status')}</dt>
                <dd>
                  <StatusBadge tone={health.Status === 'Healthy' ? 'success' : 'warning'} label={t('enums.health.' + health.Status, { defaultValue: health.Status })} />
                </dd>
              </div>
              <div>
                <dt>{t('server.version')}</dt>
                <dd className="mono">{health.Version}</dd>
              </div>
              <div>
                <dt>{t('server.started')}</dt>
                <dd>{formatDateTime(health.StartedUtc, locale)}</dd>
              </div>
              <div>
                <dt>{t('server.uptime')}</dt>
                <dd>{formatDuration(health.UptimeSeconds * 1000, locale)}</dd>
              </div>
              <div>
                <dt>{t('server.database')}</dt>
                <dd>{health.Database}</dd>
              </div>
              <div>
                <dt>{t('server.jobs')}</dt>
                <dd>{t('server.jobsValue', { active: formatNumber(health.ActiveJobs, locale), queued: formatNumber(health.QueuedJobs, locale) })}</dd>
              </div>
              <div className="kv-grid-wide">
                <dt>{t('server.ffmpeg')}</dt>
                <dd>
                  <StatusBadge kind="custom" tone={health.FfmpegAvailable ? 'success' : 'danger'} label={health.FfmpegAvailable ? t('status.available') : t('status.missing')} />{' '}
                  <span className="mono muted break-all">{health.FfmpegVersion || t('server.ffmpegMissingHelp')}</span>
                </dd>
              </div>
              <div className="kv-grid-wide">
                <dt>{t('server.endpoints')}</dt>
                <dd className="chip-list">
                  {Object.entries(health.Endpoints || {}).length
                    ? Object.entries(health.Endpoints).map(([name, ok]) => (
                        <span key={name} className="chip">
                          <span className="mono">{name}</span> <StatusBadge kind="health" value={ok} />
                        </span>
                      ))
                    : t('server.noEndpoints')}
                </dd>
              </div>
            </dl>
          ) : null}
        </section>
        <section className="panel">
          <h2 className="panel-title">{t('server.identity')}</h2>
          <dl className="kv-grid">
            <div>
              <dt>{t('server.principal')}</dt>
              <dd className="mono">{(principal && principal.PrincipalName) || t('topbar.anonymous')}</dd>
            </div>
            <div>
              <dt>{t('server.role')}</dt>
              <dd>
                <span className={'badge ' + (isAdmin ? 'badge-accent' : 'badge-neutral')}>{isAdmin ? t('roles.admin') : t('roles.accessKey')}</span>
              </dd>
            </div>
            <div>
              <dt>{t('server.authRequired')}</dt>
              <dd>
                <StatusBadge kind="custom" tone={principal && principal.AuthenticationRequired ? 'success' : 'warning'} label={principal && principal.AuthenticationRequired ? t('common.yes') : t('common.no')} />
              </dd>
            </div>
            <div>
              <dt>{t('server.dashboardVersion')}</dt>
              <dd className="mono">{APP_VERSION}</dd>
            </div>
            <div className="kv-grid-wide">
              <dt>{t('server.repository')}</dt>
              <dd>
                <a href={REPOSITORY_URL} target="_blank" rel="noreferrer" className="mono">
                  {REPOSITORY_URL}
                </a>
              </dd>
            </div>
          </dl>
        </section>
      </div>
      <section className="panel">
        <h2 className="panel-title">{t('server.urls.title')}</h2>
        <ul className="url-list">
          {urls.map(([k, url]) => (
            <li key={k}>
              <span className="url-label">{t(k)}</span>
              <code className="mono break-all">{url}</code>
              <CopyButton value={url} label={t('server.copyUrl', { name: t(k) })} />
            </li>
          ))}
        </ul>
      </section>
    </div>
  );
}

export default ServerInfoView;
