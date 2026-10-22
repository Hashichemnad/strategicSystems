import { ClipboardCheck } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { EmptyState, PageHeader } from '@/ui';

/** My Work home page (SPEC §0.3). Cards from each system are added from Phase 4 on. */
export function HomePage() {
  const { t } = useTranslation('shell');
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('home.title')} description={t('home.description')} />
      <EmptyState
        icon={ClipboardCheck}
        title={t('home.emptyTitle')}
        description={t('home.emptyDescription')}
      />
    </div>
  );
}
