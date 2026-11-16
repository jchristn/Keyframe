import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import ApiClient, { ApiError, DEFAULT_SERVER_URL, normalizeBaseUrl } from '../utils/api';
import { STORAGE_KEYS, readStorage, removeStorage, writeStorage } from '../utils/storage';
import i18n from '../i18n';

const AuthContext = createContext(null);

/**
 * Credential handling (FRONTEND_ARCHITECTURE.md, AuthContext pattern):
 * - The server URL is always remembered in localStorage.
 * - The key lives in this context. It is written to localStorage only when the operator
 *   ticks "Remember key on this device", and it is removed on logout, on a rejected key,
 *   and when the operator signs in without "remember".
 * - The key is never logged, never put into request snippets, and never stored in API
 *   Explorer history.
 */
export function AuthProvider({ children }) {
  const [serverUrl, setServerUrl] = useState(() => readStorage(STORAGE_KEYS.serverUrl, DEFAULT_SERVER_URL) || DEFAULT_SERVER_URL);
  const [apiKey, setApiKey] = useState(null);
  const [principal, setPrincipal] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [restoreError, setRestoreError] = useState(null);
  const [hasSavedKey, setHasSavedKey] = useState(() => !!readStorage(STORAGE_KEYS.apiKey, null));

  const [language, setLanguage] = useState(i18n.language);
  useEffect(() => {
    const onChange = (lng) => setLanguage(lng);
    i18n.on('languageChanged', onChange);
    return () => i18n.off('languageChanged', onChange);
  }, []);

  // Accept-Language follows the selected locale so server-authored text can be localized.
  const apiClient = useMemo(
    () => (principal ? new ApiClient(serverUrl, apiKey, { locale: language }) : null),
    [principal, serverUrl, apiKey, language]
  );

  const validate = useCallback(async (url, key) => {
    const client = new ApiClient(url, key);
    const who = await client.whoAmI();
    if (!who || (!who.IsAuthenticated && who.AuthenticationRequired)) {
      throw new ApiError({ status: 401, code: 'AuthenticationFailed', message: 'The key is not valid.' });
    }
    return who;
  }, []);

  const restore = useCallback(async () => {
    const savedUrl = readStorage(STORAGE_KEYS.serverUrl, DEFAULT_SERVER_URL) || DEFAULT_SERVER_URL;
    const savedKey = readStorage(STORAGE_KEYS.apiKey, null);
    if (!savedKey) {
      setIsLoading(false);
      return;
    }
    setIsLoading(true);
    setRestoreError(null);
    try {
      const who = await validate(savedUrl, savedKey);
      setServerUrl(savedUrl);
      setApiKey(savedKey);
      setPrincipal(who);
    } catch (err) {
      if (err instanceof ApiError && (err.status === 401 || err.status === 403)) {
        removeStorage(STORAGE_KEYS.apiKey);
        setHasSavedKey(false);
        setRestoreError('login.errors.savedKeyRejected');
      } else {
        setRestoreError('login.errors.restoreUnreachable');
      }
    } finally {
      setIsLoading(false);
    }
  }, [validate]);

  useEffect(() => {
    restore();
  }, [restore]);

  const login = useCallback(
    async (url, key, remember) => {
      const normalized = normalizeBaseUrl(url);
      const who = await validate(normalized, key || null);
      writeStorage(STORAGE_KEYS.serverUrl, normalized);
      if (remember && key) {
        writeStorage(STORAGE_KEYS.apiKey, key);
        setHasSavedKey(true);
      } else {
        removeStorage(STORAGE_KEYS.apiKey);
        setHasSavedKey(false);
      }
      setRestoreError(null);
      setServerUrl(normalized);
      setApiKey(key || null);
      setPrincipal(who);
      return who;
    },
    [validate]
  );

  const logout = useCallback((reason = null) => {
    removeStorage(STORAGE_KEYS.apiKey);
    setHasSavedKey(false);
    setApiKey(null);
    setPrincipal(null);
    setRestoreError(reason);
  }, []);

  /** Re-read whoami (for example after the server restarts). */
  const refreshPrincipal = useCallback(async () => {
    const who = await validate(serverUrl, apiKey);
    setPrincipal(who);
    return who;
  }, [validate, serverUrl, apiKey]);

  useEffect(() => {
    const onUnauthorized = () => {
      if (principal) logout('login.errors.sessionExpired');
    };
    window.addEventListener('keyframe:unauthorized', onUnauthorized);
    return () => window.removeEventListener('keyframe:unauthorized', onUnauthorized);
  }, [principal, logout]);

  const value = useMemo(
    () => ({
      isAuthenticated: !!principal,
      isLoading,
      isAdmin: !!(principal && principal.IsAdmin),
      principal,
      serverUrl,
      apiClient,
      restoreError,
      hasSavedKey,
      login,
      logout,
      restore,
      refreshPrincipal
    }),
    [principal, isLoading, serverUrl, apiClient, restoreError, hasSavedKey, login, logout, restore, refreshPrincipal]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider');
  return ctx;
}

/** For tests: provide a fixed auth value without the restore flow. */
export function TestAuthProvider({ value, children }) {
  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export default AuthContext;
