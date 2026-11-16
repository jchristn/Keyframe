import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';

async function writeClipboard(text) {
  if (navigator.clipboard && window.isSecureContext) {
    await navigator.clipboard.writeText(text);
    return;
  }
  // Fallback for plain-http origins (for example http://127.0.0.1:8701 opened by IP).
  const area = document.createElement('textarea');
  area.value = text;
  area.setAttribute('readonly', '');
  area.style.position = 'fixed';
  area.style.opacity = '0';
  document.body.appendChild(area);
  area.select();
  document.execCommand('copy');
  area.remove();
}

/** Copy control with a brief checkmark success state and no layout shift. */
function CopyButton({ value, label = null, className = '', showText = false }) {
  const { t } = useTranslation();
  const [copied, setCopied] = useState(false);
  const timer = useRef(null);
  useEffect(() => () => clearTimeout(timer.current), []);

  const text = typeof value === 'function' ? null : value;
  const aria = copied ? t('common.copied') : label || t('common.copy');

  const handle = async (e) => {
    e.stopPropagation();
    try {
      await writeClipboard(typeof value === 'function' ? value() : String(text ?? ''));
      setCopied(true);
      clearTimeout(timer.current);
      timer.current = setTimeout(() => setCopied(false), 1500);
    } catch {
      setCopied(false);
    }
  };

  return (
    <button
      type="button"
      className={(showText ? 'button button-secondary button-sm ' : 'icon-button copy-button ') + className}
      onClick={handle}
      aria-label={aria}
      title={aria}
      data-no-row-click
    >
      <Icon name={copied ? 'check' : 'copy'} className={copied ? 'icon-success' : ''} />
      {showText ? <span>{copied ? t('common.copied') : label || t('common.copy')}</span> : null}
    </button>
  );
}

export default CopyButton;
