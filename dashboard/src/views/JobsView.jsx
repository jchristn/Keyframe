import { useCallback, useId, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useLocale } from '../i18n';
import { formatBytes, formatDateTime, formatDuration, formatRelativeTime } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Pagination from '../components/Pagination';
import StatusBadge from '../components/StatusBadge';
import CopyableId from '../components/CopyableId';
import ConfirmModal from '../components/ConfirmModal';
import ProgressBar from '../components/ProgressBar';
import { JsonViewerModal } from '../components/JsonViewer';
import Icon from '../components/Icon';
import useRemote from '../hooks/useRemote';
import useInterval from '../hooks/useInterval';
import usePersistedState from '../hooks/usePersistedState';
import { STORAGE_KEYS } from '../utils/storage';
import { isoToLocalInput, localInputToIso } from '../utils/ranges';
import { describeError } from '../utils/errors';

export const JOB_STATUSES = ['Queued', 'Running', 'Succeeded', 'Failed', 'Canceled', 'Interrupted'];
export const isActiveJob = (job) => job.Status === 'Queued' || job.Status === 'Running';

/** Track video jobs: backend filters, pagination, live refresh, cancel, delete, JSON. */
function JobsView() {
  const { t, locale } = useLocale();
  const navigate = useNavigate();
  const { apiClient } = useAuth();
  const { notify } = useToast();
  const [search, setSearch] = useSearchParams();
  const ids = useId();
  const [pageSize, setPageSize] = usePersistedState(STORAGE_KEYS.pageSize('jobs'), 25);
  const [jsonJob, setJsonJob] = useState(null);
  const [confirm, setConfirm] = useState(null);
  const [confirmError, setConfirmError] = useState(null);

  const filters = {
    status: search.get('status') || '',
    mediaKind: search.get('mediaKind') || '',
    principalName: search.get('principalName') || '',
    fromUtc: search.get('fromUtc') || '',
    toUtc: search.get('toUtc') || ''
  };
  const pageNumber = Math.max(1, parseInt(search.get('page') || '1', 10) || 1);
  const [principalDraft, setPrincipalDraft] = useState(filters.principalName);

  const setFilter = (patch) => {
    const next = new URLSearchParams(search);
    for (const [k, v] of Object.entries(patch)) {
      if (v) next.set(k, v);
      else next.delete(k);
    }
    if (!('page' in patch)) next.delete('page');
    setSearch(next, { replace: true });
  };

  const filterKey = JSON.stringify(filters);
  const jobs = useRemote(
    () => apiClient.listJobs({ ...filters, pageNumber, pageSize }),
     
    [apiClient, filterKey, pageNumber, pageSize]
  );
  const items = (jobs.data && jobs.data.items) || [];
  const anyActive = items.some(isActiveJob);
  useInterval(() => jobs.reload(), anyActive && !confirm && !jsonJob ? 4000 : null);

  const hasFilters = Object.values(filters).some(Boolean);

  const doCancel = useCallback(
    async (job) => {
      try {
        await apiClient.cancelJob(job.Id);
        notify('success', 'jobs.toast.canceled', { id: job.Id });
        setConfirm(null);
        jobs.reload();
      } catch (err) {
        setConfirmError(describeError(t, err));
      }
    },
    [apiClient, notify, jobs, t]
  );

  const doDelete = useCallback(
    async (list) => {
      let ok = 0;
      const failures = [];
      for (const job of list) {
        try {
          await apiClient.deleteJob(job.Id);
          ok += 1;
        } catch (err) {
          failures.push(job.Id + ': ' + describeError(t, err));
        }
      }
      if (failures.length) {
        setConfirmError(failures.join('\n'));
      } else {
        setConfirm(null);
      }
      if (ok) notify('success', 'jobs.toast.deleted', { count: ok });
      jobs.reload();
    },
    [apiClient, notify, jobs, t]
  );

  const columns = [
    { key: 'Id', label: t('jobs.columns.id'), pinned: true, nowrap: true, render: (j) => <CopyableId value={j.Id} /> },
    { key: 'Status', label: t('jobs.columns.status'), pinned: true, sortable: true, render: (j) => <StatusBadge kind="job" value={j.Status} /> },
    {
      key: 'Progress',
      label: t('jobs.columns.progress'),
      sortable: true,
      sortValue: (j) => (j.Progress ? j.Progress.Percent : 0),
      render: (j) =>
        j.Progress ? (
          <ProgressBar compact percent={j.Progress.Percent} label={j.Progress.Stage ? t('enums.progressStage.' + j.Progress.Stage, { defaultValue: j.Progress.Stage }) : null} tone={j.Status === 'Failed' ? 'danger' : j.Status === 'Succeeded' ? 'success' : 'accent'} />
        ) : null
    },
    { key: 'MediaKind', label: t('jobs.columns.kind'), sortable: true, defaultHidden: true, render: (j) => t('enums.mediaKind.' + j.MediaKind, { defaultValue: j.MediaKind }) },
    { key: 'PrincipalName', label: t('jobs.columns.principal'), sortable: true },
    { key: 'SourceUrl', label: t('jobs.columns.source'), defaultHidden: true, render: (j) => (j.SourceUrl ? <span className="mono truncate" title={j.SourceUrl}>{j.SourceUrl}</span> : <span className="muted">{t('jobs.upload')}</span>) },
    { key: 'SizeBytes', label: t('jobs.columns.size'), sortable: true, align: 'right', nowrap: true, render: (j) => (j.SizeBytes ? formatBytes(j.SizeBytes, locale) : '') },
    { key: 'DurationMs', label: t('jobs.columns.duration'), sortable: true, align: 'right', nowrap: true, render: (j) => (j.DurationMs ? formatDuration(j.DurationMs, locale) : '') },
    {
      key: 'CreatedUtc',
      label: t('jobs.columns.created'),
      sortable: true,
      nowrap: true,
      render: (j) => <span title={formatDateTime(j.CreatedUtc, locale)}>{formatRelativeTime(j.CreatedUtc, locale)}</span>
    },
    { key: 'CompletedUtc', label: t('jobs.columns.completed'), sortable: true, nowrap: true, defaultHidden: true, render: (j) => formatDateTime(j.CompletedUtc, locale) },
    { key: 'Error', label: t('jobs.columns.error'), defaultHidden: true, render: (j) => (j.Error ? <span className="text-danger truncate" title={j.Error}>{j.Error}</span> : '') }
  ];

  const rowActions = (j) => [
    { id: 'view', label: t('common.view'), icon: 'eye', onClick: () => navigate('/dashboard/jobs/' + encodeURIComponent(j.Id)) },
    { id: 'json', label: t('common.viewJson'), icon: 'code', onClick: () => setJsonJob(j) },
    { id: 'cancel', label: t('jobs.cancel'), icon: 'stop', hidden: !isActiveJob(j), onClick: () => { setConfirmError(null); setConfirm({ type: 'cancel', jobs: [j] }); } },
    { id: 'delete', label: t('common.delete'), icon: 'trash', danger: true, hidden: isActiveJob(j), onClick: () => { setConfirmError(null); setConfirm({ type: 'delete', jobs: [j] }); } }
  ];

  const toolbar = (
    <div className="filter-bar">
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'status'} className="field-label">
          {t('jobs.filters.status')}
        </label>
        <select id={ids + 'status'} className="select-compact" value={filters.status} onChange={(e) => setFilter({ status: e.target.value })}>
          <option value="">{t('common.any')}</option>
          {JOB_STATUSES.map((s) => (
            <option key={s} value={s}>
              {t('enums.jobStatus.' + s)}
            </option>
          ))}
        </select>
      </div>
      <div className="form-field form-field-xs">
        <label htmlFor={ids + 'kind'} className="field-label">
          {t('jobs.filters.kind')}
        </label>
        <select id={ids + 'kind'} className="select-compact" value={filters.mediaKind} onChange={(e) => setFilter({ mediaKind: e.target.value })}>
          <option value="">{t('common.any')}</option>
          {['Image', 'Video', 'Audio'].map((s) => (
            <option key={s} value={s}>
              {t('enums.mediaKind.' + s)}
            </option>
          ))}
        </select>
      </div>
      <form
        className="form-field form-field-xs"
        onSubmit={(e) => {
          e.preventDefault();
          setFilter({ principalName: principalDraft.trim() });
        }}
      >
        <label htmlFor={ids + 'principal'} className="field-label">
          {t('jobs.filters.principal')}
        </label>
        <input id={ids + 'principal'} value={principalDraft} onChange={(e) => setPrincipalDraft(e.target.value)} onBlur={() => principalDraft.trim() !== filters.principalName && setFilter({ principalName: principalDraft.trim() })} placeholder={t('jobs.filters.principalPlaceholder')} />
      </form>
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
      {hasFilters ? (
        <button
          type="button"
          className="button button-link button-sm filter-clear"
          onClick={() => {
            setPrincipalDraft('');
            setSearch(new URLSearchParams(), { replace: true });
          }}
        >
          {t('filters.clear')}
        </button>
      ) : null}
    </div>
  );

  const confirmJobs = confirm ? confirm.jobs : [];

  return (
    <div className="view">
      <PageHeader
        title={t('jobs.title')}
        summary={t('jobs.summary')}
        actions={
          <Link to="/dashboard/analyze/video" className="button button-primary">
            <Icon name="plus" />
            {t('jobs.newJob')}
          </Link>
        }
      />
      <DataTable
        tableId="jobs"
        columns={columns}
        rows={items}
        loading={jobs.loading}
        error={jobs.error}
        onRetry={jobs.reload}
        onRowClick={(j) => navigate('/dashboard/jobs/' + encodeURIComponent(j.Id))}
        rowActions={rowActions}
        toolbar={toolbar}
        sortNote={t('table.sortPage')}
        selectable
        bulkActions={(rows) => {
          const deletable = rows.filter((j) => !isActiveJob(j));
          return (
            <button type="button" className="button button-danger button-sm" disabled={!deletable.length} onClick={() => { setConfirmError(null); setConfirm({ type: 'delete', jobs: deletable }); }}>
              <Icon name="trash" />
              {t('jobs.deleteSelected', { count: deletable.length })}
            </button>
          );
        }}
        emptyTitle={hasFilters ? t('jobs.emptyFiltered') : t('jobs.emptyTitle')}
        emptyMessage={hasFilters ? t('jobs.emptyFilteredMessage') : t('jobs.emptyMessage')}
        emptyActions={
          hasFilters ? null : (
            <Link to="/dashboard/analyze/video" className="button button-primary button-sm">
              {t('jobs.newJob')}
            </Link>
          )
        }
        pagination={
          <Pagination
            pageNumber={pageNumber}
            pageSize={pageSize}
            totalCount={(jobs.data && jobs.data.totalCount) || 0}
            onPageChange={(p) => setFilter({ page: p > 1 ? String(p) : '' })}
            onPageSizeChange={(s) => {
              setPageSize(s);
              setFilter({ page: '' });
            }}
            onRefresh={jobs.reload}
            loading={jobs.loading}
          />
        }
      />
      {anyActive ? <p className="help-text">{t('jobs.autoRefresh')}</p> : null}
      <JsonViewerModal open={!!jsonJob} onClose={() => setJsonJob(null)} title={t('jobs.jsonTitle')} subtitle={jsonJob && jsonJob.Id} value={jsonJob} filename={jsonJob ? jsonJob.Id + '.json' : null} />
      <ConfirmModal
        open={!!confirm}
        danger
        title={confirm && confirm.type === 'cancel' ? t('jobs.cancelTitle') : t('jobs.deleteTitle', { count: confirmJobs.length })}
        message={
          confirm && confirm.type === 'cancel'
            ? t('jobs.cancelMessage', { id: confirmJobs[0] && confirmJobs[0].Id })
            : t('jobs.deleteMessage', { count: confirmJobs.length, id: confirmJobs[0] && confirmJobs[0].Id })
        }
        confirmLabel={confirm && confirm.type === 'cancel' ? t('jobs.cancel') : t('common.delete')}
        error={confirmError}
        onCancel={() => setConfirm(null)}
        onConfirm={() => (confirm.type === 'cancel' ? doCancel(confirmJobs[0]) : doDelete(confirmJobs))}
      />
    </div>
  );
}

export default JobsView;
