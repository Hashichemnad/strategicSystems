import { useQuery } from '@tanstack/react-query';
import { ListTodo, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api, unwrap } from '@/api/client';
import { useCan } from '@/features/access/session';
import { Button, EmptyState, Skeleton } from '@/ui';
import { useTasks } from './api';
import { NewTaskDialog } from './NewTaskDialog';
import { TaskTable } from './TaskViews';

/**
 * The "Tasks" tab of any task-able record (SPEC §11.6, links registry ADR 0034): the record's tasks the viewer may see, and "Create task"
 * with the record linked. Proven on the Sample item; later systems add the same panel.
 */
export function LinkedTasksPanel({ type, id }: { type: string; id: string }) {
  const { t } = useTranslation('tasks');
  const canCreate = useCan('tasks.task.create');
  const [creating, setCreating] = useState(false);
  const tasks = useTasks({ linkedType: type, linkedId: id, pageSize: 100, sort: 'dueDate' });
  const view = useQuery({
    queryKey: ['links', 'resolve', type, id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/links/resolve', { params: { query: { type, id } }, signal })),
  });
  return (
    <section className="flex flex-col gap-4" data-testid="linked-tasks">
      <header className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="font-semibold">{t('linked.title')}</h2>
          <p className="text-sm text-muted-foreground">{t('linked.description')}</p>
        </div>
        {canCreate && (
          <Button onClick={() => setCreating(true)}>
            <Plus aria-hidden />
            {t('linked.new')}
          </Button>
        )}
      </header>
      {!tasks.data ? (
        <Skeleton className="h-32" />
      ) : tasks.data.items.length === 0 ? (
        <EmptyState icon={ListTodo} title={t('linked.empty')} />
      ) : (
        <TaskTable tasks={tasks.data.items} />
      )}
      <NewTaskDialog open={creating} onOpenChange={setCreating} link={{ type, id, view: view.data }} />
    </section>
  );
}
