import { NavLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { APP_VERSION, NAV_GROUPS } from '../routes';
import { useAuth } from '../context/AuthContext';

/** Grouped navigation with a scrollable list and a stable footer. */
function Sidebar({ collapsed, mobileOpen, onNavigate }) {
  const { t } = useTranslation();
  const { isAdmin, principal } = useAuth();
  return (
    <aside className={'sidebar' + (collapsed ? ' collapsed' : '') + (mobileOpen ? ' mobile-open' : '')} aria-label={t('nav.label')}>
      <div className="sidebar-brand">
        <img className="brand-logo logo-for-light" src="/logo-dark.png" alt={t('app.name')} />
        <img className="brand-logo logo-for-dark" src="/logo-light.png" alt={t('app.name')} />
        <img className="brand-glyph" src="/favicon.ico" alt={t('app.name')} />
      </div>
      <nav className="sidebar-nav">
        {NAV_GROUPS.map((group) => (
          <div key={group.id} className="nav-group">
            {group.labelKey ? <div className="nav-group-label">{t(group.labelKey)}</div> : null}
            <ul>
              {group.items.map((item) => (
                <li key={item.id}>
                  <NavLink
                    to={item.path}
                    className={({ isActive }) => 'nav-item' + (isActive ? ' active' : '')}
                    title={collapsed ? t(item.labelKey) : undefined}
                    onClick={onNavigate}
                  >
                    <Icon name={item.icon} size={18} />
                    <span className="nav-text">{t(item.labelKey)}</span>
                    {item.admin && !isAdmin ? (
                      <span className="nav-lock" title={t('nav.adminOnly')}>
                        <Icon name="lock" size={12} />
                        <span className="sr-only">{t('nav.adminOnly')}</span>
                      </span>
                    ) : null}
                  </NavLink>
                </li>
              ))}
            </ul>
          </div>
        ))}
      </nav>
      <div className="sidebar-footer">
        <span className="sidebar-version">{t('app.versionLabel', { version: (principal && principal.Version) || APP_VERSION })}</span>
        <span className="badge badge-warning badge-xs">{t('app.alpha')}</span>
      </div>
    </aside>
  );
}

export default Sidebar;
