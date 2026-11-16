import { useEffect, useId, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import Icon from './Icon';
import { useTheme } from '../context/ThemeContext';
import { PALETTES } from '../theme/palettes';
import { MODE_OPTIONS } from '../theme';
import { paletteSwatch } from '../theme/tokens';

const MODE_ICONS = { light: 'sun', dark: 'moon', system: 'monitor' };

/** Light, dark, or system mode as a segmented control. */
export function ModeSelector() {
  const { t } = useTranslation();
  const { mode, setMode } = useTheme();
  return (
    <div className="segmented mode-selector" role="radiogroup" aria-label={t('appearance.mode')}>
      {MODE_OPTIONS.map((m) => (
        <button key={m} type="button" role="radio" aria-checked={mode === m} className={'segment' + (mode === m ? ' active' : '')} onClick={() => setMode(m)}>
          <Icon name={MODE_ICONS[m]} size={14} />
          {t('appearance.modes.' + m)}
        </button>
      ))}
    </div>
  );
}

/** Palette swatch grid. Each swatch previews the palette in the currently resolved mode. */
export function PaletteGrid({ compact = false }) {
  const { t } = useTranslation();
  const { palette, setPalette, theme } = useTheme();
  return (
    <div className={'palette-grid' + (compact ? ' compact' : '')} role="radiogroup" aria-label={t('appearance.palette')}>
      {PALETTES.map((p) => {
        const s = paletteSwatch(p.id, theme);
        const active = palette === p.id;
        return (
          <button
            key={p.id}
            type="button"
            role="radio"
            aria-checked={active}
            className={'palette-swatch' + (active ? ' active' : '')}
            onClick={() => setPalette(p.id)}
            title={t('appearance.palettes.' + p.id)}
          >
            <span className="swatch-preview" style={{ background: s.bg, borderColor: s.surface }} aria-hidden="true">
              <span className="swatch-surface" style={{ background: s.surface }}>
                <span className="swatch-bar" style={{ background: s.primary }} />
                <span className="swatch-line" style={{ background: s.text }} />
                <span className="swatch-dots">
                  {s.chart.map((c, i) => (
                    <span key={i} style={{ background: c }} />
                  ))}
                </span>
              </span>
            </span>
            <span className="swatch-name">
              {active ? <Icon name="check" size={12} /> : null}
              {t('appearance.palettes.' + p.id)}
            </span>
          </button>
        );
      })}
    </div>
  );
}

/** Full appearance panel used on Settings: mode plus palette grid. */
export function AppearancePanel() {
  const { t } = useTranslation();
  return (
    <div className="appearance-panel">
      <div className="form-field">
        <span className="field-label">{t('appearance.mode')}</span>
        <ModeSelector />
      </div>
      <div className="form-field">
        <span className="field-label">{t('appearance.palette')}</span>
        <PaletteGrid />
        <p className="help-text">{t('appearance.help')}</p>
      </div>
    </div>
  );
}

/** Topbar button that opens a popover with the mode selector and palette grid. */
function AppearanceMenu() {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const ref = useRef(null);
  const panelId = useId();
  useEffect(() => {
    if (!open) return undefined;
    const close = (e) => ref.current && !ref.current.contains(e.target) && setOpen(false);
    const onKey = (e) => e.key === 'Escape' && setOpen(false);
    document.addEventListener('mousedown', close);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('mousedown', close);
      document.removeEventListener('keydown', onKey);
    };
  }, [open]);
  return (
    <div className="appearance-menu" ref={ref}>
      <button
        type="button"
        className="icon-button"
        aria-expanded={open}
        aria-controls={panelId}
        aria-label={t('appearance.open')}
        title={t('appearance.open')}
        onClick={() => setOpen((o) => !o)}
      >
        <Icon name="palette" size={18} />
      </button>
      {open ? (
        <div id={panelId} className="appearance-popover" role="dialog" aria-label={t('appearance.title')}>
          <div className="appearance-popover-head">
            <strong>{t('appearance.title')}</strong>
          </div>
          <ModeSelector />
          <PaletteGrid compact />
        </div>
      ) : null}
    </div>
  );
}

export default AppearanceMenu;
