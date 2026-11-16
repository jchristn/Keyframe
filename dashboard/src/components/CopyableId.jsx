import { useTranslation } from 'react-i18next';
import CopyButton from './CopyButton';

/** Monospace, non-wrapping identifier with a copy control. Long values truncate with a tooltip. */
function CopyableId({ value, label = null, truncate = false }) {
  const { t } = useTranslation();
  if (!value) return <span className="muted">{t('common.none')}</span>;
  return (
    <span className={'copyable-id' + (truncate ? ' copyable-id-truncate' : '')} data-no-row-click>
      <code className="mono" title={value}>
        {value}
      </code>
      <CopyButton value={value} label={label || t('common.copyId')} />
    </span>
  );
}

export default CopyableId;
