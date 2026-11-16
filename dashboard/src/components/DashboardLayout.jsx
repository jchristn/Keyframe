import { createContext, useCallback, useContext, useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import Sidebar from './Sidebar';
import Topbar from './Topbar';
import { useAuth } from '../context/AuthContext';
import useInterval from '../hooks/useInterval';
import usePersistedState from '../hooks/usePersistedState';
import { STORAGE_KEYS } from '../utils/storage';

const HealthContext = createContext({ health: null, healthError: null, refreshHealth: () => {} });

export function useHealth() {
  return useContext(HealthContext);
}

const HEALTH_POLL_MS = 30000;

/** Authenticated shell: sidebar, topbar, and the routed workspace. Polls health. */
function DashboardLayout() {
  const { apiClient } = useAuth();
  const location = useLocation();
  const [collapsed, setCollapsed] = usePersistedState(STORAGE_KEYS.sidebarCollapsed, false);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [health, setHealth] = useState(null);
  const [healthError, setHealthError] = useState(null);

  const refreshHealth = useCallback(async () => {
    if (!apiClient) return null;
    try {
      const h = await apiClient.getHealth();
      setHealth(h);
      setHealthError(null);
      return h;
    } catch (err) {
      setHealthError(err);
      return null;
    }
  }, [apiClient]);

  useEffect(() => {
    refreshHealth();
  }, [refreshHealth]);
  useInterval(refreshHealth, HEALTH_POLL_MS);

  useEffect(() => setMobileOpen(false), [location.pathname]);

  const toggle = () => {
    if (window.matchMedia && window.matchMedia('(max-width: 900px)').matches) setMobileOpen((o) => !o);
    else setCollapsed((c) => !c);
  };

  return (
    <HealthContext.Provider value={{ health, healthError, refreshHealth }}>
      <div className={'shell' + (collapsed ? ' sidebar-collapsed' : '')}>
        <Sidebar collapsed={collapsed} mobileOpen={mobileOpen} onNavigate={() => setMobileOpen(false)} />
        {mobileOpen ? <div className="sidebar-scrim" onClick={() => setMobileOpen(false)} aria-hidden="true" /> : null}
        <div className="shell-main">
          <Topbar onToggleSidebar={toggle} health={health} healthError={healthError} />
          <main className="workspace" id="main">
            <Outlet />
          </main>
        </div>
      </div>
    </HealthContext.Provider>
  );
}

export default DashboardLayout;
