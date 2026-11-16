import { useCallback, useEffect, useId, useMemo, useRef, useState } from 'react';
import { useAuth } from '../context/AuthContext';
import { useToast } from '../context/ToastContext';
import { useHealth } from '../components/DashboardLayout';
import { useLocale } from '../i18n';
import { formatBytes, formatDuration } from '../i18n/formatters';
import PageHeader from '../components/PageHeader';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewer from '../components/JsonViewer';
import Icon from '../components/Icon';
import LanguageSelector from '../components/LanguageSelector';
import { AppearancePanel } from '../components/AppearancePicker';
import { EmptyState, ErrorBanner, LoadingState, Notice } from '../components/States';
import { getPath, isMasked, normalizeForSave, presentSections, setPath, validateSettings } from '../utils/settingsSchema';
import { describeError } from '../utils/errors';

const RESTART_POLL_MS = 1500;
const RESTART_GIVE_UP_MS = 90000;

function metaKey(path) {
  // Settings metadata uses "[]" for array items: Inference.Endpoints[].ApiKey
  return path.replace(/\.(\d+)\./g, '[].');
}

function FieldBadges({ meta }) {
  const { t } = useLocale();
  if (!meta) return null;
  return (
    <span className="field-badges">
      {meta.ApplyMode === 'Restart' ? (
        <span className="badge badge-warning badge-xs" title={t('settings.badges.restartHelp')}>
          {t('settings.badges.restart')}
        </span>
      ) : (
        <span className="badge badge-success badge-xs" title={t('settings.badges.liveHelp')}>
          {t('settings.badges.live')}
        </span>
      )}
      {meta.OverriddenByEnvironment ? (
        <span className="badge badge-info badge-xs" title={t('settings.badges.envHelp', { name: meta.EnvironmentVariable || '' })}>
          {t('settings.badges.env', { name: meta.EnvironmentVariable || '' })}
        </span>
      ) : null}
    </span>
  );
}

/** Masked secret control: shows set/not set with last four; only sends a new value when changed. */
function SecretInput({ id, value, original, onChange, disabled, nullable }) {
  const { t } = useLocale();
  const [editing, setEditing] = useState(!isMasked(value));
  const [reveal, setReveal] = useState(false);
  if (!editing && isMasked(value)) {
    return (
      <div className="secret-display">
        <span className="badge badge-neutral">{t('settings.secret.set', { last4: value.slice(4) })}</span>
        <button type="button" className="button button-secondary button-sm" onClick={() => { setEditing(true); onChange(''); }} disabled={disabled}>
          {t('settings.secret.change')}
        </button>
        {nullable ? (
          <button type="button" className="button button-link button-sm" onClick={() => { setEditing(true); onChange(null); }} disabled={disabled}>
            {t('settings.secret.clear')}
          </button>
        ) : null}
      </div>
    );
  }
  return (
    <div className="input-with-button">
      <input id={id} type={reveal ? 'text' : 'password'} className="mono" value={value || ''} onChange={(e) => onChange(e.target.value)} disabled={disabled} autoComplete="new-password" placeholder={nullable ? t('settings.secret.notSet') : ''} />
      <button type="button" className="icon-button" onClick={() => setReveal((r) => !r)} aria-label={reveal ? t('login.hideKey') : t('login.showKey')} title={reveal ? t('login.hideKey') : t('login.showKey')}>
        <Icon name={reveal ? 'lock' : 'eye'} />
      </button>
      {isMasked(original) ? (
        <button type="button" className="button button-link button-sm" onClick={() => { setEditing(false); setReveal(false); onChange(original); }} disabled={disabled}>
          {t('settings.secret.keep')}
        </button>
      ) : null}
    </div>
  );
}

