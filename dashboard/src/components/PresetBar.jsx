import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import Modal from './Modal';
import ConfirmModal from './ConfirmModal';
import usePersistedState from '../hooks/usePersistedState';

/**
 * Save, load, and delete named request presets in local storage. A preset is the request
 * configuration (context, model, functions, fields) without any media or credentials.
 */
function PresetBar({ storageKey, current, onLoad }) {
  const { t } = useTranslation();
  const selectId = useId();
  const nameId = useId();
  const [presets, setPresets] = usePersistedState(storageKey, []);
  const [selected, setSelected] = useState('');
  const [saving, setSaving] = useState(false);
  const [name, setName] = useState('');
  const [confirmDelete, setConfirmDelete] = useState(false);
  const list = Array.isArray(presets) ? presets : [];

  const save = () => {
    const trimmed = name.trim();
    if (!trimmed) return;
    const next = list.filter((p) => p.name !== trimmed);
    next.push({ name: trimmed, savedUtc: new Date().toISOString(), config: current() });
    next.sort((a, b) => a.name.localeCompare(b.name));
    setPresets(next);
    setSelected(trimmed);
    setSaving(false);
  };

  return (
    <div className="preset-bar">
      <label htmlFor={selectId} className="field-label">
        {t('presets.label')}
      </label>
      <select
        id={selectId}
        className="select-compact"
        value={selected}
        onChange={(e) => {
          setSelected(e.target.value);
          const p = list.find((x) => x.name === e.target.value);
          if (p) onLoad(p.config);
        }}
      >
        <option value="">{list.length ? t('presets.choose') : t('presets.none')}</option>
        {list.map((p) => (
          <option key={p.name} value={p.name}>
            {p.name}
          </option>
        ))}
      </select>
      <button
        type="button"
        className="button button-secondary button-sm"
        onClick={() => {
          setName(selected || '');
          setSaving(true);
        }}
      >
        <Icon name="save" />
        {t('presets.save')}
      </button>
      {selected ? (
        <button type="button" className="icon-button danger" onClick={() => setConfirmDelete(true)} aria-label={t('presets.delete')} title={t('presets.delete')}>
          <Icon name="trash" />
        </button>
      ) : null}
      <Modal
        open={saving}
        onClose={() => setSaving(false)}
        title={t('presets.saveTitle')}
        size="sm"
        footer={
          <>
            <button type="button" className="button button-secondary" onClick={() => setSaving(false)}>
              {t('common.cancel')}
            </button>
            <button type="button" className="button button-primary" onClick={save} disabled={!name.trim()}>
              {t('presets.save')}
            </button>
          </>
        }
      >
        <form
          onSubmit={(e) => {
            e.preventDefault();
            save();
          }}
        >
          <div className="form-field">
            <label htmlFor={nameId} className="field-label">
              {t('presets.name')}
            </label>
            <input id={nameId} value={name} onChange={(e) => setName(e.target.value)} maxLength={80} data-autofocus />
            <p className="help-text">{t('presets.saveHelp')}</p>
          </div>
        </form>
      </Modal>
      <ConfirmModal
        open={confirmDelete}
        title={t('presets.deleteTitle')}
        message={t('presets.deleteMessage', { name: selected })}
        danger
        confirmLabel={t('common.delete')}
        onCancel={() => setConfirmDelete(false)}
        onConfirm={() => {
          setPresets(list.filter((p) => p.name !== selected));
          setSelected('');
          setConfirmDelete(false);
        }}
      />
    </div>
  );
}

export default PresetBar;
