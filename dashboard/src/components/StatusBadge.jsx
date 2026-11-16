import { useTranslation } from 'react-i18next';

const JOB_TONES = {
  Queued: 'info',
  Running: 'accent',
  Succeeded: 'success',
  Failed: 'danger',
  Canceled: 'neutral',
  Interrupted: 'warning'
};

/**
 * Status pill. Color is paired with a text label (and a dot) so meaning never relies on
 * color alone. `kind` picks the label namespace and tone mapping:
 * job (JobStatusEnum), health (true/false), http (status code), bool, valid, or custom.
 */
function StatusBadge({ kind = 'custom', value, tone = null, label = null }) {
  const { t } = useTranslation();
  let resolvedTone = tone || 'neutral';
  let text = label;

  if (kind === 'job') {
    resolvedTone = JOB_TONES[value] || 'neutral';
    text = text || t('enums.jobStatus.' + value, { defaultValue: String(value) });
  } else if (kind === 'health') {
    resolvedTone = value ? 'success' : 'danger';
    text = text || (value ? t('status.healthy') : t('status.unhealthy'));
  } else if (kind === 'http') {
    const code = Number(value);
    resolvedTone = code >= 500 ? 'danger' : code >= 400 ? 'warning' : code >= 300 ? 'info' : 'success';
    text = text || String(value);
  } else if (kind === 'bool') {
    resolvedTone = value ? 'success' : 'neutral';
    text = text || (value ? t('common.yes') : t('common.no'));
  } else if (kind === 'valid') {
    resolvedTone = value ? 'success' : 'danger';
    text = text || (value ? t('status.valid') : t('status.invalid'));
  } else if (kind === 'active') {
    resolvedTone = value ? 'success' : 'neutral';
    text = text || (value ? t('status.active') : t('status.inactive'));
  }

  return (
    <span className={'badge badge-' + resolvedTone}>
      <span className="badge-dot" aria-hidden="true" />
      {text ?? String(value)}
    </span>
  );
}

/** HTTP method pill. */
export function MethodBadge({ method }) {
  const m = String(method || '').toUpperCase();
  return <span className={'method method-' + m.toLowerCase()}>{m}</span>;
}

export default StatusBadge;
