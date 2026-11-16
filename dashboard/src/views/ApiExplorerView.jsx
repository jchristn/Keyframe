import { useId, useMemo, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useLocale } from '../i18n';
import { formatBytes, formatDuration, formatList, formatRelativeTime } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import { MethodBadge } from '../components/StatusBadge';
import StatusBadge from '../components/StatusBadge';
import Tabs from '../components/Tabs';
import JsonViewer from '../components/JsonViewer';
import CopyButton from '../components/CopyButton';
import ConfirmModal from '../components/ConfirmModal';
import Icon from '../components/Icon';
import { EmptyState, ErrorBanner, LoadingState, Notice } from '../components/States';
import { SnippetPanel } from '../components/AnalysisParts';
import OperationPicker from '../components/OperationPicker';
import useApiExplorer from '../hooks/useApiExplorer';
import { groupByTag, isDestructive } from '../utils/openApi';
import { downloadBlob } from '../utils/media';

function ParamInputs({ params, values, onChange, kind }) {
  const { t } = useLocale();
  const base = useId();
  if (!params.length) return null;
  return (
    <section className="panel explorer-section" aria-label={t('explorer.' + kind + 'Params')}>
      <h3 className="panel-title">{t('explorer.' + kind + 'Params')}</h3>
      <div className="explorer-param-grid">
      {params.map((p) => {
        const schema = p.schema || {};
        return (
          <div key={p.name} className="form-field">
            <label htmlFor={base + p.name} className="field-label">
              <span className="mono">{p.name}</span>
              {p.required ? (
                <span className="required-mark" title={t('explorer.required')}>
                  *
                </span>
              ) : null}
              {schema.type ? <span className="muted"> {schema.format || schema.type}</span> : null}
            </label>
            {Array.isArray(schema.enum) ? (
              <select id={base + p.name} value={values[p.name] || ''} onChange={(e) => onChange({ ...values, [p.name]: e.target.value })}>
                <option value="">{t('common.any')}</option>
                {schema.enum.map((v) => (
                  <option key={v} value={v}>
                    {v}
                  </option>
                ))}
              </select>
            ) : (
              <input
                id={base + p.name}
                className="mono"
                value={values[p.name] || ''}
                onChange={(e) => onChange({ ...values, [p.name]: e.target.value })}
                aria-required={p.required || undefined}
                placeholder={p.description || ''}
              />
            )}
            {p.description ? <p className="help-text">{p.description}</p> : null}
          </div>
        );
      })}
      </div>
    </section>
  );
}

function HeaderEditor({ headers, onChange }) {
  const { t } = useLocale();
  return (
    <section className="panel explorer-section" aria-label={t('explorer.headers')}>
      <h3 className="panel-title">{t('explorer.headers')}</h3>
      <p className="help-text">{t('explorer.authInherited')}</p>
      {headers.map((h, i) => (
        <div key={i} className="header-row">
          <input className="mono" value={h.name} onChange={(e) => onChange(headers.map((x, j) => (j === i ? { ...x, name: e.target.value } : x)))} placeholder={t('explorer.headerName')} aria-label={t('explorer.headerName')} />
          <input className="mono" value={h.value} onChange={(e) => onChange(headers.map((x, j) => (j === i ? { ...x, value: e.target.value } : x)))} placeholder={t('explorer.headerValue')} aria-label={t('explorer.headerValue')} />
          <button type="button" className="icon-button danger" onClick={() => onChange(headers.filter((_, j) => j !== i))} aria-label={t('explorer.removeHeader')} title={t('explorer.removeHeader')}>
            <Icon name="trash" />
          </button>
        </div>
      ))}
      <div>
        <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...headers, { name: '', value: '' }])}>
          <Icon name="plus" />
          {t('explorer.addHeader')}
        </button>
      </div>
    </section>
  );
}

