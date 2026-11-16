import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import PageHeader from '../components/PageHeader';
import { EmptyState } from '../components/States';

function NotFoundView() {
  const { t } = useTranslation();
  return (
    <div className="view">
      <PageHeader title={t('notFound.title')} />
      <EmptyState icon="alert" title={t('notFound.title')} message={t('notFound.message')}>
        <Link to="/dashboard/home" className="button button-primary">
          {t('notFound.home')}
        </Link>
      </EmptyState>
    </div>
  );
}

export default NotFoundView;
