import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';

/**
 * Row action menu. The menu portals to document.body and positions itself against the
 * trigger so table scroll containers never clip it. Items: { id, label, icon, onClick,
 * danger, disabled, hidden }.
 */
function ActionMenu({ items, label = null }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [pos, setPos] = useState({ top: 0, left: 0 });
  const triggerRef = useRef(null);
  const menuRef = useRef(null);
  const visible = items.filter((i) => !i.hidden);

  const place = useCallback(() => {
    if (!triggerRef.current) return;
    const rect = triggerRef.current.getBoundingClientRect();
    const menuWidth = (menuRef.current && menuRef.current.offsetWidth) || 200;
    const menuHeight = (menuRef.current && menuRef.current.offsetHeight) || visible.length * 36 + 8;
    let top = rect.bottom + 4;
    if (top + menuHeight > window.innerHeight - 8) top = Math.max(8, rect.top - menuHeight - 4);
    let left = rect.right - menuWidth;
    if (left < 8) left = 8;
    if (left + menuWidth > window.innerWidth - 8) left = Math.max(8, window.innerWidth - menuWidth - 8);
    setPos({ top, left });
  }, [visible.length]);

  useLayoutEffect(() => {
    if (open) place();
  }, [open, place]);

  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => {
      if (menuRef.current && menuRef.current.contains(e.target)) return;
      if (triggerRef.current && triggerRef.current.contains(e.target)) return;
      setOpen(false);
    };
    const onKey = (e) => {
      if (e.key === 'Escape') {
        setOpen(false);
        triggerRef.current && triggerRef.current.focus();
      }
    };
    // Keep the menu attached to its trigger while containers scroll or the window resizes.
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    window.addEventListener('resize', place);
    document.addEventListener('scroll', place, true);
    const first = menuRef.current && menuRef.current.querySelector('button:not([disabled])');
    first && first.focus({ preventScroll: true });
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
      window.removeEventListener('resize', place);
      document.removeEventListener('scroll', place, true);
    };
  }, [open, place]);

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        className="icon-button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={label || t('common.actions')}
        title={label || t('common.actions')}
        onClick={(e) => {
          e.stopPropagation();
          setOpen((o) => !o);
        }}
        data-no-row-click
      >
        <Icon name="more" />
      </button>
      {open
        ? createPortal(
            <div ref={menuRef} className="action-menu" role="menu" style={{ top: pos.top, left: pos.left }} data-no-row-click>
              {visible.map((item) => (
                <button
                  key={item.id}
                  type="button"
                  role="menuitem"
                  className={'action-menu-item' + (item.danger ? ' danger' : '')}
                  disabled={item.disabled}
                  onClick={(e) => {
                    e.stopPropagation();
                    setOpen(false);
                    item.onClick();
                  }}
                >
                  {item.icon ? <Icon name={item.icon} /> : null}
                  <span>{item.label}</span>
                </button>
              ))}
            </div>,
            document.body
          )
        : null}
    </>
  );
}

export default ActionMenu;
