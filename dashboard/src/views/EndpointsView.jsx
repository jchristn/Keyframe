import { useId, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatDateTime, formatDuration, formatNumber, formatRelativeTime } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import StatusBadge from '../components/StatusBadge';
import Modal from '../components/Modal';
import { JsonViewerModal } from '../components/JsonViewer';
import Icon from '../components/Icon';
import CopyButton from '../components/CopyButton';
import useRemote from '../hooks/useRemote';
import { describeError } from '../utils/errors';

const CAPABILITIES = [
  { id: 'images', flag: 'SupportsImages', icon: 'image', kind: 'Completion' },
  { id: 'structuredOutput', flag: 'SupportsStructuredOutput', icon: 'braces', kind: 'Completion' },
  { id: 'audioInput', flag: 'SupportsAudioInput', icon: 'headphones', kind: 'Completion' },
  { id: 'transcription', flag: 'SupportsTranscription', icon: 'mic', kind: 'Transcription' }
];

/** Capabilities that apply to an endpoint of its kind (a transcription endpoint never takes images). */
export function endpointCapabilities(endpoint) {
  return CAPABILITIES.filter((c) => c.kind === endpoint.Kind && endpoint[c.flag]);
}

/** Capability pills, each with an icon and text so meaning never relies on color. */
export function CapabilityBadges({ endpoint }) {
  const { t } = useLocale();
  const caps = endpointCapabilities(endpoint);
  if (!caps.length) return <span className="muted">{t('common.none')}</span>;
  return (
    <span className="capability-badges">
      {caps.map((c) => (
        <span key={c.id} className={'badge capability-badge' + (c.id === 'audioInput' ? ' badge-accent' : ' badge-info')} title={t('endpoints.capabilities.' + c.id + 'Help')}>
          <Icon name={c.icon} size={12} />
          {t('endpoints.capabilities.' + c.id)}
        </span>
      ))}
    </span>
  );
}

