import { useEffect, useId, useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { MethodBadge } from './StatusBadge';

function matches(op, q) {
  return [op.method, op.path, op.id, op.summary, op.tag].some((v) => v && String(v).toLowerCase().includes(q));
}

/**
 * Full-width searchable dropdown for picking an API operation. Operations are grouped by
 * tag; type to filter by method, path, operation id, or summary. Keyboard: arrows move,
 * Enter selects, Escape closes.
 */
function OperationPicker({ groups, value, onChange }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState('');
  const [highlight, setHighlight] = useState(0);
  const rootRef = useRef(null);
  const searchRef = useRef(null);
  const listRef = useRef(null);
  const listId = useId();
  const searchId = useId();

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return groups;
    return groups.map((g) => ({ ...g, items: g.items.filter((o) => matches(o, q)) })).filter((g) => g.items.length);
  }, [groups, query]);
  const flat = useMemo(() => filtered.flatMap((g) => g.items), [filtered]);
  const total = useMemo(() => groups.reduce((n, g) => n + g.items.length, 0), [groups]);

  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => rootRef.current && !rootRef.current.contains(e.target) && setOpen(false);
    document.addEventListener('mousedown', close);
    return () => document.removeEventListener('mousedown', close);
  }, [open]);

  useEffect(() => {
    if (!open) return;
    const idx = Math.max(0, flat.findIndex((o) => value && o.id === value.id));
    setHighlight(idx);
    if (searchRef.current) searchRef.current.focus();
    // Only when the popover opens.
  }, [open]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => setHighlight(0), [query]);

  useEffect(() => {
    if (!open || !listRef.current) return;
    const el = listRef.current.querySelector('[data-index="' + highlight + '"]');
    if (el && el.scrollIntoView) el.scrollIntoView({ block: 'nearest' });
  }, [highlight, open]);

  const choose = (op) => {
    onChange(op.id);
    setOpen(false);
    setQuery('');
  };

  const onKeyDown = (e) => {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlight((h) => Math.min(flat.length - 1, h + 1));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlight((h) => Math.max(0, h - 1));
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (flat[highlight]) choose(flat[highlight]);
    } else if (e.key === 'Escape') {
      e.preventDefault();
      setOpen(false);
    }
  };

  let index = -1;
  return (
    <div className="op-picker" ref={rootRef}>
      <button
        type="button"
        className={'op-picker-trigger' + (open ? ' open' : '')}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={listId}
        aria-label={t('explorer.pickerLabel')}
        onClick={() => setOpen((o) => !o)}
        onKeyDown={(e) => {
          if (e.key === 'ArrowDown' && !open) {
            e.preventDefault();
            setOpen(true);
          }
        }}
      >
        {value ? (
          <>
            <MethodBadge method={value.method} />
            <span className="op-picker-value">
              <span className="mono op-picker-path">{value.path}</span>
              {value.summary ? <span className="op-picker-summary">{value.summary}</span> : null}
            </span>
          </>
        ) : (
          <span className="op-picker-value muted">{t('explorer.pickOperation')}</span>
        )}
        <span className="op-picker-count muted">{t('explorer.operationCount', { count: total })}</span>
        <Icon name="chevronDown" className="op-picker-chevron" />
      </button>
      {open ? (
        <div className="op-picker-popover">
          <div className="search-input">
            <Icon name="search" />
            <label htmlFor={searchId} className="sr-only">
              {t('explorer.search')}
            </label>
            <input
              id={searchId}
              ref={searchRef}
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={onKeyDown}
              placeholder={t('explorer.search')}
              role="combobox"
              aria-expanded="true"
              aria-controls={listId}
              aria-activedescendant={flat[highlight] ? listId + '-' + highlight : undefined}
              autoComplete="off"
            />
          </div>
          <div id={listId} ref={listRef} className="op-picker-list" role="listbox" aria-label={t('explorer.operations')}>
            {filtered.length === 0 ? <p className="muted op-picker-empty">{t('explorer.noMatches')}</p> : null}
            {filtered.map((g) => (
              <div key={g.tag} role="group" aria-label={g.tag} className="op-picker-group">
                <div className="nav-group-label">{g.tag}</div>
                {g.items.map((o) => {
                  index += 1;
                  const i = index;
                  const selected = value && value.id === o.id;
                  return (
                    <div
                      key={o.id}
                      id={listId + '-' + i}
                      data-index={i}
                      role="option"
                      aria-selected={selected}
                      className={'op-picker-option' + (i === highlight ? ' highlighted' : '') + (selected ? ' selected' : '')}
                      onMouseEnter={() => setHighlight(i)}
                      onMouseDown={(e) => e.preventDefault()}
                      onClick={() => choose(o)}
                    >
                      <MethodBadge method={o.method} />
                      <span className="mono op-picker-path">{o.path}</span>
                      <span className="op-picker-summary">{o.summary}</span>
                      {selected ? <Icon name="check" className="op-picker-check" /> : null}
                    </div>
                  );
                })}
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

export default OperationPicker;
