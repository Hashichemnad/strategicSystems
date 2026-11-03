import { Link } from '@tanstack/react-router';
import { ListTodo } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import type { components } from '@/api/schema';
import { formatDate, pickText } from '@/lib/format';
import { StatusChip } from '@/ui';
import { useTaskCounts } from './api';
import { defaultTasksSearch } from './api';

type MyWorkItem = components['schemas']['MyWorkItem'];

/** My Work → Tasks (SPEC §0.4 level 1): counts and the tasks that need the user (assigned, awaiting approval, overdue). */
export function MyTasksCard({ items }: { items: MyWorkItem[] }) {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const counts = useTaskCounts();
  const c = counts.data;
  return (
    <section
      className="flex flex-col gap-3 rounded-lg border bg-card p-5"
      aria-label={t('myWork.title')}
      data-testid="my-tasks-card"
    >
      <header className="flex items-center justify-between gap-2">
        <h2 className="flex items-center gap-2 font-semibold">
          <ListTodo className="size-5 text-primary" aria-hidden />
          {t('myWork.title')}
        </h2>
        <Link
          to="/tasks"
          search={{ ...defaultTasksSearch, tab: 'assigned' }}
          className="text-sm text-primary hover:underline"
        >
          {t('myWork.all')}
        </Link>
      </header>
      <div className="flex flex-wrap gap-2 text-sm">
        <StatusChip tone="info">
          {t('counts.open')}: {c?.assignedOpen ?? '—'}
        </StatusChip>
        <StatusChip tone={c && c.assignedOverdue > 0 ? 'danger' : 'neutral'}>
          {t('counts.overdue')}: {c?.assignedOverdue ?? '—'}
        </StatusChip>
        <StatusChip tone={c && c.awaitingMyApproval > 0 ? 'warning' : 'neutral'}>
          {t('counts.awaitingApproval')}: {c?.awaitingMyApproval ?? '—'}
        </StatusChip>
      </div>
      {items.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('myWork.empty')}</p>
      ) : (
        <ul className="flex flex-col divide-y">
          {items.slice(0, 8).map((item) => (
            <li key={item.resourceId} className="flex flex-col gap-0.5 py-3">
              <Link to={item.link as '/'} className="font-medium text-primary hover:underline">
                {[item.number, pickText(i18n.language, item.titleAr, item.titleEn)]
                  .filter(Boolean)
                  .join(' · ')}
              </Link>
              <span className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
                {translateMessageKey(tc, item.messageKey)}
                {item.dueDate && <span>· {formatDate(item.dueDate, i18n.language)}</span>}
              </span>
            </li>
          ))}
        </ul>
      )}
    </section>
  );
}
