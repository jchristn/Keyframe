import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import CopyButton from './CopyButton';
import LanguageSelector from './LanguageSelector';
import AppearanceMenu from './AppearancePicker';
import { useAuth } from '../context/AuthContext';
import { useTheme } from '../context/ThemeContext';
import { REPOSITORY_URL } from '../routes';

/**
 * Topbar: navigation toggle, server and identity context, live health, and utilities
 * (language, repository, appearance, light/dark, logout).
 */
function Topbar({ onToggleSidebar, health, healthError }) {
  const { t } = useTranslation();
  const { serverUrl, principal, isAdmin, logout } = useAuth();
  const { theme, toggleTheme } = useTheme();

  let healthTone = 'neutral';
  let healthLabel = t('status.checking');
  if (healthError) {
    healthTone = 'danger';
    healthLabel = t('status.unreachable');
  } else if (health) {
    healthTone = health.Status === 'Healthy' ? 'success' : 'warning';
    healthLabel = t('enums.health.' + health.Status, { defaultValue: health.Status });
  }

  return (
    <header className="topbar">
      <button type="button" className="icon-button" onClick={onToggleSidebar} aria-label={t('topbar.toggleNav')} title={t('topbar.toggleNav')}>
        <Icon name="menu" size={18} />
      </button>
      <div className="topbar-context">
        <span className="chip chip-server" title={serverUrl}>
          <Icon name="server" />
          <span className="chip-text mono">{serverUrl}</span>
          <CopyButton value={serverUrl} label={t('topbar.copyServerUrl')} />
        </span>
        <span className={'chip chip-health tone-' + healthTone} title={t('topbar.health')}>
          <span className="health-dot" aria-hidden="true" />
          <span className="chip-text">{healthLabel}</span>
        </span>
        <span className="chip chip-principal" title={t('topbar.principal')}>
          <Icon name="key" />
          <span className="chip-text">{(principal && principal.PrincipalName) || t('topbar.anonymous')}</span>
          <span className={'badge badge-xs ' + (isAdmin ? 'badge-accent' : 'badge-neutral')}>{isAdmin ? t('roles.admin') : t('roles.accessKey')}</span>
        </span>
      </div>
      <div className="topbar-actions">
        <LanguageSelector compact />
        <a className="icon-button" href={REPOSITORY_URL} target="_blank" rel="noreferrer" aria-label={t('topbar.repository')} title={t('topbar.repository')}>
          <Icon name="github" size={18} />
        </a>
        <AppearanceMenu />
        <button
          type="button"
          className="icon-button"
          onClick={toggleTheme}
          aria-label={theme === 'dark' ? t('topbar.lightMode') : t('topbar.darkMode')}
          title={theme === 'dark' ? t('topbar.lightMode') : t('topbar.darkMode')}
        >
          <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
        </button>
        <button type="button" className="icon-button" onClick={() => logout()} aria-label={t('topbar.logout')} title={t('topbar.logout')}>
          <Icon name="logout" size={18} />
        </button>
      </div>
    </header>
  );
}

export default Topbar;
