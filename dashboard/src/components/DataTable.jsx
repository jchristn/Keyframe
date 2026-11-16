import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import ActionMenu from './ActionMenu';
import { EmptyState, ErrorBanner, LoadingState } from './States';
import usePersistedState from '../hooks/usePersistedState';
import { STORAGE_KEYS } from '../utils/storage';

const INTERACTIVE = 'button, a, input, select, textarea, label, [role="menu"], [role="menuitem"], [data-no-row-click]';

function compare(a, b) {
  if (a === b) return 0;
  if (a === null || a === undefined || a === '') return 1;
  if (b === null || b === undefined || b === '') return -1;
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return String(a).localeCompare(String(b), undefined, { numeric: true, sensitivity: 'base' });
}

/** "Select Columns" popover. Pinned columns are always shown and not listed. */
function ColumnPicker({ columns, hidden, onChange }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const ref = useRef(null);
  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => ref.current && !ref.current.contains(e.target) && setOpen(false);
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);
  const toggleable = columns.filter((c) => !c.pinned);
  if (!toggleable.length) return null;
  return (
    <div className="column-picker" ref={ref}>
      <button type="button" className="button button-secondary button-sm" aria-expanded={open} onClick={() => setOpen((o) => !o)}>
        <Icon name="columns" />
        <span>{t('table.selectColumns')}</span>
      </button>
      {open ? (
        <div className="column-picker-panel" role="group" aria-label={t('table.selectColumns')}>
          {toggleable.map((c) => (
            <label key={c.key} className="checkbox-row">
              <input
                type="checkbox"
                checked={!hidden.includes(c.key)}
                onChange={(e) => onChange(e.target.checked ? hidden.filter((k) => k !== c.key) : [...hidden, c.key])}
              />
              <span>{c.label}</span>
            </label>
          ))}
          <button type="button" className="button button-link button-sm" onClick={() => onChange([])}>
            {t('table.showAllColumns')}
          </button>
        </div>
      ) : null}
    </div>
  );
}

/**
 * Reusable data table.
 * - Columns: { key, label, render(row), sortValue(row), sortable, pinned, defaultHidden, mono, nowrap, align, className }
 * - Toolbar above the table: `toolbar` (filters, add) on the left, Select Columns on the right,
 *   then `pagination` (above the table, never only below).
 * - Row click runs `onRowClick` except on interactive targets.
 * - Sorting is local to the rows passed in; `sortNote` explains its scope to the operator.
 * - Optional selection with a bulk action bar (`bulkActions(selectedRows, clear)`).
 */
