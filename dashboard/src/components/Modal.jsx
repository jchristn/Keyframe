import { useEffect, useId, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';

const FOCUSABLE = 'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

let openCount = 0;

/**
 * Accessible modal: portals to body, locks body scroll, closes on Escape and backdrop click
 * (unless `blocking`), traps focus, and restores focus to the opener on close.
 * Sizes: sm (confirm), md (forms), lg (detail), xl (inspectors), full (95% of the viewport). variant="drawer" slides the
 * same panel in from the inline end edge for record details that stay tied to a list.
 */
function Modal({ open, onClose, title, subtitle = null, headerExtra = null, size = 'md', footer = null, blocking = false, children, className = '', variant = 'dialog' }) {
  const { t } = useTranslation();
  const panelRef = useRef(null);
  const titleId = useId();
  const onCloseRef = useRef(onClose);
  onCloseRef.current = onClose;

  useEffect(() => {
    if (!open) return undefined;
    const opener = document.activeElement;
    openCount += 1;
    document.body.style.overflow = 'hidden';
    const panel = panelRef.current;
    const first = panel && panel.querySelector('[data-autofocus]');
    (first || panel)?.focus();

    const onKey = (e) => {
      if (e.key === 'Escape' && !blocking) {
        e.stopPropagation();
        onCloseRef.current && onCloseRef.current();
      }
      if (e.key === 'Tab' && panel) {
        const items = [...panel.querySelectorAll(FOCUSABLE)].filter((el) => el.offsetParent !== null || el === document.activeElement);
        if (!items.length) return;
        const firstEl = items[0];
        const lastEl = items[items.length - 1];
        if (e.shiftKey && document.activeElement === firstEl) {
          e.preventDefault();
          lastEl.focus();
        } else if (!e.shiftKey && document.activeElement === lastEl) {
          e.preventDefault();
          firstEl.focus();
        }
      }
    };
    panel && panel.addEventListener('keydown', onKey);
    return () => {
      panel && panel.removeEventListener('keydown', onKey);
      openCount -= 1;
      if (openCount <= 0) {
        openCount = 0;
        document.body.style.overflow = '';
      }
      if (opener && typeof opener.focus === 'function') opener.focus();
    };
  }, [open, blocking]);

  if (!open) return null;

  return createPortal(
    <div
      className={'modal-backdrop' + (variant === 'drawer' ? ' drawer-backdrop' : '')}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !blocking) onClose && onClose();
      }}
    >
      <div
        ref={panelRef}
        className={'modal modal-' + size + (variant === 'drawer' ? ' drawer' : '') + ' ' + className}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        tabIndex={-1}
      >
        <div className="modal-header">
          <div className="modal-titles">
            <h2 id={titleId} className="modal-title">
              {title}
            </h2>
            {subtitle ? <div className="modal-subtitle">{subtitle}</div> : null}
            {headerExtra ? <div className="modal-header-extra">{headerExtra}</div> : null}
          </div>
          {!blocking ? (
            <button type="button" className="icon-button" onClick={onClose} aria-label={t('common.close')} title={t('common.close')}>
              <Icon name="close" />
            </button>
          ) : null}
        </div>
        <div className="modal-body">{children}</div>
        {footer ? <div className="modal-footer">{footer}</div> : null}
      </div>
    </div>,
    document.body
  );
}

export default Modal;
