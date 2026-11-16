import { createContext, useCallback, useContext, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from '../components/Icon';

const ToastContext = createContext({ notify: () => {} });

let toastId = 0;

/**
 * Toast notifications. Toasts store semantic payloads (an i18n key plus params) and are
 * localized at render time, so a locale change re-renders them correctly.
 */
export function ToastProvider({ children }) {
  const { t } = useTranslation();
  const [toasts, setToasts] = useState([]);

  const dismiss = useCallback((id) => setToasts((list) => list.filter((x) => x.id !== id)), []);

  const notify = useCallback(
    (kind, key, params = {}, detail = null) => {
      toastId += 1;
      const id = toastId;
      setToasts((list) => [...list.slice(-4), { id, kind, key, params, detail }]);
      setTimeout(() => dismiss(id), kind === 'error' ? 8000 : 4000);
    },
    [dismiss]
  );

  const value = useMemo(() => ({ notify }), [notify]);

  return (
    <ToastContext.Provider value={value}>
      {children}
      <div className="toast-region" role="status" aria-live="polite">
        {toasts.map((toast) => (
          <div key={toast.id} className={'toast toast-' + toast.kind}>
            <Icon name={toast.kind === 'error' ? 'alert' : toast.kind === 'success' ? 'check' : 'info'} />
            <div className="toast-body">
              <div>{t(toast.key, toast.params)}</div>
              {toast.detail ? <div className="toast-detail">{toast.detail}</div> : null}
            </div>
            <button type="button" className="icon-button" onClick={() => dismiss(toast.id)} aria-label={t('common.dismiss')} title={t('common.dismiss')}>
              <Icon name="close" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  return useContext(ToastContext);
}
