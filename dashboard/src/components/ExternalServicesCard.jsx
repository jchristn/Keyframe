import { useTranslation } from 'react-i18next';
import CopyButton from './CopyButton';
import Icon from './Icon';

// Host-published ports from plan D7. Hostname follows the server URL the operator connected
// to, so a remote deployment links to the same host.
const SERVICES = [
  { id: 'grafana', port: 3700, path: '/', user: 'admin', password: 'admin' },
  { id: 'prometheus', port: 9700, path: '/' },
  { id: 'tempo', port: 3710, path: '/ready' }
];

export function serviceUrls(serverUrl) {
  let host = '127.0.0.1';
  let protocol = 'http:';
  try {
    const u = new URL(serverUrl);
    host = u.hostname;
    protocol = u.protocol;
  } catch {
    // Keep defaults.
  }
  if (host.includes(':') && !host.startsWith('[')) host = '[' + host + ']';
  return SERVICES.map((s) => ({ ...s, url: protocol + '//' + host + ':' + s.port + s.path }));
}

/** Links to the bundled observability tools with URLs and default local credentials. */
function ExternalServicesCard({ serverUrl }) {
  const { t } = useTranslation();
  const services = serviceUrls(serverUrl);
  return (
    <section className="panel" aria-labelledby="external-services-title">
      <div className="panel-header">
        <h2 id="external-services-title" className="panel-title">
          {t('externalServices.title')}
        </h2>
      </div>
      <p className="help-text">{t('externalServices.summary')}</p>
      <ul className="service-list">
        {services.map((s) => (
          <li key={s.id} className="service-item">
            <div className="service-name">
              <strong>{t('externalServices.services.' + s.id + '.name')}</strong>
              <span className="muted">{t('externalServices.services.' + s.id + '.purpose')}</span>
            </div>
            <div className="service-url">
              <a href={s.url} target="_blank" rel="noreferrer" className="mono">
                {s.url}
                <Icon name="external" size={12} />
              </a>
              <CopyButton value={s.url} label={t('externalServices.copyUrl')} />
            </div>
            <div className="service-creds">
              {s.user ? (
                <span>
                  {t('externalServices.credentials')}: <code className="mono token">{s.user}</code> / <code className="mono token">{s.password}</code>
                </span>
              ) : (
                <span className="muted">{t('externalServices.noLogin')}</span>
              )}
            </div>
          </li>
        ))}
      </ul>
      <p className="help-text">{t('externalServices.note')}</p>
    </section>
  );
}

export default ExternalServicesCard;
