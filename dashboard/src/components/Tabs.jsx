import { useId } from 'react';

/** Accessible tab strip. tabs: [{ id, label, badge }]. Arrow keys move between tabs. */
function Tabs({ tabs, active, onChange, label, children = null, size = 'md' }) {
  const base = useId();
  const onKeyDown = (e) => {
    const idx = tabs.findIndex((tab) => tab.id === active);
    if (e.key === 'ArrowRight' || e.key === 'ArrowLeft') {
      e.preventDefault();
      const next = (idx + (e.key === 'ArrowRight' ? 1 : -1) + tabs.length) % tabs.length;
      onChange(tabs[next].id);
      const el = document.getElementById(base + '-tab-' + tabs[next].id);
      el && el.focus();
    }
  };
  return (
    <div className={'tabs tabs-' + size}>
      <div className="tab-list" role="tablist" aria-label={label} onKeyDown={onKeyDown}>
        {tabs.map((tab) => (
          <button
            key={tab.id}
            id={base + '-tab-' + tab.id}
            type="button"
            role="tab"
            aria-selected={tab.id === active}
            aria-controls={base + '-panel'}
            tabIndex={tab.id === active ? 0 : -1}
            className={'tab' + (tab.id === active ? ' active' : '')}
            onClick={() => onChange(tab.id)}
          >
            {tab.label}
            {tab.badge !== undefined && tab.badge !== null ? <span className="tab-badge">{tab.badge}</span> : null}
          </button>
        ))}
      </div>
      {children !== null ? (
        <div id={base + '-panel'} role="tabpanel" className="tab-panel" aria-labelledby={base + '-tab-' + active}>
          {children}
        </div>
      ) : null}
    </div>
  );
}

export default Tabs;
