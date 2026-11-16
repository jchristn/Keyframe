import { useId, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { FIELD_TYPES, TYPE_CONSTRAINTS, createField, moveItem } from '../utils/fields';

function FieldError({ error }) {
  const { t } = useTranslation();
  if (!error) return null;
  return (
    <div className="field-error" role="alert">
      {t(error.key, error.params)}
    </div>
  );
}

/** Chip editor for Enum options. Enter or comma adds; Backspace on empty removes the last. */
function OptionsEditor({ id, options, onChange, disabled, invalid }) {
  const { t } = useTranslation();
  const [draft, setDraft] = useState('');
  const commit = () => {
    const parts = draft
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean)
      .filter((s) => !options.some((o) => o.toLowerCase() === s.toLowerCase()));
    if (parts.length) onChange([...options, ...parts]);
    setDraft('');
  };
  return (
    <div className={'chip-input' + (invalid ? ' invalid' : '')}>
      {options.map((o, i) => (
        <span key={o + i} className="chip">
          <span>{o}</span>
          <button
            type="button"
            className="chip-remove"
            onClick={() => onChange(options.filter((_, j) => j !== i))}
            aria-label={t('fieldBuilder.removeOption', { option: o })}
            title={t('fieldBuilder.removeOption', { option: o })}
            disabled={disabled}
          >
            <Icon name="close" size={12} />
          </button>
        </span>
      ))}
      <input
        id={id}
        value={draft}
        disabled={disabled}
        placeholder={options.length ? '' : t('fieldBuilder.optionsPlaceholder')}
        onChange={(e) => setDraft(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ',') {
            e.preventDefault();
            commit();
          } else if (e.key === 'Backspace' && !draft && options.length) {
            onChange(options.slice(0, -1));
          }
        }}
        onBlur={commit}
        aria-invalid={invalid || undefined}
      />
    </div>
  );
}