/** Configured model endpoints with health, connectivity tests (admin), and JSON. */
function EndpointsView() {
  const { t, locale } = useLocale();
  const { apiClient, isAdmin } = useAuth();
  const { notify } = useToast();
  const ids = useId();
  const endpoints = useRemote(() => apiClient.listEndpoints(), [apiClient]);
  const [kind, setKind] = useState('');
  const [capability, setCapability] = useState('');
  const [query, setQuery] = useState('');
  const [detail, setDetail] = useState(null);
  const [json, setJson] = useState(null);
  const [testing, setTesting] = useState({});
  const [tests, setTests] = useState({});

  const all = endpoints.data || [];
  const q = query.trim().toLowerCase();
  const rows = all.filter(
    (e) => (!kind || e.Kind === kind) && (!capability || endpointCapabilities(e).some((c) => c.id === capability)) && (!q || [e.Name, e.Provider, e.Model, e.Endpoint].some((v) => v && String(v).toLowerCase().includes(q)))
  );

  const runTest = async (e) => {
    setTesting((s) => ({ ...s, [e.Name]: true }));
    try {
      const r = await apiClient.testEndpoint(e.Name);
      setTests((s) => ({ ...s, [e.Name]: r }));
      notify(r.Success ? 'success' : 'error', r.Success ? 'endpoints.toast.testOk' : 'endpoints.toast.testFailed', { name: e.Name, duration: formatDuration(r.DurationMs, locale) }, r.Error);
      endpoints.reload();
    } catch (err) {
      notify('error', 'endpoints.toast.testError', { name: e.Name }, describeError(t, err));
    } finally {
      setTesting((s) => ({ ...s, [e.Name]: false }));
    }
  };

  const columns = [
    { key: 'Name', label: t('endpoints.columns.name'), pinned: true, sortable: true, mono: true },
    {
      key: 'Healthy',
      label: t('endpoints.columns.health'),
      pinned: true,
      sortable: true,
      render: (e) =>
        !e.Active ? <StatusBadge kind="active" value={false} /> : <span title={e.HealthMessage || ''}><StatusBadge kind="health" value={e.Healthy} /></span>
    },
    { key: 'Kind', label: t('endpoints.columns.kind'), sortable: true, render: (e) => t('enums.endpointKind.' + e.Kind, { defaultValue: e.Kind }) },
    { key: 'Provider', label: t('endpoints.columns.provider'), sortable: true, render: (e) => t('enums.provider.' + e.Provider, { defaultValue: e.Provider }) },
    { key: 'Model', label: t('endpoints.columns.model'), sortable: true, mono: true },
    { key: 'Capabilities', label: t('endpoints.columns.capabilities'), render: (e) => <CapabilityBadges endpoint={e} /> },
    { key: 'Endpoint', label: t('endpoints.columns.url'), mono: true, render: (e) => e.Endpoint || <span className="muted">{t('endpoints.noUrl')}</span> },
    { key: 'IsDefault', label: t('endpoints.columns.default'), sortable: true, render: (e) => (e.IsDefault ? <StatusBadge tone="accent" label={t('endpoints.default')} /> : '') },
    { key: 'MaxConcurrentRequests', label: t('endpoints.columns.concurrency'), sortable: true, align: 'right', defaultHidden: true },
    { key: 'TimeoutMs', label: t('endpoints.columns.timeout'), sortable: true, align: 'right', defaultHidden: true, render: (e) => formatDuration(e.TimeoutMs, locale) },
    { key: 'ApiKeySet', label: t('endpoints.columns.apiKey'), defaultHidden: true, render: (e) => (e.ApiKeySet ? t('endpoints.keySet', { last4: e.ApiKeyLast4 || '' }) : t('endpoints.keyNotSet')) },
    {
      key: 'LastCheckedUtc',
      label: t('endpoints.columns.lastChecked'),
      sortable: true,
      nowrap: true,
      render: (e) => (e.LastCheckedUtc ? <span title={formatDateTime(e.LastCheckedUtc, locale)}>{formatRelativeTime(e.LastCheckedUtc, locale)}</span> : t('common.never'))
    },
    {
      key: 'test',
      label: t('endpoints.columns.test'),
      pinned: true,
      render: (e) =>
        isAdmin ? (
          <button type="button" className="button button-secondary button-sm" onClick={() => runTest(e)} disabled={!!testing[e.Name]}>
            <Icon name="zap" className={testing[e.Name] ? 'spin' : ''} />
            {testing[e.Name] ? t('endpoints.testing') : t('endpoints.test')}
          </button>
        ) : null
    }
  ];

  const rowActions = (e) => [
    { id: 'view', label: t('common.view'), icon: 'eye', onClick: () => setDetail(e) },
    { id: 'test', label: t('endpoints.test'), icon: 'zap', hidden: !isAdmin, onClick: () => runTest(e) },
    { id: 'json', label: t('common.viewJson'), icon: 'code', onClick: () => setJson(e) }
  ];

  const toolbar = (
    <div className="filter-bar">
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'kind'} className="field-label">
          {t('endpoints.filters.kind')}
        </label>
        <select id={ids + 'kind'} className="select-compact" value={kind} onChange={(e) => setKind(e.target.value)}>
          <option value="">{t('common.any')}</option>
          <option value="Completion">{t('enums.endpointKind.Completion')}</option>
          <option value="Transcription">{t('enums.endpointKind.Transcription')}</option>
        </select>
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'cap'} className="field-label">
          {t('endpoints.filters.capability')}
        </label>
        <select id={ids + 'cap'} className="select-compact" value={capability} onChange={(e) => setCapability(e.target.value)}>
          <option value="">{t('common.any')}</option>
          {CAPABILITIES.map((c) => (
            <option key={c.id} value={c.id}>
              {t('endpoints.capabilities.' + c.id)}
            </option>
          ))}
        </select>
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'q'} className="field-label">
          {t('endpoints.filters.search')}
        </label>
        <input id={ids + 'q'} type="search" value={query} onChange={(e) => setQuery(e.target.value)} placeholder={t('endpoints.filters.searchPlaceholder')} />
      </div>
      <button type="button" className="icon-button" onClick={endpoints.reload} aria-label={t('common.refresh')} title={t('common.refresh')} disabled={endpoints.loading}>
        <Icon name="refresh" className={endpoints.loading ? 'spin' : ''} />
      </button>
      <span className="muted filter-note">{t('endpoints.localFilterNote', { shown: formatNumber(rows.length, locale), total: formatNumber(all.length, locale) })}</span>
    </div>
  );

  const test = detail ? tests[detail.Name] : null;

  return (
    <div className="view">
      <PageHeader
        title={t('endpoints.title')}
        summary={t('endpoints.summary')}
        actions={
          isAdmin ? (
            <Link to="/dashboard/settings#inference" className="button button-primary">
              <Icon name="plus" />
              {t('endpoints.add')}
            </Link>
          ) : null
        }
      />
      {!isAdmin ? <p className="help-text">{t('endpoints.readOnlyNote')}</p> : null}
      <DataTable
        tableId="endpoints"
        columns={columns}
        rows={rows}
        rowKey={(e) => e.Name}
        loading={endpoints.loading}
        error={endpoints.error}
        onRetry={endpoints.reload}
        onRowClick={setDetail}
        rowActions={rowActions}
        toolbar={toolbar}
        sortNote={t('table.sortLocal')}
        rowClassName={(e) => (e.Active && !e.Healthy ? 'row-danger' : '')}
        emptyTitle={all.length ? t('endpoints.emptyFiltered') : t('endpoints.emptyTitle')}
        emptyMessage={all.length ? null : t('endpoints.emptyMessage')}
      />
      <Modal
        open={!!detail}
        onClose={() => setDetail(null)}
        title={detail ? detail.Name : ''}
        subtitle={t('endpoints.detailSubtitle')}
        size="lg"
        footer={
          detail ? (
            <>
              {isAdmin ? (
                <Link to="/dashboard/settings#inference" className="button button-secondary">
                  <Icon name="settings" />
                  {t('endpoints.editInSettings')}
                </Link>
              ) : null}
              {isAdmin ? (
                <button type="button" className="button button-primary" onClick={() => runTest(detail)} disabled={!!testing[detail.Name]}>
                  <Icon name="zap" />
                  {testing[detail.Name] ? t('endpoints.testing') : t('endpoints.test')}
                </button>
              ) : null}
            </>
          ) : null
        }
      >
        {detail ? (
          <>
            <dl className="kv-grid">
              <div>
                <dt>{t('endpoints.columns.health')}</dt>
                <dd>{detail.Active ? <StatusBadge kind="health" value={detail.Healthy} /> : <StatusBadge kind="active" value={false} />}</dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.kind')}</dt>
                <dd>{t('enums.endpointKind.' + detail.Kind, { defaultValue: detail.Kind })}</dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.provider')}</dt>
                <dd>
                  {t('enums.provider.' + detail.Provider, { defaultValue: detail.Provider })}
                  {detail.ProviderAvailable ? null : <span className="text-warning"> ({t('endpoints.providerUnavailable')})</span>}
                </dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.model')}</dt>
                <dd className="mono">{detail.Model || t('common.none')}</dd>
              </div>
              <div className="kv-grid-wide">
                <dt>{t('endpoints.columns.capabilities')}</dt>
                <dd>
                  <CapabilityBadges endpoint={detail} />
                  {detail.Kind === 'Completion' && !detail.SupportsAudioInput ? <p className="help-text">{t('endpoints.capabilities.noAudioInputHelp')}</p> : null}
                </dd>
              </div>
              <div className="kv-grid-wide">
                <dt>{t('endpoints.columns.url')}</dt>
                <dd className="mono break-all">
                  {detail.Endpoint || t('endpoints.noUrl')} {detail.Endpoint ? <CopyButton value={detail.Endpoint} /> : null}
                </dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.apiKey')}</dt>
                <dd>{detail.ApiKeySet ? t('endpoints.keySet', { last4: detail.ApiKeyLast4 || '' }) : t('endpoints.keyNotSet')}</dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.default')}</dt>
                <dd>{detail.IsDefault ? t('common.yes') : t('common.no')}</dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.concurrency')}</dt>
                <dd>{formatNumber(detail.MaxConcurrentRequests, locale)}</dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.timeout')}</dt>
                <dd>{formatDuration(detail.TimeoutMs, locale)}</dd>
              </div>
              <div className="kv-grid-wide">
                <dt>{t('endpoints.healthMessage')}</dt>
                <dd>{detail.HealthMessage || t('common.none')}</dd>
              </div>
              <div>
                <dt>{t('endpoints.columns.lastChecked')}</dt>
                <dd>{detail.LastCheckedUtc ? formatDateTime(detail.LastCheckedUtc, locale) : t('common.never')}</dd>
              </div>
            </dl>
            {test ? (
              <div className={'banner ' + (test.Success ? 'banner-success' : 'banner-danger')}>
                <Icon name={test.Success ? 'check' : 'alert'} />
                <div className="banner-body">
                  <div className="banner-title">{test.Success ? t('endpoints.testPassed') : t('endpoints.testFailedTitle')}</div>
                  <div>{t('endpoints.testDetail', { duration: formatDuration(test.DurationMs, locale), model: test.Model || '' })}</div>
                  {test.Error ? <div className="mono">{test.Error}</div> : null}
                </div>
              </div>
            ) : null}
          </>
        ) : null}
      </Modal>
      <JsonViewerModal open={!!json} onClose={() => setJson(null)} title={t('endpoints.jsonTitle')} subtitle={json && json.Name} value={json} />
    </div>
  );
}

export default EndpointsView;
