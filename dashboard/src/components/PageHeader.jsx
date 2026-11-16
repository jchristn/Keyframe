import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';

/** Route header: title, operator-facing summary, and actions. Also sets document.title. */
function PageHeader({ title, summary = null, actions = null, children = null }) {
  const { t } = useTranslation();
  useEffect(() => {
    document.title = t('app.documentTitle', { page: title });
  }, [t, title]);
  return (
    <header className="page-header">
      <div className="page-header-text">
        <h1 className="page-title">{title}</h1>
        {summary ? <p className="page-summary">{summary}</p> : null}
        {children}
      </div>
      {actions ? <div className="page-actions">{actions}</div> : null}
    </header>
  );
}

export default PageHeader;
