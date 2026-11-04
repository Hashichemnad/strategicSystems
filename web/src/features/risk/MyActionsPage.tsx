import { getRouteApi } from '@tanstack/react-router';
import { ListChecks } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmptyState, PageHeader, Skeleton, StatusBanner, Switch } from '@/ui';
import { ActionTable, ProgressDialog, UpdatesDialog } from './ActionsTab';
import { useMyActions, type TreatmentAction } from './api';
import { RiskPageLayout } from './components';

const route = getRouteApi('/app/risk/my-actions');

/** My treatment actions (docs/design/risk.md §12) with a quick progress update; `?action=` highlights one (My Work, e-mails). */
export function RiskMyActionsPage() {
  const { t } = useTranslation('risk');
  const { t: tc } = useTranslation();
  const { action } = route.useSearch();
  const [includeCompleted, setIncludeCompleted] = useState(false);
  const actions = useMyActions(includeCompleted);
  const [progress, setProgress] = useState<TreatmentAction>();
  const [updates, setUpdates] = useState<TreatmentAction>();
  useEffect(() => {
    if (action && actions.data)
      document.querySelector('[data-highlighted="true"]')?.scrollIntoView({ block: 'center' });
  }, [action, actions.data]);
  return (
    <RiskPageLayout testId="risk-my-actions">
      <PageHeader
        title={t('myActions.title')}
        description={t('myActions.description')}
        actions={
          <Switch checked={includeCompleted} onCheckedChange={setIncludeCompleted} label={t('myActions.includeCompleted')} />
        }
      />
      {actions.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, actions.error)} />
      ) : !actions.data ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : actions.data.length === 0 ? (
        <EmptyState icon={ListChecks} title={t('myActions.empty')} />
      ) : (
        <ActionTable rows={actions.data} showRisk highlight={action} onProgress={setProgress} onUpdates={setUpdates} />
      )}
      {progress && <ProgressDialog action={progress} onClose={() => setProgress(undefined)} />}
      {updates && <UpdatesDialog action={updates} onClose={() => setUpdates(undefined)} />}
    </RiskPageLayout>
  );
}
