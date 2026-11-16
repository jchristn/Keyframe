import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { applyAppearance, readAppearance, resolveMode } from '../theme';

const ThemeContext = createContext({
  palette: 'slate',
  mode: 'system',
  theme: 'light',
  setPalette: () => {},
  setMode: () => {},
  toggleTheme: () => {}
});

/**
 * Appearance state: palette id, mode (light, dark, or system), and the resolved theme.
 * Changes apply immediately to <html data-palette data-theme> and persist per browser.
 * In system mode the resolved theme follows the operating system preference live.
 */
export function ThemeProvider({ children }) {
  const [appearance, setAppearance] = useState(readAppearance);
  const [systemTick, setSystemTick] = useState(0);
  const theme = useMemo(() => resolveMode(appearance.mode), [appearance.mode, systemTick]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    applyAppearance(appearance);
  }, [appearance, systemTick]);

  useEffect(() => {
    if (appearance.mode !== 'system' || typeof window === 'undefined' || !window.matchMedia) return undefined;
    const mq = window.matchMedia('(prefers-color-scheme: dark)');
    const onChange = () => setSystemTick((n) => n + 1);
    if (mq.addEventListener) mq.addEventListener('change', onChange);
    return () => mq.removeEventListener && mq.removeEventListener('change', onChange);
  }, [appearance.mode]);

  const setPalette = useCallback((palette) => setAppearance((a) => ({ ...a, palette })), []);
  const setMode = useCallback((mode) => setAppearance((a) => ({ ...a, mode })), []);
  const toggleTheme = useCallback(() => setAppearance((a) => ({ ...a, mode: resolveMode(a.mode) === 'dark' ? 'light' : 'dark' })), []);

  const value = useMemo(
    () => ({ palette: appearance.palette, mode: appearance.mode, theme, setPalette, setMode, toggleTheme }),
    [appearance, theme, setPalette, setMode, toggleTheme]
  );
  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}

export function useTheme() {
  return useContext(ThemeContext);
}
