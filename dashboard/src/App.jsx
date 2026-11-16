import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { AuthProvider, useAuth } from './context/AuthContext';
import { ThemeProvider } from './context/ThemeContext';
import { ToastProvider } from './context/ToastContext';
import DashboardLayout from './components/DashboardLayout';
import { LoadingState } from './components/States';
import LoginView from './views/LoginView';
import HomeView from './views/HomeView';
import AnalyzeImageView from './views/AnalyzeImageView';
import AnalyzeVideoView from './views/AnalyzeVideoView';
import AnalyzeAudioView from './views/AnalyzeAudioView';
import AnalyticsView from './views/AnalyticsView';
import JobsView from './views/JobsView';
import JobDetailView from './views/JobDetailView';
import EndpointsView from './views/EndpointsView';
import RequestHistoryView from './views/RequestHistoryView';
import ApiExplorerView from './views/ApiExplorerView';
import SettingsView from './views/SettingsView';
import ServerInfoView from './views/ServerInfoView';
import NotFoundView from './views/NotFoundView';

function FullPageLoading() {
  const { t } = useTranslation();
  return (
    <div className="app-loading">
      <LoadingState label={t('login.restoring')} />
    </div>
  );
}

/** Requires an authenticated session; otherwise redirects to /login, remembering the target. */
export function PrivateRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  const location = useLocation();
  if (isLoading) return <FullPageLoading />;
  return isAuthenticated ? children : <Navigate to="/login" state={{ from: location }} replace />;
}

function PublicRoute({ children }) {
  const { isAuthenticated, isLoading } = useAuth();
  const location = useLocation();
  if (isLoading) return <FullPageLoading />;
  if (!isAuthenticated) return children;
  const from = location.state && location.state.from;
  const target = from && from.pathname && from.pathname.startsWith('/dashboard') ? from.pathname + (from.search || '') : '/dashboard/home';
  return <Navigate to={target} replace />;
}

export function AppRoutes() {
  return (
    <Routes>
      <Route
        path="/login"
        element={
          <PublicRoute>
            <LoginView />
          </PublicRoute>
        }
      />
      <Route
        path="/dashboard"
        element={
          <PrivateRoute>
            <DashboardLayout />
          </PrivateRoute>
        }
      >
        <Route index element={<Navigate to="home" replace />} />
        <Route path="home" element={<HomeView />} />
        <Route path="analyze" element={<Navigate to="image" replace />} />
        <Route path="analyze/image" element={<AnalyzeImageView />} />
        <Route path="analyze/video" element={<AnalyzeVideoView />} />
        <Route path="analyze/audio" element={<AnalyzeAudioView />} />
        <Route path="analytics" element={<AnalyticsView />} />
        <Route path="jobs" element={<JobsView />} />
        <Route path="jobs/:id" element={<JobDetailView />} />
        <Route path="endpoints" element={<EndpointsView />} />
        <Route path="request-history" element={<RequestHistoryView />} />
        <Route path="api-explorer" element={<ApiExplorerView />} />
        <Route path="settings" element={<SettingsView />} />
        <Route path="server" element={<ServerInfoView />} />
        <Route path="*" element={<NotFoundView />} />
      </Route>
      <Route path="/" element={<Navigate to="/dashboard/home" replace />} />
      <Route path="*" element={<Navigate to="/dashboard/home" replace />} />
    </Routes>
  );
}

function App() {
  return (
    <ThemeProvider>
      <ToastProvider>
        <AuthProvider>
          <BrowserRouter>
            <AppRoutes />
          </BrowserRouter>
        </AuthProvider>
      </ToastProvider>
    </ThemeProvider>
  );
}

export default App;
