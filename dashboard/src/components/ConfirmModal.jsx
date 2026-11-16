import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';

/**
 * Custom confirmation dialog (never browser confirm). `onConfirm` may be async; the confirm
 * button shows a working state and the dialog stays open on failure so the error is visible.
 */
function ConfirmModal({ open, title, message, detail = null, confirmLabel = null, danger = false, onConfirm, onCancel, error = null }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);

  const handleConfirm = async () => {
    setBusy(true);
    try {
      await onConfirm();
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={busy ? undefined : onCancel}
      title={title}
      size="sm"
      footer={
        <>
          <button type="button" className="button button-secondary" onClick={onCancel} disabled={busy}>
            {t('common.cancel')}
          </button>
          <button
            type="button"
            className={'button ' + (danger ? 'button-danger' : 'button-primary')}
            onClick={handleConfirm}
            disabled={busy}
            data-autofocus
          >
            {busy ? t('common.working') : confirmLabel || t('common.confirm')}
          </button>
        </>
      }
    >
      <p className="confirm-message">{message}</p>
      {detail ? <div className="confirm-detail">{detail}</div> : null}
      {error ? (
        <div className="inline-error" role="alert">
          {error}
        </div>
      ) : null}
    </Modal>
  );
}

export default ConfirmModal;
