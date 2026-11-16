import { useEffect, useState } from 'react';
import { useLocale } from '../i18n';
import { formatBytes, formatDateTime, formatDuration } from '../i18n/formatters';
import Modal from './Modal';
import JsonViewer from './JsonViewer';
import CopyableId from './CopyableId';
import CopyButton from './CopyButton';
import StatusBadge, { MethodBadge } from './StatusBadge';
import Icon from './Icon';
import { ErrorBanner, LoadingState } from './States';

function Section({ title, copyValue = null, defaultOpen = true, children }) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <section className="inspector-section">
      <div className="inspector-section-header">
        <button type="button" className="inspector-toggle" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
          <Icon name={open ? 'chevronDown' : 'chevronRight'} />
          <span>{title}</span>
        </button>
        {copyValue !== null ? <CopyButton value={copyValue} /> : null}
      </div>
      {open ? <div className="inspector-section-body">{children}</div> : null}
    </section>
  );
}

function HeadersTable({ headers }) {
  const { t } = useLocale();
  const entries = Object.entries(headers || {});
  if (!entries.length) return <div className="muted">{t('requestHistory.inspector.noHeaders')}</div>;
  return (
    <table className="kv-table">
      <tbody>
        {entries.map(([k, v]) => (
          <tr key={k}>
            <th scope="row" className="mono">
              {k}
            </th>
            <td className="mono break-all">{v}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function BodyBlock({ body, bytes, truncated, contentType }) {
  const { t, locale } = useLocale();
  return (
    <>
      <div className="muted body-meta">
        {contentType ? <span className="mono">{contentType}</span> : null}
        <span>{formatBytes(bytes || 0, locale)}</span>
        {truncated ? <span className="badge badge-warning badge-xs">{t('requestHistory.inspector.truncated', { size: formatBytes(bytes || 0, locale) })}</span> : null}
      </div>
      <JsonViewer value={body} maxHeight={320} emptyText={t('requestHistory.inspector.noBody')} />
    </>
  );
}

/** Request inspector: metadata, headers, bodies, and raw JSON, each collapsible and copyable. */
function RequestDetailsModal({ entryId, apiClient, onClose, onDelete = null }) {
  const { t, locale } = useLocale();
  const [entry, setEntry] = useState(null);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(false);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (!entryId) return undefined;
    let current = true;
    setLoading(true);
    setError(null);
    setEntry(null);
    apiClient
      .getRequestHistoryEntry(entryId)
      .then((e) => current && setEntry(e))
      .catch((err) => current && setError(err))
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
  }, [entryId, apiClient, attempt]);

  const e = entry;
  return (
    <Modal
      open={!!entryId}
      onClose={onClose}
      size="xl"
      title={t('requestHistory.inspector.title')}
      subtitle={e ? e.Method + ' ' + e.Path : null}
      headerExtra={<CopyableId value={entryId} />}
      footer={
        onDelete && e ? (
          <button type="button" className="button button-danger" onClick={() => onDelete(e)}>
            <Icon name="trash" />
            {t('common.delete')}
          </button>
        ) : null
      }
    >
      {loading ? <LoadingState /> : null}
      {error ? <ErrorBanner error={error} onRetry={() => setAttempt((a) => a + 1)} /> : null}
      {e ? (
        <div className="inspector">
          <Section title={t('requestHistory.inspector.metadata')}>
            <dl className="kv-grid">
              <div>
                <dt>{t('requestHistory.columns.method')}</dt>
                <dd>
                  <MethodBadge method={e.Method} />
                </dd>
              </div>
              <div>
                <dt>{t('requestHistory.columns.status')}</dt>
                <dd>
                  <StatusBadge kind="http" value={e.StatusCode} />
                </dd>
              </div>
              <div>
                <dt>{t('requestHistory.columns.duration')}</dt>
                <dd>{formatDuration(e.DurationMs, locale)}</dd>
              </div>
              <div className="kv-grid-wide">
                <dt>{t('requestHistory.inspector.url')}</dt>
                <dd className="mono break-all">
                  {e.Url} <CopyButton value={e.Url} />
                </dd>
              </div>
              <div>
                <dt>{t('requestHistory.inspector.requestId')}</dt>
                <dd>
                  <CopyableId value={e.RequestId} />
                </dd>
              </div>
              <div>
                <dt>{t('requestHistory.columns.principal')}</dt>
                <dd>{e.PrincipalName || t('common.none')}</dd>
              </div>
              <div>
                <dt>{t('requestHistory.columns.sourceIp')}</dt>
                <dd className="mono">{e.SourceIp || t('common.none')}</dd>
              </div>
              <div>
                <dt>{t('requestHistory.inspector.created')}</dt>
                <dd>{formatDateTime(e.CreatedUtc, locale)}</dd>
              </div>
              <div>
                <dt>{t('requestHistory.inspector.completed')}</dt>
                <dd>{e.CompletedUtc ? formatDateTime(e.CompletedUtc, locale) : t('common.notYet')}</dd>
              </div>
            </dl>
          </Section>
          <Section title={t('requestHistory.inspector.requestHeaders')} copyValue={JSON.stringify(e.RequestHeaders || {}, null, 2)}>
            <HeadersTable headers={e.RequestHeaders} />
          </Section>
          <Section title={t('requestHistory.inspector.requestBody')} copyValue={e.RequestBody || ''}>
            <BodyBlock body={e.RequestBody} bytes={e.RequestBodyBytes} truncated={e.RequestBodyTruncated} contentType={e.RequestContentType} />
          </Section>
          <Section title={t('requestHistory.inspector.responseHeaders')} copyValue={JSON.stringify(e.ResponseHeaders || {}, null, 2)}>
            <HeadersTable headers={e.ResponseHeaders} />
          </Section>
          <Section title={t('requestHistory.inspector.responseBody')} copyValue={e.ResponseBody || ''}>
            <BodyBlock body={e.ResponseBody} bytes={e.ResponseBodyBytes} truncated={e.ResponseBodyTruncated} contentType={e.ResponseContentType} />
          </Section>
          <Section title={t('requestHistory.inspector.raw')} defaultOpen={false}>
            <JsonViewer value={e} maxHeight={400} filename={e.Id + '.json'} />
          </Section>
        </div>
      ) : null}
    </Modal>
  );
}

export default RequestDetailsModal;
