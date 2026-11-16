import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import CopyButton from './CopyButton';
import Modal from './Modal';
import { downloadJson } from '../utils/media';
import Icon from './Icon';

/** Lightweight JSON syntax highlighting: escapes HTML, then wraps tokens in spans. */
export function highlightJson(text) {
  const escaped = text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  return escaped.replace(
    /("(\\u[a-fA-F0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+-]?\d+)?)/g,
    (match) => {
      let cls = 'json-number';
      if (/^"/.test(match)) cls = /:$/.test(match) ? 'json-key' : 'json-string';
      else if (/true|false/.test(match)) cls = 'json-bool';
      else if (/null/.test(match)) cls = 'json-null';
      return '<span class="' + cls + '">' + match + '</span>';
    }
  );
}

/** Pretty-print a value (or a JSON string) when possible; otherwise return the raw text. */
export function toPrettyText(value) {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') {
    try {
      return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
      return value;
    }
  }
  return JSON.stringify(value, null, 2);
}

/** Read-only code block for JSON or text with copy (and optional download). */
function JsonViewer({ value, maxHeight = 480, filename = null, label = null, emptyText = null }) {
  const { t } = useTranslation();
  const text = useMemo(() => toPrettyText(value), [value]);
  const isJson = useMemo(() => {
    if (typeof value !== 'string') return value !== null && value !== undefined;
    try {
      JSON.parse(value);
      return true;
    } catch {
      return false;
    }
  }, [value]);

  if (!text) return <div className="muted code-empty">{emptyText || t('common.empty')}</div>;

  return (
    <div className="code-block">
      <div className="code-toolbar">
        {label ? <span className="code-label">{label}</span> : <span />}
        <span className="code-actions">
          {filename && isJson ? (
            <button
              type="button"
              className="icon-button"
              onClick={() => downloadJson(typeof value === 'string' ? JSON.parse(value) : value, filename)}
              aria-label={t('common.download')}
              title={t('common.download')}
            >
              <Icon name="download" />
            </button>
          ) : null}
          <CopyButton value={text} />
        </span>
      </div>
      <pre className="code" style={{ maxHeight }} tabIndex={0}>
        {isJson ? <code dangerouslySetInnerHTML={{ __html: highlightJson(text) }} /> : <code>{text}</code>}
      </pre>
    </div>
  );
}

/** Modal wrapper used by row "View JSON" actions. */
export function JsonViewerModal({ open, onClose, title, value, subtitle = null, filename = null }) {
  return (
    <Modal open={open} onClose={onClose} title={title} subtitle={subtitle} size="xl">
      <JsonViewer value={value} maxHeight="70vh" filename={filename} />
    </Modal>
  );
}

export default JsonViewer;