/** OpenAPI-driven playground: operation picker, request builder, response tabs, history. */
function ApiExplorerView() {
  const { t, locale } = useLocale();
  const { apiClient } = useAuth();
  const ex = useApiExplorer(apiClient);
  const bodyId = useId();
  const [respTab, setRespTab] = useState('body');
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);

  const groups = useMemo(() => groupByTag(ex.operations, ex.spec), [ex.operations, ex.spec]);

  const op = ex.operation;
  const pathParams = op ? op.parameters.filter((p) => p.in === 'path') : [];
  const queryParams = op ? op.parameters.filter((p) => p.in === 'query') : [];
  const canRun = op && !ex.running && !ex.missingPath.length && !ex.bodyError;

  const run = () => {
    if (!canRun) return;
    if (isDestructive(op.method, op.path)) setConfirmOpen(true);
    else ex.execute();
  };

  let parsedBody = null;
  if (ex.response && ex.response.body) {
    try {
      parsedBody = JSON.parse(ex.response.body);
    } catch {
      parsedBody = null;
    }
  }

  return (
    <div className="view">
      <PageHeader
        title={t('explorer.title')}
        summary={t('explorer.summary')}
        actions={
          <button type="button" className="button button-secondary" onClick={() => setHistoryOpen((o) => !o)} aria-expanded={historyOpen}>
            <Icon name="history" />
            {t('explorer.history', { count: ex.history.length })}
          </button>
        }
      />
      {ex.specLoading ? <LoadingState label={t('explorer.loadingSpec')} /> : null}
      {ex.specError ? (
        <>
          <ErrorBanner error={ex.specError} onRetry={ex.loadSpec} titleKey="explorer.specMissing" />
          <EmptyState icon="explorer" title={t('explorer.specMissing')} message={t('explorer.specMissingMessage')} />
        </>
      ) : null}
      {ex.specViaProxy ? <Notice tone="warning" icon="alert" title={t('explorer.viaProxyTitle')}>{t('explorer.viaProxyMessage')}</Notice> : null}
      {historyOpen ? (
        <section className="panel" aria-label={t('explorer.historyTitle')}>
          <div className="panel-header">
            <h2 className="panel-title">{t('explorer.historyTitle')}</h2>
          </div>
          {ex.history.length ? (
            <ul className="history-list">
              {ex.history.map((h) => (
                <li key={h.id} className="history-item">
                  <MethodBadge method={h.method} />
                  <span className="mono truncate" title={h.path}>
                    {h.path}
                  </span>
                  <StatusBadge kind="http" value={h.status} />
                  <span className="muted">{formatRelativeTime(h.atUtc, locale)}</span>
                  <span className="history-actions">
                    <button type="button" className="button button-secondary button-sm" onClick={() => ex.loadHistory(h)}>
                      {t('explorer.load')}
                    </button>
                    <button type="button" className="icon-button danger" onClick={() => ex.deleteHistory(h.id)} aria-label={t('common.delete')} title={t('common.delete')}>
                      <Icon name="trash" />
                    </button>
                  </span>
                </li>
              ))}
            </ul>
          ) : (
            <p className="muted">{t('explorer.noHistory')}</p>
          )}
        </section>
      ) : null}
      {ex.spec ? (
        <div className="explorer">
          <section className="panel explorer-picker" aria-label={t('explorer.operations')}>
            <OperationPicker groups={groups} value={op} onChange={ex.setOperationId} />
            {op ? (
              <>
                {op.description ? <p className="help-text">{op.description}</p> : null}
                <div className="resolved-url">
                  <span className="field-label">{t('explorer.resolvedUrl')}</span>
                  <code className="mono break-all">{ex.resolvedUrl}</code>
                  <CopyButton value={ex.resolvedUrl} label={t('explorer.copyUrl')} />
                </div>
              </>
            ) : null}
          </section>
          {op ? (
            <>
              <ParamInputs kind="path" params={pathParams} values={ex.pathParams} onChange={ex.setPathParams} />
              <ParamInputs kind="query" params={queryParams} values={ex.queryParams} onChange={ex.setQueryParams} />
              <HeaderEditor headers={ex.headers} onChange={ex.setHeaders} />
              {op.requestBody ? (
                <section className="panel explorer-section" aria-label={t('explorer.body')}>
                  <div className="panel-header">
                    <h3 className="panel-title">{t('explorer.body')}</h3>
                    <div className="body-toolbar">
                      <button
                        type="button"
                        className="button button-secondary button-sm"
                        onClick={() => {
                          try {
                            ex.setBody(JSON.stringify(JSON.parse(ex.body), null, 2));
                          } catch {
                            // Leave the input unchanged; the inline error explains why.
                          }
                        }}
                        disabled={!!ex.bodyError}
                      >
                        {t('explorer.format')}
                      </button>
                      <button type="button" className="button button-secondary button-sm" onClick={ex.resetBody}>
                        {t('explorer.resetTemplate')}
                      </button>
                      <CopyButton value={ex.body} label={t('explorer.copyBody')} />
                    </div>
                  </div>
                  <label htmlFor={bodyId} className="sr-only">
                    {t('explorer.body')}
                  </label>
                  <textarea id={bodyId} className="mono code-input" rows={14} value={ex.body} onChange={(e) => ex.setBody(e.target.value)} spellCheck={false} aria-invalid={!!ex.bodyError || undefined} />
                  {ex.bodyError ? (
                    <div className="field-error" role="alert">
                      {t('explorer.invalidJson', { message: ex.bodyError })}
                    </div>
                  ) : null}
                </section>
              ) : null}
              <div className="form-actions sticky-actions">
                {ex.missingPath.length ? <span className="field-error">{t('explorer.missingPath', { names: formatList(ex.missingPath, locale) })}</span> : null}
                {isDestructive(op.method, op.path) ? (
                  <span className="text-warning">
                    <Icon name="alert" /> {t('explorer.destructive')}
                  </span>
                ) : null}
                {ex.running ? (
                  <button type="button" className="button button-secondary" onClick={ex.stop}>
                    <Icon name="stop" />
                    {t('explorer.stop')}
                  </button>
                ) : null}
                <button type="button" className={'button ' + (isDestructive(op.method, op.path) ? 'button-danger' : 'button-primary')} onClick={run} disabled={!canRun}>
                  <Icon name="play" />
                  {ex.running ? t('explorer.running') : t('explorer.execute')}
                </button>
              </div>

              <section className="panel" aria-live="polite" aria-labelledby="explorer-response-title">
                <div className="panel-header">
                  <h2 id="explorer-response-title" className="panel-title">
                    {t('explorer.response')}
                  </h2>
                  {ex.response && ex.response.status ? (
                    <span className="panel-actions">
                      <StatusBadge kind="http" value={ex.response.status} label={ex.response.status + (ex.response.statusText ? ' ' + ex.response.statusText : '')} />
                      <span className="muted">{formatDuration(ex.response.durationMs, locale)}</span>
                      <span className="muted">{formatBytes(ex.response.bytes, locale)}</span>
                      {ex.response.streaming ? <span className="badge badge-info badge-xs">{t('explorer.streaming')}</span> : null}
                    </span>
                  ) : null}
                </div>
                {!ex.response && !ex.running ? <p className="muted">{t('explorer.noResponse')}</p> : null}
                {ex.running && !ex.response ? <LoadingState compact label={t('explorer.running')} /> : null}
                {ex.response && ex.response.aborted ? <p className="muted">{t('explorer.aborted')}</p> : null}
                {ex.response && ex.response.error ? <ErrorBanner error={ex.response.error} onRetry={run} /> : null}
                <Tabs
                  size="sm"
                  label={t('explorer.responseTabs')}
                  active={respTab}
                  onChange={setRespTab}
                  tabs={[
                    { id: 'body', label: t('explorer.tabs.body') },
                    { id: 'headers', label: t('explorer.tabs.headers') },
                    { id: 'code', label: t('explorer.tabs.code') }
                  ]}
                >
                  {respTab === 'body' && ex.response && ex.response.binary ? (
                    <div className="binary-response">
                      <Icon name="download" size={20} />
                      <div>
                        <div className="banner-title">{t('explorer.binaryTitle')}</div>
                        <div className="muted">{t('explorer.binaryMessage', { type: ex.response.contentType, size: formatBytes(ex.response.bytes, locale) })}</div>
                      </div>
                      <button type="button" className="button button-secondary button-sm" onClick={() => downloadBlob(ex.response.blob, ex.response.filename || t('explorer.binaryFilename'))}>
                        <Icon name="download" />
                        {t('explorer.downloadBody')}
                      </button>
                    </div>
                  ) : null}
                  {respTab === 'body' && !(ex.response && ex.response.binary) ? (
                    ex.response && ex.response.status ? (
                      <JsonViewer value={parsedBody !== null ? parsedBody : ex.response.body} maxHeight="60vh" emptyText={t('explorer.emptyBody')} />
                    ) : null
                  ) : null}
                  {respTab === 'headers' ? (ex.response && ex.response.headers ? <JsonViewer value={ex.response.headers} maxHeight={360} /> : null) : null}
                  {respTab === 'code' ? <SnippetPanel method={op.method} url={ex.resolvedUrl} body={op.requestBody && ex.body.trim() ? ex.body : null} /> : null}
                </Tabs>
              </section>
            </>
          ) : (
            <EmptyState icon="explorer" title={t('explorer.pickOperation')} />
          )}
        </div>
      ) : null}
      <ConfirmModal
        open={confirmOpen}
        danger
        title={t('explorer.confirmTitle')}
        message={op ? t('explorer.confirmMessage', { method: op.method, path: ex.resolvedPath }) : ''}
        confirmLabel={t('explorer.execute')}
        onCancel={() => setConfirmOpen(false)}
        onConfirm={() => {
          setConfirmOpen(false);
          ex.execute();
        }}
      />
    </div>
  );
}

export default ApiExplorerView;