function FieldControl({ field, path, value, original, onChange, disabled, error, meta, endpointNames }) {
  const { t, locale } = useLocale();
  const id = useId();
  const labelKey = 'settings.fields.' + (field.path || field.labelPath);
  const env = meta && meta.OverriddenByEnvironment;
  const locked = disabled || env;
  let control;
  switch (field.type) {
    case 'bool':
      control = (
        <label className="toggle-row">
          <input id={id} type="checkbox" checked={!!value} onChange={(e) => onChange(e.target.checked)} disabled={locked} />
          <span>{value ? t('common.enabled') : t('common.disabled')}</span>
        </label>
      );
      break;
    case 'select':
      control = (
        <select id={id} value={value ?? ''} onChange={(e) => onChange(e.target.value)} disabled={locked}>
          {field.options.map((o) => (
            <option key={o} value={o}>
              {field.optionLabel ? t('settings.' + field.optionLabel + '.' + o) : t('settings.options.' + o, { defaultValue: String(o) })}
            </option>
          ))}
        </select>
      );
      break;
    case 'endpointRef':
      control = (
        <select id={id} value={value ?? ''} onChange={(e) => onChange(e.target.value)} disabled={locked}>
          <option value="">{t('settings.noEndpoint')}</option>
          {endpointNames(field.kind).map((n) => (
            <option key={n} value={n}>
              {n}
            </option>
          ))}
          {value && !endpointNames(field.kind).includes(value) ? <option value={value}>{value}</option> : null}
        </select>
      );
      break;
    case 'secret':
      control = <SecretInput id={id} value={value} original={original} onChange={onChange} disabled={locked} nullable={field.nullable} />;
      break;
    case 'number':
    case 'nullableNumber':
      control = (
        <input
          id={id}
          type="number"
          min={field.min}
          max={field.max}
          step={field.step || (field.float ? 'any' : 1)}
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value === '' ? (field.type === 'nullableNumber' ? null : '') : e.target.value)}
          onBlur={() => {
            if (value === '' || value === null) return;
            let n = Number(value);
            if (!Number.isFinite(n)) return;
            if (field.min !== undefined) n = Math.max(field.min, n);
            if (field.max !== undefined) n = Math.min(field.max, n);
            if (String(n) !== String(value)) onChange(n);
          }}
          disabled={locked}
          aria-invalid={!!error || undefined}
          placeholder={field.type === 'nullableNumber' ? t('settings.unset') : ''}
        />
      );
      break;
    default:
      control = (
        <input
          id={id}
          className={/Path|Directory|Filename|Endpoint|Hostname/.test(path) ? 'mono' : ''}
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value)}
          disabled={locked}
          maxLength={field.maxLength}
          aria-invalid={!!error || undefined}
          placeholder={field.type === 'nullableText' ? t('settings.unset') : ''}
        />
      );
  }
  let hint = null;
  if ((field.type === 'number' || field.type === 'nullableNumber') && field.min !== undefined) {
    const fmt = (n) => (field.unit === 'bytes' ? formatBytes(n, locale) : field.unit === 'ms' ? formatDuration(n, locale) : String(n));
    hint = t('settings.range', { min: fmt(field.min), max: fmt(field.max) });
  }
  return (
    <div className={'form-field settings-field' + (field.wide ? ' field-grid-wide' : '') + (field.type === 'bool' ? ' settings-field-bool' : '')}>
      <label htmlFor={id} className="field-label">
        <span>{t(labelKey)}</span>
        <FieldBadges meta={meta} />
      </label>
      {control}
      {hint ? <p className="help-text">{hint}</p> : null}
      {env ? <p className="help-text text-info">{t('settings.envOverride', { name: meta.EnvironmentVariable || '' })}</p> : null}
      {error ? (
        <div className="field-error" role="alert">
          {t(error)}
        </div>
      ) : null}
    </div>
  );
}

