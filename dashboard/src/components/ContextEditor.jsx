import { useId } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { MAX_CONTEXT_ITEMS, MAX_LABEL_LENGTH } from '../utils/fields';

let ctxCounter = 0;
export function createContextRow(Label = '', Text = '') {
  ctxCounter += 1;
  return { uid: 'c' + ctxCounter, Label, Text };
}

function ContextRow({ row, index, onChange, onRemove, disabled }) {
  const { t } = useTranslation();
  const id = useId();
  return (
    <li className="context-row">
      <div className="form-field form-field-sm">
        <label htmlFor={id + 'label'} className="field-label">
          {t('context.label')}
        </label>
        <input id={id + 'label'} value={row.Label} maxLength={MAX_LABEL_LENGTH} onChange={(e) => onChange({ ...row, Label: e.target.value })} disabled={disabled} placeholder={t('context.labelPlaceholder')} />
      </div>
      <div className="form-field context-text">
        <label htmlFor={id + 'text'} className="field-label">
          {t('context.text')}
        </label>
        <textarea id={id + 'text'} rows={2} value={row.Text} onChange={(e) => onChange({ ...row, Text: e.target.value })} disabled={disabled} placeholder={t('context.textPlaceholder')} />
      </div>
      <button type="button" className="icon-button danger context-remove" onClick={onRemove} disabled={disabled} aria-label={t('context.remove', { n: index + 1 })} title={t('context.remove', { n: index + 1 })}>
        <Icon name="trash" />
      </button>
    </li>
  );
}

/** Editor for Context items ({ Label, Text }) shared by every prompt in a request. */
function ContextEditor({ rows, onChange, errors = [], disabled = false }) {
  const { t } = useTranslation();
  return (
    <div className="context-editor">
      <p className="help-text">{t('context.help')}</p>
      <ol className="context-list">
        {rows.map((row, i) => (
          <ContextRow
            key={row.uid}
            row={row}
            index={i}
            disabled={disabled}
            onChange={(next) => onChange(rows.map((r, j) => (j === i ? next : r)))}
            onRemove={() => onChange(rows.filter((_, j) => j !== i))}
          />
        ))}
      </ol>
      {errors.map((e, i) => (
        <div key={i} className="field-error" role="alert">
          {t(e.key, e.params)}
        </div>
      ))}
      <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...rows, createContextRow()])} disabled={disabled || rows.length >= MAX_CONTEXT_ITEMS}>
        <Icon name="plus" />
        {t('context.add')}
      </button>
    </div>
  );
}

export default ContextEditor;
