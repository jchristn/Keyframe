import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { errorHeadlineKey, errorHintKey } from '../utils/errors';

/** Centered loading indicator that reserves space so layout does not jump. */
export function LoadingState({ label = null, compact = false }) {
  const { t } = useTranslation();
  return (
    <div className={'state state-loading' + (compact ? ' state-compact' : '')} role="status">
      <span className="spinner" aria-hidden="true" />
      <span>{label || t('common.loading')}</span>
    </div>
  );
}

/** Page or panel empty state with a title, guidance, and optional actions. */
export function EmptyState({ icon = 'info', title, message = null, children = null, compact = false }) {
  return (
    <div className={'state state-empty' + (compact ? ' state-compact' : '')}>
      <Icon name={icon} size={compact ? 18 : 28} className="state-icon" />
      <div className="state-title">{title}</div>
      {message ? <div className="state-message">{message}</div> : null}
      {children ? <div className="state-actions">{children}</div> : null}
    </div>
  );
}

/**
 * Error banner for an ApiError (or any Error): localized headline, server message and
 * details, request id, retry guidance, an optional Retry button, and dismiss.
 */
export function ErrorBanner({ error, onRetry = null, dismissible = true, titleKey = null }) {
  const { t } = useTranslation();
  const [dismissed, setDismissed] = useState(null);
  if (!error || dismissed === error) return null;
  const hint = errorHintKey(error);
  const serverMessage = !error.network && error.message ? error.message : null;
  return (
    <div className="banner banner-danger" role="alert">
      <Icon name="alert" />
      <div className="banner-body">
        <div className="banner-title">{t(titleKey || errorHeadlineKey(error), { status: error.status })}</div>
        {serverMessage ? <div>{serverMessage}</div> : null}
        {error.details && error.details.length ? (
          <ul className="banner-list">
            {error.details.map((d, i) => (
              <li key={i}>{d}</li>
            ))}
          </ul>
        ) : null}
        {hint ? <div className="banner-hint">{t(hint)}</div> : null}
        {error.requestId ? (
          <div className="banner-meta">
            {t('errors.requestId')}: <code className="mono">{error.requestId}</code>
          </div>
        ) : null}
      </div>
      <div className="banner-actions">
        {onRetry ? (
          <button type="button" className="button button-secondary button-sm" onClick={onRetry}>
            <Icon name="refresh" />
            {t('common.retry')}
          </button>
        ) : null}
        {dismissible ? (
          <button type="button" className="icon-button" onClick={() => setDismissed(error)} aria-label={t('common.dismiss')} title={t('common.dismiss')}>
            <Icon name="close" />
          </button>
        ) : null}
      </div>
    </div>
  );
}

/** Neutral or warning banner for non-error notices. */
export function Notice({ tone = 'info', icon = 'info', title = null, children, actions = null }) {
  return (
    <div className={'banner banner-' + tone} role={tone === 'warning' ? 'alert' : 'status'}>
      <Icon name={icon} />
      <div className="banner-body">
        {title ? <div className="banner-title">{title}</div> : null}
        {children}
      </div>
      {actions ? <div className="banner-actions">{actions}</div> : null}
    </div>
  );
}
