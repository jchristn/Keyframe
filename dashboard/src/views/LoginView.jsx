import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import LanguageSelector from '../components/LanguageSelector';
import Icon from '../components/Icon';
import { DEFAULT_SERVER_URL } from '../utils/api';
import { REPOSITORY_URL } from '../routes';

function isLoopback(url) {
  try {
    const host = new URL(url).hostname;
    return host === '127.0.0.1' || host === '::1' || host === '[::1]';
  } catch {
    return false;
  }
}

function loginErrorKey(err) {
  if (!err) return null;
  if (err.network) return 'login.errors.unreachable';
  if (err.status === 401 || err.status === 403) return 'login.errors.badKey';
  if (err.status === 429) return 'login.errors.rateLimited';
  if (err.status === 404) return 'login.errors.notKeyframe';
  return 'login.errors.unexpected';
}

/** Connect to a Keyframe server with a URL and key. Validates via GET /v1.0/whoami. */
function LoginView() {
  const { t } = useTranslation();
  const { login, serverUrl: savedUrl, restoreError, hasSavedKey, restore } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const urlId = useId();
  const keyId = useId();
  const [serverUrl, setServerUrl] = useState(savedUrl || DEFAULT_SERVER_URL);
  const [apiKey, setApiKey] = useState('');
  const [showKey, setShowKey] = useState(false);
  const [remember, setRemember] = useState(true);
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    document.title = t('app.documentTitle', { page: t('login.title') });
  }, [t]);

  const urlValid = (() => {
    try {
      const u = new URL(serverUrl);
      return u.protocol === 'http:' || u.protocol === 'https:';
    } catch {
      return false;
    }
  })();

  const submit = async (e) => {
    e.preventDefault();
    if (!urlValid) {
      setError({ key: 'login.errors.badUrl' });
      return;
    }
    setBusy(true);
    setError(null);
    try {
      await login(serverUrl, apiKey.trim(), remember);
    } catch (err) {
      setError({ key: loginErrorKey(err), detail: err && !err.network ? err.message : null });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="login-page">
      <div className="login-utilities">
        <LanguageSelector compact />
        <button type="button" className="icon-button" onClick={toggleTheme} aria-label={theme === 'dark' ? t('topbar.lightMode') : t('topbar.darkMode')} title={theme === 'dark' ? t('topbar.lightMode') : t('topbar.darkMode')}>
          <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
        </button>
        <a className="icon-button" href={REPOSITORY_URL} target="_blank" rel="noreferrer" aria-label={t('topbar.repository')} title={t('topbar.repository')}>
          <Icon name="github" size={18} />
        </a>
      </div>
      <main className="login-card">
        <div className="login-brand">
          <img className="login-logo logo-for-light" src="/logo-dark.png" alt={t('app.name')} />
          <img className="login-logo logo-for-dark" src="/logo-light.png" alt={t('app.name')} />
          <p className="login-tagline">{t('login.tagline')}</p>
        </div>
        <h1 className="login-title">{t('login.title')}</h1>
        {restoreError ? (
          <div className="banner banner-warning" role="alert">
            <Icon name="alert" />
            <div className="banner-body">
              <div>{t(restoreError, { url: savedUrl })}</div>
            </div>
            {hasSavedKey ? (
              <div className="banner-actions">
                <button type="button" className="button button-secondary button-sm" onClick={restore}>
                  {t('login.retrySaved')}
                </button>
              </div>
            ) : null}
          </div>
        ) : null}
        <form className="login-form" onSubmit={submit} noValidate>
          <div className="form-field">
            <label htmlFor={urlId} className="field-label">
              {t('login.serverUrl')}
            </label>
            <input
              id={urlId}
              type="url"
              className="mono"
              value={serverUrl}
              onChange={(e) => setServerUrl(e.target.value)}
              placeholder={DEFAULT_SERVER_URL}
              required
              disabled={busy}
              autoComplete="url"
              aria-invalid={!urlValid || undefined}
            />
            <p className="help-text">{t('login.serverUrlHelp')}</p>
          </div>
          <div className="form-field">
            <label htmlFor={keyId} className="field-label">
              {t('login.apiKey')}
            </label>
            <div className="input-with-button">
              <input
                id={keyId}
                type={showKey ? 'text' : 'password'}
                className="mono"
                value={apiKey}
                onChange={(e) => setApiKey(e.target.value)}
                placeholder={t('login.apiKeyPlaceholder')}
                disabled={busy}
                autoComplete="current-password"
                spellCheck={false}
              />
              <button type="button" className="icon-button" onClick={() => setShowKey((s) => !s)} aria-label={showKey ? t('login.hideKey') : t('login.showKey')} title={showKey ? t('login.hideKey') : t('login.showKey')}>
                <Icon name={showKey ? 'lock' : 'eye'} />
              </button>
            </div>
          </div>
          <label className="checkbox-row">
            <input type="checkbox" checked={remember} onChange={(e) => setRemember(e.target.checked)} disabled={busy} />
            <span>{t('login.remember')}</span>
          </label>
          <p className="help-text">{t('login.rememberHelp')}</p>
          {error ? (
            <div className="inline-error" role="alert">
              <div>{t(error.key, { url: serverUrl })}</div>
              {error.detail ? <div className="muted">{error.detail}</div> : null}
            </div>
          ) : null}
          <button type="submit" className="button button-primary button-block" disabled={busy}>
            {busy ? t('login.connecting') : t('login.connect')}
          </button>
        </form>
        {isLoopback(serverUrl) ? <p className="login-defaults">{t('login.localDefaults')}</p> : null}
      </main>
    </div>
  );
}

export default LoginView;