function FieldRow({ field, index, count, errors, onUpdate, onRemove, onDuplicate, onMove, disabled, dragHandlers }) {
  const { t } = useTranslation();
  const base = useId();
  const constraints = TYPE_CONSTRAINTS[field.Type] || [];
  const err = errors || {};
  const set = (prop) => (e) => onUpdate({ ...field, [prop]: e && e.target ? (e.target.type === 'checkbox' ? e.target.checked : e.target.value) : e });
  const number = (prop, labelKey, integer) => (
    <div className="form-field form-field-sm">
      <label htmlFor={base + prop} className="field-label">
        {t(labelKey)}
      </label>
      <input
        id={base + prop}
        type="number"
        step={integer ? 1 : 'any'}
        min={prop === 'MaxLength' || prop === 'MaxItems' ? 1 : undefined}
        value={field[prop]}
        onChange={set(prop)}
        disabled={disabled}
        aria-invalid={!!err[prop] || undefined}
        placeholder={t('fieldBuilder.optional')}
      />
      <FieldError error={err[prop]} />
    </div>
  );

  return (
    <li className={'field-row' + (Object.keys(err).length ? ' has-error' : '')} {...dragHandlers} data-testid="field-row">
      <div className="field-row-head">
        <span className="drag-handle" title={t('fieldBuilder.dragToReorder')} aria-hidden="true">
          <Icon name="grip" />
        </span>
        <span className="field-index">{t('fieldBuilder.fieldNumber', { n: index + 1 })}</span>
        <span className="field-row-actions">
          <button type="button" className="icon-button" onClick={() => onMove(index, index - 1)} disabled={disabled || index === 0} aria-label={t('fieldBuilder.moveUp')} title={t('fieldBuilder.moveUp')}>
            <Icon name="arrowUp" />
          </button>
          <button type="button" className="icon-button" onClick={() => onMove(index, index + 1)} disabled={disabled || index === count - 1} aria-label={t('fieldBuilder.moveDown')} title={t('fieldBuilder.moveDown')}>
            <Icon name="arrowDown" />
          </button>
          <button type="button" className="icon-button" onClick={onDuplicate} disabled={disabled} aria-label={t('fieldBuilder.duplicate')} title={t('fieldBuilder.duplicate')}>
            <Icon name="duplicate" />
          </button>
          <button type="button" className="icon-button danger" onClick={onRemove} disabled={disabled} aria-label={t('fieldBuilder.remove')} title={t('fieldBuilder.remove')}>
            <Icon name="trash" />
          </button>
        </span>
      </div>
      <div className="field-grid">
        <div className="form-field">
          <label htmlFor={base + 'key'} className="field-label">
            {t('fieldBuilder.key')}
            <span className="required-mark" aria-hidden="true">
              *
            </span>
          </label>
          <input
            id={base + 'key'}
            className="mono"
            value={field.Key}
            onChange={set('Key')}
            disabled={disabled}
            maxLength={64}
            placeholder={t('fieldBuilder.keyPlaceholder')}
            aria-invalid={!!err.Key || undefined}
            autoComplete="off"
            spellCheck={false}
          />
          <FieldError error={err.Key} />
        </div>
        <div className="form-field">
          <label htmlFor={base + 'type'} className="field-label">
            {t('fieldBuilder.type')}
          </label>
          <select id={base + 'type'} value={field.Type} onChange={set('Type')} disabled={disabled}>
            {FIELD_TYPES.map((ft) => (
              <option key={ft} value={ft}>
                {t('enums.fieldType.' + ft)}
              </option>
            ))}
          </select>
        </div>
        <div className="form-field field-grid-wide">
          <label htmlFor={base + 'prompt'} className="field-label">
            {t('fieldBuilder.prompt')}
            <span className="required-mark" aria-hidden="true">
              *
            </span>
          </label>
          <textarea
            id={base + 'prompt'}
            rows={2}
            value={field.Prompt}
            onChange={set('Prompt')}
            disabled={disabled}
            placeholder={t('fieldBuilder.promptPlaceholder')}
            aria-invalid={!!err.Prompt || undefined}
          />
          <FieldError error={err.Prompt} />
        </div>
        {constraints.includes('Options') ? (
          <div className="form-field field-grid-wide">
            <label htmlFor={base + 'options'} className="field-label">
              {t('fieldBuilder.options')}
              <span className="required-mark" aria-hidden="true">
                *
              </span>
            </label>
            <OptionsEditor id={base + 'options'} options={field.Options || []} onChange={(o) => onUpdate({ ...field, Options: o })} disabled={disabled} invalid={!!err.Options} />
            <FieldError error={err.Options} />
          </div>
        ) : null}
        {constraints.includes('Minimum') ? number('Minimum', 'fieldBuilder.minimum', field.Type === 'Integer') : null}
        {constraints.includes('Maximum') ? number('Maximum', 'fieldBuilder.maximum', field.Type === 'Integer') : null}
        {constraints.includes('MaxLength') ? number('MaxLength', 'fieldBuilder.maxLength', true) : null}
        {constraints.includes('MaxItems') ? number('MaxItems', 'fieldBuilder.maxItems', true) : null}
        {constraints.includes('Schema') ? (
          <div className="form-field field-grid-wide">
            <label htmlFor={base + 'schema'} className="field-label">
              {t('fieldBuilder.schema')}
            </label>
            <textarea id={base + 'schema'} className="mono" rows={3} value={field.Schema} onChange={set('Schema')} disabled={disabled} placeholder={t('fieldBuilder.schemaPlaceholder')} aria-invalid={!!err.Schema || undefined} spellCheck={false} />
            <FieldError error={err.Schema} />
          </div>
        ) : null}
        <div className="form-field form-field-sm">
          <label htmlFor={base + 'default'} className="field-label">
            {t('fieldBuilder.default')}
          </label>
          {field.Type === 'Boolean' ? (
            <select id={base + 'default'} value={String(field.Default)} onChange={set('Default')} disabled={disabled}>
              <option value="">{t('fieldBuilder.noDefault')}</option>
              <option value="true">{t('common.true')}</option>
              <option value="false">{t('common.false')}</option>
            </select>
          ) : field.Type === 'Enum' ? (
            <select id={base + 'default'} value={field.Default} onChange={set('Default')} disabled={disabled} aria-invalid={!!err.Default || undefined}>
              <option value="">{t('fieldBuilder.noDefault')}</option>
              {(field.Options || []).map((o) => (
                <option key={o} value={o}>
                  {o}
                </option>
              ))}
            </select>
          ) : (
            <input
              id={base + 'default'}
              value={field.Default}
              onChange={set('Default')}
              disabled={disabled}
              placeholder={field.Type === 'StringList' ? t('fieldBuilder.defaultListPlaceholder') : t('fieldBuilder.optional')}
              aria-invalid={!!err.Default || undefined}
            />
          )}
          <FieldError error={err.Default} />
        </div>
        <div className="form-field form-field-sm form-field-check">
          <label className="checkbox-row">
            <input type="checkbox" checked={!!field.Required} onChange={set('Required')} disabled={disabled} />
            <span>{t('fieldBuilder.required')}</span>
          </label>
        </div>
      </div>
    </li>
  );
}

