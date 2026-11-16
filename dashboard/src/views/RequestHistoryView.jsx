import { useId, useMemo, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatDateTime, formatDuration, formatList, formatNumber, formatPercent, formatRelativeTime } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Pagination from '../components/Pagination';
import StatusBadge, { MethodBadge } from '../components/StatusBadge';
import ActivityChart from '../components/ActivityChart';
import ConfirmModal from '../components/ConfirmModal';
import RequestDetailsModal from '../components/RequestDetailsModal';
import { JsonViewerModal } from '../components/JsonViewer';
import CopyableId from '../components/CopyableId';
import Icon from '../components/Icon';
import { ErrorBanner } from '../components/States';
import useRemote from '../hooks/useRemote';
import usePersistedState from '../hooks/usePersistedState';
import { STORAGE_KEYS } from '../utils/storage';
import { getRange, isoToLocalInput, localInputToIso, normalizeBuckets, rangeToParams } from '../utils/ranges';
import { describeError } from '../utils/errors';

const METHODS = ['GET', 'POST', 'PUT', 'DELETE', 'PATCH', 'HEAD'];
const FILTER_KEYS = ['method', 'statusCode', 'pathContains', 'principalName', 'fromUtc', 'toUtc'];

/** Investigate traffic: KPIs, activity chart, backend filters, paginated table, inspector, delete. */
function RequestHistoryView() {
  const { t, locale } = useLocale();
  const { apiClient, isAdmin } = useAuth();
  const { notify } = useToast();
  const ids = useId();
  const [search, setSearch] = useSearchParams();
  const [pageSize, setPageSize] = usePersistedState(STORAGE_KEYS.pageSize('requestHistory'), 25);
  const [rangeId, setRangeId] = usePersistedState('keyframe.history.range', 'day');
  const [chartParams, setChartParams] = useState(() => rangeToParams(rangeId));
  const [inspectId, setInspectId] = useState(null);
  const [jsonEntry, setJsonEntry] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [confirmError, setConfirmError] = useState(null);

  const filters = Object.fromEntries(FILTER_KEYS.map((k) => [k, search.get(k) || '']));
  const pageNumber = Math.max(1, parseInt(search.get('page') || '1', 10) || 1);
  const [drafts, setDrafts] = useState({ statusCode: filters.statusCode, pathContains: filters.pathContains, principalName: filters.principalName });
  const filterKey = JSON.stringify(filters);
  const hasFilters = FILTER_KEYS.some((k) => filters[k]);

  const setFilter = (patch) => {
    const next = new URLSearchParams(search);
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    if (!('page' in patch)) next.delete('page');
    setSearch(next, { replace: true });
  };

  const list = useRemote(
    () => apiClient.getRequestHistory({ ...filters, pageNumber, pageSize }),
     
    [apiClient, filterKey, pageNumber, pageSize]
  );
  const summary = useRemote(() => apiClient.getRequestHistorySummary(chartParams), [apiClient, chartParams]);
  const range = getRange(rangeId);
  const buckets = useMemo(() => normalizeBuckets(summary.data, chartParams, range.bucketCount), [summary.data, chartParams, range.bucketCount]);
  const s = summary.data;

  const filterSentence = () => {
    const parts = [];
    if (filters.method) parts.push(t('requestHistory.filterParts.method', { value: filters.method }));
    if (filters.statusCode) parts.push(t('requestHistory.filterParts.status', { value: filters.statusCode }));
    if (filters.pathContains) parts.push(t('requestHistory.filterParts.path', { value: filters.pathContains }));
    if (filters.principalName) parts.push(t('requestHistory.filterParts.principal', { value: filters.principalName }));
    if (filters.fromUtc) parts.push(t('requestHistory.filterParts.from', { value: formatDateTime(filters.fromUtc, locale) }));
    if (filters.toUtc) parts.push(t('requestHistory.filterParts.to', { value: formatDateTime(filters.toUtc, locale) }));
    return formatList(parts, locale);
  };

  const deleteOne = async (entry) => {
    try {
      await apiClient.deleteRequestHistoryEntry(entry.Id);
      notify('success', 'requestHistory.toast.deleted');
      setConfirm(null);
      setInspectId(null);
      list.reload();
      summary.reload();
    } catch (err) {
      setConfirmError(describeError(t, err));
    }
  };

  const deleteBulk = async () => {
    try {
      const { method, statusCode, pathContains, principalName, fromUtc, toUtc } = filters;
      const r = await apiClient.deleteRequestHistory({ method, statusCode, pathContains, principalName, fromUtc, toUtc });
      notify('success', 'requestHistory.toast.bulkDeleted', { count: (r && r.DeletedCount) || 0 });
      setConfirm(null);
      setFilter({ page: '' });
      list.reload();
      summary.reload();
    } catch (err) {
      setConfirmError(describeError(t, err));
    }
  };

  const openJson = async (row) => {
    try {
      setJsonEntry(await apiClient.getRequestHistoryEntry(row.Id));
    } catch (err) {
      notify('error', 'requestHistory.toast.loadFailed', {}, describeError(t, err));
    }
  };

  const columns = [
    {
      key: 'CreatedUtc',
      label: t('requestHistory.columns.time'),
      pinned: true,
      sortable: true,
      nowrap: true,
      render: (r) => <span title={formatDateTime(r.CreatedUtc, locale)}>{formatDateTime(r.CreatedUtc, locale, { dateStyle: 'short', timeStyle: 'medium' })}</span>
    },
    { key: 'Method', label: t('requestHistory.columns.method'), pinned: true, sortable: true, render: (r) => <MethodBadge method={r.Method} /> },
    { key: 'Path', label: t('requestHistory.columns.path'), pinned: true, sortable: true, className: 'cell-path', render: (r) => <span className="mono truncate" title={r.Url}>{r.Path}</span> },
    { key: 'StatusCode', label: t('requestHistory.columns.status'), sortable: true, render: (r) => <StatusBadge kind="http" value={r.StatusCode} /> },
    { key: 'DurationMs', label: t('requestHistory.columns.duration'), sortable: true, align: 'right', nowrap: true, render: (r) => formatDuration(r.DurationMs, locale) },
    { key: 'PrincipalName', label: t('requestHistory.columns.principal'), sortable: true },
    { key: 'SourceIp', label: t('requestHistory.columns.sourceIp'), mono: true, defaultHidden: true },
    { key: 'Id', label: t('requestHistory.columns.id'), defaultHidden: true, render: (r) => <CopyableId value={r.Id} /> },
    { key: 'ago', label: t('requestHistory.columns.ago'), defaultHidden: true, render: (r) => formatRelativeTime(r.CreatedUtc, locale) }
  ];

  const rowActions = (r) => [
    { id: 'view', label: t('common.view'), icon: 'eye', onClick: () => setInspectId(r.Id) },
    { id: 'json', label: t('common.viewJson'), icon: 'code', onClick: () => openJson(r) },
    { id: 'filterPath', label: t('requestHistory.filterByPath'), icon: 'filter', onClick: () => { setDrafts((d) => ({ ...d, pathContains: r.Path })); setFilter({ pathContains: r.Path }); } },
    { id: 'delete', label: t('common.delete'), icon: 'trash', danger: true, hidden: !isAdmin, onClick: () => { setConfirmError(null); setConfirm({ type: 'one', entry: r }); } }
  ];

  const commitDraft = (key) => {
    const v = (drafts[key] || '').trim();
    if (v !== filters[key]) setFilter({ [key]: v });
  };

  const toolbar = (
    <form
      className="filter-bar"
      onSubmit={(e) => {
        e.preventDefault();
        setFilter({ statusCode: drafts.statusCode.trim(), pathContains: drafts.pathContains.trim(), principalName: drafts.principalName.trim() });
      }}
    >
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'method'} className="field-label">
          {t('requestHistory.filters.method')}
        </label>
        <select id={ids + 'method'} className="select-compact" value={filters.method} onChange={(e) => setFilter({ method: e.target.value })}>
          <option value="">{t('common.any')}</option>
          {METHODS.map((m) => (
            <option key={m} value={m}>
              {m}
            </option>
          ))}
        </select>
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'status'} className="field-label">
          {t('requestHistory.filters.status')}
        </label>
        <input id={ids + 'status'} type="number" min={100} max={599} className="input-status" value={drafts.statusCode} onChange={(e) => setDrafts({ ...drafts, statusCode: e.target.value })} onBlur={() => commitDraft('statusCode')} placeholder={t('requestHistory.filters.statusPlaceholder')} />
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'path'} className="field-label">
          {t('requestHistory.filters.path')}
        </label>
        <input id={ids + 'path'} value={drafts.pathContains} onChange={(e) => setDrafts({ ...drafts, pathContains: e.target.value })} onBlur={() => commitDraft('pathContains')} placeholder={t('requestHistory.filters.pathPlaceholder')} />
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'principal'} className="field-label">
          {t('requestHistory.filters.principal')}
        </label>
        <input id={ids + 'principal'} value={drafts.principalName} onChange={(e) => setDrafts({ ...drafts, principalName: e.target.value })} onBlur={() => commitDraft('principalName')} placeholder={t('requestHistory.filters.principalPlaceholder')} />
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'from'} className="field-label">
          {t('filters.from')}
        </label>
        <input id={ids + 'from'} type="datetime-local" value={isoToLocalInput(filters.fromUtc)} onChange={(e) => setFilter({ fromUtc: localInputToIso(e.target.value) })} />
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'to'} className="field-label">
          {t('filters.to')}
        </label>
        <input id={ids + 'to'} type="datetime-local" value={isoToLocalInput(filters.toUtc)} onChange={(e) => setFilter({ toUtc: localInputToIso(e.target.value) })} />
      </div>
      <button type="submit" className="button button-secondary button-sm">
        <Icon name="filter" />
        {t('filters.apply')}
      </button>
      {hasFilters ? (
        <button
          type="button"
          className="button button-link button-sm filter-clear"
          onClick={() => {
            setDrafts({ statusCode: '', pathContains: '', principalName: '' });
            setSearch(new URLSearchParams(), { replace: true });
          }}
        >
          {t('filters.clear')}
        </button>
      ) : null}
      {isAdmin ? (
        <button
          type="button"
          className="button button-danger button-sm"
          disabled={!list.data || !list.data.totalCount}
          onClick={() => {
            setConfirmError(null);
            setConfirm({ type: 'bulk' });
          }}
        >
          <Icon name="trash" />
          {hasFilters ? t('requestHistory.deleteMatching') : t('requestHistory.clearAll')}
        </button>
      ) : null}
    </form>
  );

  const total = (list.data && list.data.totalCount) || 0;

  return (
    <div className="view">
      <PageHeader title={t('requestHistory.title')} summary={t('requestHistory.summary')} />
      <dl className="kpi-strip">
        <div>
          <dt>{t('requestHistory.kpi.inRange', { range: t('chart.ranges.' + rangeId) })}</dt>
          <dd>{s ? formatNumber(s.TotalCount, locale) : '...'}</dd>
        </div>
        <div>
          <dt>{t('requestHistory.kpi.failures')}</dt>
          <dd className={s && s.TotalFailure ? 'text-danger' : ''}>{s ? formatNumber(s.TotalFailure, locale) : '...'}</dd>
        </div>
        <div>
          <dt>{t('requestHistory.kpi.successRate')}</dt>
          <dd>{s && s.TotalCount ? formatPercent(s.TotalSuccess / s.TotalCount, locale) : t('common.notAvailable')}</dd>
        </div>
        <div>
          <dt>{t('requestHistory.kpi.avgDuration')}</dt>
          <dd>{s ? formatDuration(s.AverageDurationMs, locale) : '...'}</dd>
        </div>
        <div>
          <dt>{t('requestHistory.kpi.matching')}</dt>
          <dd>{list.data ? formatNumber(total, locale) : '...'}</dd>
        </div>
      </dl>
      {summary.error ? <ErrorBanner error={summary.error} onRetry={summary.reload} /> : null}
      <ActivityChart
        buckets={buckets}
        bucketMinutes={chartParams.bucketMinutes}
        rangeId={rangeId}
        onRangeChange={(id) => {
          setRangeId(id);
          setChartParams(rangeToParams(id));
        }}
        onRefresh={() => setChartParams(rangeToParams(rangeId))}
        loading={summary.loading}
        loadedAt={summary.loadedAt}
        onBucketClick={(b) => setFilter({ fromUtc: b.startUtc, toUtc: b.endUtc })}
      />
      <DataTable
        tableId="requestHistory"
        columns={columns}
        rows={(list.data && list.data.items) || []}
        loading={list.loading}
        error={list.error}
        onRetry={list.reload}
        onRowClick={(r) => setInspectId(r.Id)}
        rowActions={rowActions}
        toolbar={toolbar}
        sortNote={t('table.sortPage')}
        rowClassName={(r) => (r.StatusCode >= 500 ? 'row-danger' : '')}
        emptyTitle={hasFilters ? t('requestHistory.emptyFiltered') : t('requestHistory.emptyTitle')}
        emptyMessage={hasFilters ? t('requestHistory.emptyFilteredMessage') : t('requestHistory.emptyMessage')}
        pagination={
          <Pagination
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={total}
            onPageChange={(p) => setFilter({ page: p > 1 ? String(p) : '' })}
            onPageSizeChange={(sz) => {
              setPageSize(sz);
              setFilter({ page: '' });
            }}
            onRefresh={list.reload}
            loading={list.loading}
          />
        }
      />
      <RequestDetailsModal
        entryId={inspectId}
        apiClient={apiClient}
        onClose={() => setInspectId(null)}
        onDelete={
          isAdmin
            ? (e) => {
                setConfirmError(null);
                setConfirm({ type: 'one', entry: e });
              }
            : null
        }
      />
      <JsonViewerModal open={!!jsonEntry} onClose={() => setJsonEntry(null)} title={t('requestHistory.jsonTitle')} subtitle={jsonEntry && jsonEntry.Id} value={jsonEntry} filename={jsonEntry ? jsonEntry.Id + '.json' : null} />
      <ConfirmModal
        open={!!confirm}
        danger
        title={confirm && confirm.type === 'bulk' ? t('requestHistory.bulkTitle') : t('requestHistory.deleteTitle')}
        message={
          confirm && confirm.type === 'bulk'
            ? hasFilters
              ? t('requestHistory.bulkMessageFiltered', { count: total, filters: filterSentence() })
              : t('requestHistory.bulkMessageAll', { count: total })
            : t('requestHistory.deleteMessage', { method: confirm && confirm.entry ? confirm.entry.Method : '', path: confirm && confirm.entry ? confirm.entry.Path : '' })
        }
        confirmLabel={t('common.delete')}
        error={confirmError}
        onCancel={() => setConfirm(null)}
        onConfirm={() => (confirm.type === 'bulk' ? deleteBulk() : deleteOne(confirm.entry))}
      />
    </div>
  );
}

export default RequestHistoryView;
