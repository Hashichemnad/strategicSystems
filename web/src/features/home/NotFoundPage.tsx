import { Link } from '@tanstack/react-router';
import { SearchX } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Button, EmptyState } from '@/ui';

export function NotFoundPage() {
  const { t } = useTranslation('shell');
  return (
    <EmptyState
      icon={SearchX}
      title={t('notFound.title')}
      description={t('notFound.description')}
      action={
        <Button asChild variant="outline">
          <Link to="/">{t('notFound.goHome')}</Link>
        </Button>
      }
    />
  );
}
