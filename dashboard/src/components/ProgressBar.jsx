import { useLocale } from '../i18n';
import { formatPercent } from '../i18n/formatters';

/** Determinate progress bar with an accessible value and a stage label. */
function ProgressBar({ percent, label = null, tone = 'accent', compact = false }) {
  const { locale } = useLocale();
  const value = Math.max(0, Math.min(100, Number(percent) || 0));
  return (
    <div className={'progress' + (compact ? ' progress-compact' : '')}>
      <div className="progress-track" role="progressbar" aria-valuemin={0} aria-valuemax={100} aria-valuenow={Math.round(value)} aria-label={label || undefined}>
        <div className={'progress-fill tone-' + tone} style={{ width: value + '%' }} />
      </div>
      <span className="progress-text">
        {formatPercent(value / 100, locale, 0)}
        {label ? <span className="muted"> · {label}</span> : null}
      </span>
    </div>
  );
}

export default ProgressBar;