function DataTable({
  tableId,
  columns,
  rows,
  rowKey = (r) => r.Id,
  loading = false,
  error = null,
  onRetry = null,
  onRowClick = null,
  rowActions = null,
  toolbar = null,
  pagination = null,
  emptyTitle = null,
  emptyMessage = null,
  emptyActions = null,
  sortNote = null,
  selectable = false,
  bulkActions = null,
  caption = null,
  rowClassName = null
}) {
  const { t } = useTranslation();
  const defaultHidden = useMemo(() => columns.filter((c) => c.defaultHidden).map((c) => c.key), [columns]);
  const [hidden, setHidden] = usePersistedState(STORAGE_KEYS.columns(tableId), defaultHidden);
  const [sort, setSort] = useState({ key: null, dir: 'asc' });
  const [selected, setSelected] = useState([]);

  const visibleColumns = columns.filter((c) => c.pinned || !hidden.includes(c.key));
  const list = useMemo(() => rows || [], [rows]);

  const sorted = useMemo(() => {
    if (!sort.key) return list;
    const col = columns.find((c) => c.key === sort.key);
    if (!col) return list;
    const get = col.sortValue || ((r) => r[col.key]);
    const out = list.slice().sort((a, b) => compare(get(a), get(b)));
    return sort.dir === 'desc' ? out.reverse() : out;
  }, [list, sort, columns]);

  // Clear selection whenever the underlying rows change (page change, refresh, mutation).
  useEffect(() => setSelected([]), [rows]);

  const keys = sorted.map(rowKey);
  const allSelected = keys.length > 0 && keys.every((k) => selected.includes(k));
  const selectedRows = sorted.filter((r) => selected.includes(rowKey(r)));

  const toggleSort = (col) => {
    if (!col.sortable) return;
    setSort((s) => (s.key === col.key ? { key: col.key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key: col.key, dir: 'asc' }));
  };

  const colCount = visibleColumns.length + (rowActions ? 1 : 0) + (selectable ? 1 : 0);
  const showEmpty = !loading && !error && sorted.length === 0;

  return (
    <div className="table-frame">
      <div className="table-toolbar">
        <div className="table-toolbar-left">{toolbar}</div>
        <div className="table-toolbar-right">
          <ColumnPicker columns={columns} hidden={hidden} onChange={setHidden} />
        </div>
      </div>
      {pagination}
      {selectable && selectedRows.length > 0 ? (
        <div className="bulk-bar" role="region" aria-label={t('table.bulkActions')}>
          <span>{t('table.selectedCount', { count: selectedRows.length })}</span>
          <button type="button" className="button button-link button-sm" onClick={() => setSelected([])}>
            {t('table.clearSelection')}
          </button>
          <span className="bulk-bar-actions">{bulkActions ? bulkActions(selectedRows, () => setSelected([])) : null}</span>
        </div>
      ) : null}
      {sortNote && sort.key ? <div className="table-note">{sortNote}</div> : null}
      {error ? <ErrorBanner error={error} onRetry={onRetry} /> : null}
      <div className="table-scroll">
        <table className="data-table">
          {caption ? <caption className="sr-only">{caption}</caption> : null}
          <thead>
            <tr>
              {selectable ? (
                <th scope="col" className="cell-select">
                  <input
                    type="checkbox"
                    aria-label={t('table.selectAll')}
                    checked={allSelected}
                    onChange={(e) => setSelected(e.target.checked ? keys : [])}
                  />
                </th>
              ) : null}
              {visibleColumns.map((col) => {
                const active = sort.key === col.key;
                return (
                  <th
                    key={col.key}
                    scope="col"
                    className={(col.sortable ? 'sortable ' : '') + (col.align === 'right' ? 'align-right ' : '') + (col.headerClassName || '')}
                    aria-sort={active ? (sort.dir === 'asc' ? 'ascending' : 'descending') : col.sortable ? 'none' : undefined}
                  >
                    {col.sortable ? (
                      <button type="button" className="th-sort" onClick={() => toggleSort(col)} title={t('table.sortBy', { column: col.label })}>
                        <span>{col.label}</span>
                        <Icon name={active ? (sort.dir === 'asc' ? 'chevronUp' : 'chevronDown') : 'chevronDown'} size={12} className={active ? '' : 'sort-idle'} />
                      </button>
                    ) : (
                      col.label
                    )}
                  </th>
                );
              })}
              {rowActions ? (
                <th scope="col" className="cell-actions">
                  <span className="sr-only">{t('common.actions')}</span>
                </th>
              ) : null}
            </tr>
          </thead>
          <tbody>
            {loading && sorted.length === 0 ? (
              <tr>
                <td colSpan={colCount}>
                  <LoadingState compact />
                </td>
              </tr>
            ) : null}
            {showEmpty ? (
              <tr>
                <td colSpan={colCount}>
                  <EmptyState compact title={emptyTitle || t('table.empty')} message={emptyMessage}>
                    {emptyActions}
                  </EmptyState>
                </td>
              </tr>
            ) : null}
            {sorted.map((row) => {
              const key = rowKey(row);
              return (
                <tr
                  key={key}
                  className={(onRowClick ? 'clickable ' : '') + (rowClassName ? rowClassName(row) || '' : '') + (loading ? ' row-stale' : '')}
                  onClick={(e) => {
                    if (!onRowClick) return;
                    if (e.target.closest(INTERACTIVE)) return;
                    onRowClick(row);
                  }}
                >
                  {selectable ? (
                    <td className="cell-select" data-no-row-click>
                      <input
                        type="checkbox"
                        aria-label={t('table.selectRow')}
                        checked={selected.includes(key)}
                        onChange={(e) => setSelected((s) => (e.target.checked ? [...s, key] : s.filter((k) => k !== key)))}
                      />
                    </td>
                  ) : null}
                  {visibleColumns.map((col) => (
                    <td
                      key={col.key}
                      className={
                        (col.mono ? 'mono ' : '') +
                        (col.nowrap || col.mono ? 'cell-nowrap ' : '') +
                        (col.align === 'right' ? 'align-right ' : '') +
                        (col.className || '')
                      }
                    >
                      {col.render ? col.render(row) : row[col.key] ?? ''}
                    </td>
                  ))}
                  {rowActions ? (
                    <td className="cell-actions">
                      <ActionMenu items={rowActions(row)} />
                    </td>
                  ) : null}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

export default DataTable;
