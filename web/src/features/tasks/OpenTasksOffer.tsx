import { Link } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { useCan } from '@/features/access/session';
import { Button, StatusBanner } from '@/ui';
import { useReassignmentPreview } from './api';

/**
 * On an employee's page: when they still hold open tasks, the people who may reassign them see how many and a link to the action — in
 * warning tone once the employee is marked as left (ADR 0040).
 */
export function OpenTasksOffer({
  employeeId,
  name,
  status,
}: {
  employeeId: string;
  name: string;
  status: string;
}) {
  const { t } = useTranslation('tasks');
  const can = useCan('tasks.reassignment.manage');
  const preview = useReassignmentPreview(employeeId, can);
  const count = preview.data?.tasks.length ?? 0;
  if (!can || count === 0) return null;
  return (
    <StatusBanner
      tone={status === 'left' ? 'warning' : 'info'}
      title={t(status === 'left' ? 'reassign.offerLeft' : 'reassign.offer', { name, count })}
      actions={
        <Button size="sm" variant="outline" asChild>
          <Link to="/settings/tasks/reassign" search={{ employee: employeeId }} data-testid="reassign-offer">
            {t('reassign.offerAction')}
          </Link>
        </Button>
      }
    />
  );
}
