import { useEffect, useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { useLocale } from '../i18n';
import { formatNumber } from '../i18n/formatters';

export const PAGE_SIZES = [10, 25, 50, 100, 250, 500, 1000];

/**
 * Above-table pagination bar: total records, visible range, page N of M, first, previous,
 * jump-to-page, next, last, page size, and refresh.
 */
function Pagination({ pageNumber, pageSize, totalCount, onPageChange, onPageSizeChange, onRefresh = null, loading = false, pageSizes = PAGE_SIZES }) {
  const { t } = useTranslation();
  const { locale } = useLocale();
  const sizeId = useId();
  const jumpId = useId();
  const totalPages = Math.max(1, Math.ceil((totalCount || 0) / pageSize));
  const [jump, setJump] = useState(String(pageNumber));
  useEffect(() => setJump(String(pageNumber)), [pageNumber]);

  const from = totalCount === 0 ? 0 : (pageNumber - 1) * pageSize + 1;
  const to = Math.min(totalCount, pageNumber * pageSize);
  const go = (n) => onPageChange(Math.min(totalPages, Math.max(1, n)));

  return (
    <div className="pagination" role="navigation" aria-label={t('pagination.label')}>
      <div className="pagination-summary">
        {t('pagination.showing', {
          from: formatNumber(from, locale),
          to: formatNumber(to, locale),
          total: formatNumber(totalCount, locale)
        })}
        <span className="pagination-sep" aria-hidden="true">
          ·
        </span>
        {t('pagination.pageOf', { page: formatNumber(pageNumber, locale), pages: formatNumber(totalPages, locale) })}
      </div>
      <div className="pagination-controls">
        <button type="button" className="icon-button" onClick={() => go(1)} disabled={pageNumber <= 1 || loading} aria-label={t('pagination.first')} title={t('pagination.first')}>
          <Icon name="first" />
        </button>
        <button type="button" className="icon-button" onClick={() => go(pageNumber - 1)} disabled={pageNumber <= 1 || loading} aria-label={t('pagination.previous')} title={t('pagination.previous')}>
          <Icon name="chevronLeft" />
        </button>
        <form
          className="pagination-jump"
          onSubmit={(e) => {
            e.preventDefault();
            const n = parseInt(jump, 10);
            if (Number.isFinite(n)) go(n);
          }}
        >
          <label htmlFor={jumpId} className="sr-only">
            {t('pagination.jump')}
          </label>
          <input id={jumpId} type="number" min={1} max={totalPages} value={jump} onChange={(e) => setJump(e.target.value)} onBlur={() => {
            const n = parseInt(jump, 10);
            if (Number.isFinite(n) && n !== pageNumber) go(n);
          }} />
        </form>
        <button type="button" className="icon-button" onClick={() => go(pageNumber + 1)} disabled={pageNumber >= totalPages || loading} aria-label={t('pagination.next')} title={t('pagination.next')}>
          <Icon name="chevronRight" />
        </button>
        <button type="button" className="icon-button" onClick={() => go(totalPages)} disabled={pageNumber >= totalPages || loading} aria-label={t('pagination.last')} title={t('pagination.last')}>
          <Icon name="last" />
        </button>
        <label htmlFor={sizeId} className="pagination-size-label">
          {t('pagination.pageSize')}
        </label>
        <select id={sizeId} className="select-compact" value={pageSize} onChange={(e) => onPageSizeChange(Number(e.target.value))}>
          {pageSizes.map((s) => (
            <option key={s} value={s}>
              {formatNumber(s, locale)}
            </option>
          ))}
        </select>
        {onRefresh ? (
          <button type="button" className="icon-button" onClick={onRefresh} disabled={loading} aria-label={t('common.refresh')} title={t('common.refresh')}>
            <Icon name="refresh" className={loading ? 'spin' : ''} />
          </button>
        ) : null}
      </div>
    </div>
  );
}

export default Pagination;