/** Form-based editor for the server settings file with live/restart annotations and restart. */
function SettingsView() {
  const { t } = useLocale();
  const { apiClient, isAdmin, refreshPrincipal } = useAuth();
  const { refreshHealth } = useHealth();
  const { notify } = useToast();
  const [loaded, setLoaded] = useState(null);
  const [draft, setDraft] = useState(null);
  const [loadError, setLoadError] = useState(null);
  const [saveError, setSaveError] = useState(null);
  const [saving, setSaving] = useState(false);
  const [showErrors, setShowErrors] = useState(false);
  const [restartRequired, setRestartRequired] = useState(false);
  const [confirmRestart, setConfirmRestart] = useState(false);
  const [restartError, setRestartError] = useState(null);
  const [restartState, setRestartState] = useState(null); // null | 'waiting' | 'back' | 'timeout'
  const [showRaw, setShowRaw] = useState(false);
  const restartTimer = useRef(null);

  const load = useCallback(async () => {
    setLoadError(null);
    try {
      const r = await apiClient.getSettings();
      setLoaded(r);
      setDraft(r.Settings);
      setRestartRequired(!!r.RestartRequired);
    } catch (err) {
      setLoadError(err);
    }
  }, [apiClient]);

  useEffect(() => {
    if (isAdmin) load();
  }, [isAdmin, load]);

  useEffect(() => () => clearTimeout(restartTimer.current), []);

  useEffect(() => {
    if (!draft) return;
    const hash = window.location.hash.replace('#', '');
    if (hash) {
      const el = document.getElementById('settings-' + hash);
      el && el.scrollIntoView({ block: 'start' });
    }
  }, [draft]);

  const meta = useMemo(() => {
    const m = {};
    for (const f of (loaded && loaded.Fields) || []) m[f.Path] = f;
    return m;
  }, [loaded]);

  const errors = useMemo(() => (draft ? validateSettings(draft) : {}), [draft]);
  const errorCount = Object.keys(errors).length;
  const dirty = useMemo(() => loaded && draft && JSON.stringify(loaded.Settings) !== JSON.stringify(draft), [loaded, draft]);
  const busy = saving || !!restartState;

  const endpointNames = (kind) => ((draft && draft.Inference && draft.Inference.Endpoints) || []).filter((e) => e.Name && (!kind || e.Kind === kind)).map((e) => e.Name);

  const update = (path, value) => setDraft((d) => setPath(d, path, value));

  const save = async () => {
    setShowErrors(true);
    if (errorCount) return;
    setSaving(true);
    setSaveError(null);
    try {
      const r = await apiClient.updateSettings(normalizeForSave(draft));
      setLoaded(r);
      setDraft(r.Settings);
      setShowErrors(false);
      if (r.RestartRequired) setRestartRequired(true);
      notify('success', r.RestartRequired ? 'settings.toast.savedRestart' : 'settings.toast.savedLive');
    } catch (err) {
      setSaveError(err);
    } finally {
      setSaving(false);
    }
  };

  const pollUntilBack = useCallback(
    (startedAt, sawDown) => {
      restartTimer.current = setTimeout(async () => {
        let up = false;
        try {
          await apiClient.getHealth();
          up = true;
        } catch {
          up = false;
        }
        const elapsed = Date.now() - startedAt;
        if (up && (sawDown || elapsed > 5000)) {
          setRestartState('back');
          setRestartRequired(false);
          try {
            await refreshPrincipal();
          } catch {
            // The key may have changed with the new settings; the 401 handler signs out.
          }
          await load();
          refreshHealth();
          notify('success', 'settings.toast.restarted');
          setTimeout(() => setRestartState(null), 1500);
          return;
        }
        if (elapsed > RESTART_GIVE_UP_MS) {
          setRestartState('timeout');
          return;
        }
        pollUntilBack(startedAt, sawDown || !up);
      }, RESTART_POLL_MS);
    },
    [apiClient, load, refreshHealth, refreshPrincipal, notify]
  );

  const restart = async () => {
    setRestartError(null);
    try {
      await apiClient.restartServer();
    } catch (err) {
      // A dropped connection right after the 202 is expected; anything with a status is real.
      if (!err.network) {
        setRestartError(describeError(t, err));
        return;
      }
    }
    setConfirmRestart(false);
    setRestartState('waiting');
    pollUntilBack(Date.now(), false);
  };

  if (!isAdmin) {
    return (
      <div className="view">
        <PageHeader title={t('settings.title')} summary={t('settings.summary')} />
        <EmptyState icon="lock" title={t('settings.notAdminTitle')} message={t('settings.notAdminMessage')} />
        <section className="panel">
          <h2 className="panel-title">{t('settings.preferences')}</h2>
          <p className="help-text">{t('settings.preferencesHelp')}</p>
          <LanguageSelector />
          <AppearancePanel />
        </section>
      </div>
    );
  }

  return (
    <div className="view">
      <PageHeader
        title={t('settings.title')}
        summary={t('settings.summary')}
        actions={
          <>
            <button type="button" className="button button-secondary" onClick={() => setShowRaw((s) => !s)} disabled={!draft}>
              <Icon name="code" />
              {showRaw ? t('settings.hideRaw') : t('settings.showRaw')}
            </button>
            <button type="button" className="button button-danger" onClick={() => { setRestartError(null); setConfirmRestart(true); }} disabled={busy}>
              <Icon name="power" />
              {t('settings.restart')}
            </button>
          </>
        }
      >
        {loaded && loaded.SettingsFile ? (
          <p className="muted">
            {t('settings.file')}: <code className="mono break-all">{loaded.SettingsFile}</code>
          </p>
        ) : null}
      </PageHeader>

      {restartState === 'waiting' ? (
        <Notice tone="warning" icon="refresh" title={t('settings.restarting')}>
          <span className="spinner spinner-inline" aria-hidden="true" /> {t('settings.restartingMessage')}
        </Notice>
      ) : null}
      {restartState === 'back' ? <Notice tone="success" icon="check" title={t('settings.restartDone')} /> : null}
      {restartState === 'timeout' ? (
        <Notice
          tone="danger"
          icon="alert"
          title={t('settings.restartTimeout')}
          actions={
            <button type="button" className="button button-secondary button-sm" onClick={() => { setRestartState('waiting'); pollUntilBack(Date.now(), true); }}>
              {t('common.retry')}
            </button>
          }
        >
          {t('settings.restartTimeoutMessage')}
        </Notice>
      ) : null}
      {restartRequired && !restartState ? (
        <Notice
          tone="warning"
          icon="alert"
          title={t('settings.restartRequired')}
          actions={
            <button type="button" className="button button-danger button-sm" onClick={() => setConfirmRestart(true)}>
              {t('settings.restartNow')}
            </button>
          }
        >
          {t('settings.restartRequiredMessage')}
        </Notice>
      ) : null}
      {loadError ? <ErrorBanner error={loadError} onRetry={load} /> : null}
      {!draft && !loadError ? <LoadingState /> : null}
      {saveError ? <ErrorBanner error={saveError} titleKey="settings.saveFailed" /> : null}
      {showRaw && loaded ? (
        <section className="panel">
          <h2 className="panel-title">{t('settings.rawTitle')}</h2>
          <p className="help-text">{t('settings.rawHelp')}</p>
          <JsonViewer value={loaded.Settings} maxHeight="50vh" />
        </section>
      ) : null}

      {draft ? (
        <div className="settings-layout">
          <nav className="settings-toc" aria-label={t('settings.sections')}>
            {presentSections(draft).map((s) => (
              <a key={s.id} href={'#' + s.id} onClick={(e) => { e.preventDefault(); document.getElementById('settings-' + s.id).scrollIntoView({ behavior: 'smooth', block: 'start' }); window.history.replaceState(null, '', '#' + s.id); }}>
                {t('settings.groups.' + s.id + '.title')}
              </a>
            ))}
            <a href="#preferences" onClick={(e) => { e.preventDefault(); document.getElementById('settings-preferences').scrollIntoView({ behavior: 'smooth' }); }}>
              {t('settings.preferences')}
            </a>
          </nav>
          <form
            className="settings-form"
            onSubmit={(e) => {
              e.preventDefault();
              save();
            }}
            noValidate
          >
            {presentSections(draft).map((section) => (
              <fieldset key={section.id} id={'settings-' + section.id} className="panel settings-section" disabled={busy}>
                <legend className="panel-title">{t('settings.groups.' + section.id + '.title')}</legend>
                <p className="help-text">{t('settings.groups.' + section.id + '.help')}</p>
                <div className="settings-grid">
                  {section.fields
                    .filter((f) => !f.showIf || f.showIf(draft))
                    .map((f) => (
                      <FieldControl
                        key={f.path}
                        field={f}
                        path={f.path}
                        value={getPath(draft, f.path)}
                        original={getPath(loaded.Settings, f.path)}
                        onChange={(v) => update(f.path, v)}
                        disabled={busy}
                        error={showErrors ? errors[f.path] : null}
                        meta={meta[f.path]}
                        endpointNames={endpointNames}
                      />
                    ))}
                </div>
                {(section.arrays || []).map((arr) => {
                  const items = getPath(draft, arr.path) || [];
                  const originals = getPath(loaded.Settings, arr.path) || [];
                  return (
                    <div key={arr.path} className="settings-array">
                      <div className="section-label">{t('settings.fields.' + arr.path + '._')}</div>
                      {items.length === 0 ? <p className="muted">{t('settings.arrayEmpty')}</p> : null}
                      {items.map((item, i) => (
                        <div key={i} className="settings-array-item">
                          <div className="settings-array-head">
                            <strong>{(arr.titleField && item[arr.titleField]) || item.Name || item.Hostname || t('settings.newItem')}</strong>
                            <button
                              type="button"
                              className="button button-link button-sm danger"
                              onClick={() => update(arr.path, items.filter((_, j) => j !== i))}
                              disabled={busy}
                            >
                              <Icon name="trash" />
                              {t('settings.removeItem')}
                            </button>
                          </div>
                          <div className="settings-grid">
                            {arr.itemFields.map((f) => {
                              const p = arr.path + '.' + i + '.' + f.name;
                              return (
                                <FieldControl
                                  key={p}
                                  field={{ ...f, labelPath: arr.path + '.' + f.name }}
                                  path={p}
                                  value={item[f.name]}
                                  original={originals[i] ? originals[i][f.name] : undefined}
                                  onChange={(v) => update(arr.path, items.map((x, j) => (j === i ? { ...x, [f.name]: v } : x)))}
                                  disabled={busy}
                                  error={showErrors ? errors[p] : null}
                                  meta={meta[metaKey(p)]}
                                  endpointNames={endpointNames}
                                />
                              );
                            })}
                          </div>
                        </div>
                      ))}
                      <button type="button" className="button button-secondary button-sm" onClick={() => update(arr.path, [...items, arr.newItem()])} disabled={busy}>
                        <Icon name="plus" />
                        {t('settings.addItem.' + arr.path)}
                      </button>
                    </div>
                  );
                })}
              </fieldset>
            ))}
            <section id="settings-preferences" className="panel">
              <h2 className="panel-title">{t('settings.preferences')}</h2>
              <p className="help-text">{t('settings.preferencesHelp')}</p>
              <LanguageSelector />
              <AppearancePanel />
            </section>
            <div className="form-actions sticky-actions">
              {showErrors && errorCount ? (
                <span className="field-error" role="alert">
                  {t('settings.errors.summary', { count: errorCount })}
                </span>
              ) : null}
              {dirty ? <span className="muted">{t('settings.unsaved')}</span> : <span className="muted">{t('settings.noChanges')}</span>}
              <button type="button" className="button button-secondary" onClick={() => { setDraft(loaded.Settings); setShowErrors(false); setSaveError(null); }} disabled={!dirty || busy}>
                {t('settings.discard')}
              </button>
              <button type="submit" className="button button-primary" disabled={!dirty || busy}>
                <Icon name="save" />
                {saving ? t('common.saving') : t('common.save')}
              </button>
            </div>
          </form>
        </div>
      ) : null}

      <ConfirmModal
        open={confirmRestart}
        danger
        title={t('settings.restartTitle')}
        message={t('settings.restartMessage')}
        detail={dirty ? <span className="text-warning">{t('settings.restartUnsaved')}</span> : null}
        confirmLabel={t('settings.restart')}
        error={restartError}
        onCancel={() => setConfirmRestart(false)}
        onConfirm={restart}
      />
    </div>
  );
}

export default SettingsView;