/**
 * Typed field builder for Extract.Fields: add, remove, duplicate, reorder (buttons or
 * drag and drop), type, prompt, and per-type constraints. Controlled: `fields` is the list
 * of editable fields from utils/fields.createField; `errors` is validateFields().byUid.
 */
function FieldBuilder({ fields, onChange, errors = {}, disabled = false, label = null }) {
  const { t } = useTranslation();
  const [dragIndex, setDragIndex] = useState(null);
  const [newType, setNewType] = useState('String');
  const typeId = useId();

  const update = (i, next) => onChange(fields.map((f, j) => (j === i ? next : f)));
  const remove = (i) => onChange(fields.filter((_, j) => j !== i));
  const duplicate = (i) => {
    const source = fields[i];
    const copy = createField(source.Type, { ...source, uid: undefined, Key: source.Key ? source.Key + '_copy' : '', Options: [...(source.Options || [])] });
    const next = fields.slice();
    next.splice(i + 1, 0, { ...copy, uid: createField().uid });
    onChange(next);
  };
  const move = (from, to) => onChange(moveItem(fields, from, to));

  return (
    <div className="field-builder">
      {label ? <div className="section-label">{label}</div> : null}
      {fields.length === 0 ? <div className="muted field-builder-empty">{t('fieldBuilder.empty')}</div> : null}
      <ol className="field-list" aria-label={label || t('fieldBuilder.title')}>
        {fields.map((f, i) => (
          <FieldRow
            key={f.uid}
            field={f}
            index={i}
            count={fields.length}
            errors={errors[f.uid]}
            disabled={disabled}
            onUpdate={(next) => update(i, next)}
            onRemove={() => remove(i)}
            onDuplicate={() => duplicate(i)}
            onMove={move}
            dragHandlers={
              disabled
                ? {}
                : {
                    draggable: true,
                    onDragStart: (e) => {
                      if (e.target.closest('input, textarea, select')) {
                        e.preventDefault();
                        return;
                      }
                      setDragIndex(i);
                      e.dataTransfer.effectAllowed = 'move';
                    },
                    onDragOver: (e) => {
                      if (dragIndex !== null) e.preventDefault();
                    },
                    onDrop: (e) => {
                      e.preventDefault();
                      if (dragIndex !== null) move(dragIndex, i);
                      setDragIndex(null);
                    },
                    onDragEnd: () => setDragIndex(null)
                  }
            }
          />
        ))}
      </ol>
      <div className="field-builder-add">
        <label htmlFor={typeId} className="sr-only">
          {t('fieldBuilder.newFieldType')}
        </label>
        <select id={typeId} className="select-compact" value={newType} onChange={(e) => setNewType(e.target.value)} disabled={disabled}>
          {FIELD_TYPES.map((ft) => (
            <option key={ft} value={ft}>
              {t('enums.fieldType.' + ft)}
            </option>
          ))}
        </select>
        <button type="button" className="button button-secondary button-sm" onClick={() => onChange([...fields, createField(newType)])} disabled={disabled}>
          <Icon name="plus" />
          {t('fieldBuilder.add')}
        </button>
      </div>
    </div>
  );
}

export default FieldBuilder;
