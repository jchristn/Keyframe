// i18n-aware render helper: wraps a component in the router, a fixed auth value, and toasts.
import { render } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { TestAuthProvider } from '../context/AuthContext';
import { ToastProvider } from '../context/ToastContext';
import { ThemeProvider } from '../context/ThemeContext';

export function makeAuth(overrides = {}) {
  return {
    isAuthenticated: true,
    isLoading: false,
    isAdmin: true,
    principal: { IsAuthenticated: true, IsAdmin: true, PrincipalName: 'admin', AuthenticationRequired: true, Version: '0.1.0' },
    serverUrl: 'http://127.0.0.1:8700',
    apiClient: null,
    restoreError: null,
    hasSavedKey: false,
    login: async () => {},
    logout: () => {},
    restore: async () => {},
    refreshPrincipal: async () => {},
    ...overrides
  };
}

export function renderWithProviders(ui, { auth = makeAuth(), route = '/', path = '*' } = {}) {
  return render(
    <ThemeProvider>
      <ToastProvider>
        <TestAuthProvider value={auth}>
          <MemoryRouter initialEntries={[route]}>
            <Routes>
              <Route path={path} element={ui} />
            </Routes>
          </MemoryRouter>
        </TestAuthProvider>
      </ToastProvider>
    </ThemeProvider>
  );
}
