import { useId } from 'react';
import { useLocale } from '../i18n';
import { LOCALES } from '../i18n/localeRegistry';
import Icon from './Icon';

/** Locale selector showing autonyms. Used on login, in the topbar, and on Settings. */
function LanguageSelector({ compact = false }) {
  const { t, locale, setLocale } = useLocale();
  const id = useId();
  return (
    <div className={'language-selector' + (compact ? ' compact' : '')}>
      <label htmlFor={id} className={compact ? 'sr-only' : 'field-label'}>
        {t('common.language')}
      </label>
      {compact ? <Icon name="globe" /> : null}
      <select id={id} className="select-compact" value={locale} onChange={(e) => setLocale(e.target.value)} title={t('common.language')}>
        {LOCALES.map((l) => (
          <option key={l.code} value={l.code}>
            {l.nativeName}
          </option>
        ))}
      </select>
    </div>
  );
}

export default LanguageSelector;
