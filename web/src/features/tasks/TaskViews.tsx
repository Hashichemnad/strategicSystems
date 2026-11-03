import {
  DndContext,
  PointerSensor,
  KeyboardSensor,
  useDraggable,
  useDroppable,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import { Link } from '@tanstack/react-router';
import { ChevronLeft, ChevronRight, Users } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { formatDate, pickText } from '@/lib/format';
import { cn } from '@/lib/cn';
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useToast,
  type SortDirection,
  SortableHeader,
} from '@/ui';
import { useTaskAction, type TaskAction, type TaskListItem } from './api';
import { ConfidentialChip, IndicatorChip, PriorityChip, TaskStatusChip } from './TaskChips';
import { instantActions, TaskActionDialog } from './TaskActionDialog';

/** Quick actions of a row / card: instant ones run at once, the others open the action dialog. */
export function useQuickAction() {
  const { t } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const toast = useToast();
  const act = useTaskAction();
  const [dialog, setDialog] = useState<{ task: TaskListItem; action: TaskAction }>();
  const run = (task: TaskListItem, action: TaskAction) => {
    if (instantActions.has(action)) {
      act.mutate(
        { task, action },
        {
          onSuccess: () => toast({ title: t('toast.acted') }),
          onError: (failure) => toast({ tone: 'error', title: errorMessage(tc, failure) }),
        },
      );
    } else setDialog({ task, action });
  };
  const element = dialog && (
    <TaskActionDialog task={dialog.task} action={dialog.action} onClose={() => setDialog(undefined)} />
  );
  return { run, element, pending: act.isPending };
}

function TaskTitle({ task }: { task: TaskListItem }) {
  const { i18n } = useTranslation();
  return (
    <Link
      to="/tasks/$taskId"
      params={{ taskId: task.id }}
      search={{ tab: 'overview' }}
      className="font-medium text-primary hover:underline"
    >
      {pickText(i18n.language, task.titleAr, task.titleEn)}
    </Link>
  );
}

export function TaskTable({
  tasks,
  sortDirection,
  onSort,
  showAssignee = true,
  showCreator = true,
}: {
  tasks: TaskListItem[];
  sortDirection?: (field: string) => SortDirection;
  onSort?: (field: string) => void;
  showAssignee?: boolean;
  showCreator?: boolean;
}) {
  const { t, i18n } = useTranslation('tasks');
  const quick = useQuickAction();
  const header = (field: string, label: string) =>
    sortDirection && onSort ? (
      <SortableHeader direction={sortDirection(field)} onSort={() => onSort(field)}>
        {label}
      </SortableHeader>
    ) : (
      <TableHeaderCell>{label}</TableHeaderCell>
    );
  return (
    <>
      <Table>
        <TableHead>
          <tr>
            {header('number', t('fields.number'))}
            {header(i18n.language === 'ar' ? 'titleAr' : 'titleEn', t('fields.title'))}
            {showAssignee && <TableHeaderCell>{t('fields.assignee')}</TableHeaderCell>}
            {showCreator && <TableHeaderCell>{t('fields.creator')}</TableHeaderCell>}
            {header('status', t('fields.status'))}
            {header('priority', t('fields.priority'))}
            {header('dueDate', t('fields.dueDate'))}
            <TableHeaderCell className="text-end">{t('actions.open')}</TableHeaderCell>
          </tr>
        </TableHead>
        <TableBody>
          {tasks.map((task) => (
            <TableRow key={task.id} data-testid="task-row">
              <TableCell className="whitespace-nowrap tabular-nums" dir="ltr">
                {task.number}
              </TableCell>
              <TableCell>
                <div className="flex flex-wrap items-center gap-2">
                  <TaskTitle task={task} />
                  {task.groupId && (
                    <Users className="size-4 text-muted-foreground" aria-label={t('fields.group')} />
                  )}
                  {task.confidential && <ConfidentialChip />}
                </div>
              </TableCell>
              {showAssignee && (
                <TableCell>{pickText(i18n.language, task.assignee.nameAr, task.assignee.nameEn)}</TableCell>
              )}
              {showCreator && (
                <TableCell>{pickText(i18n.language, task.creator.nameAr, task.creator.nameEn)}</TableCell>
              )}
              <TableCell>
                <TaskStatusChip template={task.template} status={task.status} />
              </TableCell>
              <TableCell>
                <PriorityChip priority={task.priority} />
              </TableCell>
              <TableCell className="whitespace-nowrap">
                <div className="flex flex-col gap-1">
                  {task.dueDate ? formatDate(task.dueDate, i18n.language) : '—'}
                  <IndicatorChip indicator={task.indicator} />
                </div>
              </TableCell>
              <TableCell className="text-end">
                <div className="flex flex-wrap justify-end gap-1">
                  {task.actions
                    .filter((a) => a !== 'cancel' && a !== 'reassign')
                    .slice(0, 2)
                    .map((action) => (
                      <Button
                        key={action}
                        size="sm"
                        variant={action === 'reject' ? 'outline' : 'secondary'}
                        disabled={quick.pending}
                        onClick={() => quick.run(task, action as TaskAction)}
                      >
                        {t(`actions.${action}` as 'actions.accept')}
                      </Button>
                    ))}
                </div>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {quick.element}
    </>
  );
}

// ---- Kanban ------------------------------------------------------------------------------------------------------------------

type Column = 'new' | 'in_progress' | 'on_hold' | 'submitted' | 'closed';

const columnOf = (status: string): Column | undefined =>
  status === 'assigned' || status === 'to_do'
    ? 'new'
    : status === 'in_progress'
      ? 'in_progress'
      : status === 'on_hold'
        ? 'on_hold'
        : status === 'submitted'
          ? 'submitted'
          : status === 'approved' || status === 'done'
            ? 'closed'
            : undefined;

/** The transition a drop on a column means (only if the task offers it to the user). */
const targetOf: Record<string, Column> = {
  accept: 'in_progress',
  start: 'in_progress',
  return: 'in_progress',
  pause: 'on_hold',
  submit: 'submitted',
  complete: 'closed',
  approve: 'closed',
};

function KanbanCard({ task }: { task: TaskListItem }) {
  const { i18n } = useTranslation();
  const { attributes, listeners, setNodeRef, transform, isDragging } = useDraggable({
    id: task.id,
    data: task,
  });
  return (
    <li
      ref={setNodeRef}
      {...listeners}
      {...attributes}
      style={transform ? { transform: `translate3d(${transform.x}px, ${transform.y}px, 0)` } : undefined}
      className={cn(
        'flex cursor-grab flex-col gap-2 rounded-md border bg-card p-3 text-sm shadow-sm',
        isDragging && 'z-10 opacity-80 shadow-lg',
      )}
      data-testid="kanban-card"
    >
      <span className="text-xs text-muted-foreground" dir="ltr">
        {task.number}
      </span>
      <TaskTitle task={task} />
      <div className="flex flex-wrap items-center gap-1">
        <PriorityChip priority={task.priority} />
        <IndicatorChip indicator={task.indicator} />
      </div>
      <span className="text-xs text-muted-foreground">
        {pickText(i18n.language, task.assignee.nameAr, task.assignee.nameEn)}
        {task.dueDate ? ` · ${formatDate(task.dueDate, i18n.language)}` : ''}
      </span>
    </li>
  );
}

function KanbanColumn({ column, title, tasks }: { column: Column; title: string; tasks: TaskListItem[] }) {
  const { t } = useTranslation('tasks');
  const { setNodeRef, isOver } = useDroppable({ id: column });
  return (
    <section
      ref={setNodeRef}
      aria-label={title}
      className={cn(
        'flex min-w-64 flex-1 flex-col gap-2 rounded-lg border bg-muted/40 p-3',
        isOver && 'ring-2 ring-primary',
      )}
      data-testid={`kanban-column-${column}`}
    >
      <h3 className="flex items-center justify-between text-sm font-semibold">
        {title}
        <span className="rounded-full bg-background px-2 text-xs tabular-nums">{tasks.length}</span>
      </h3>
      {tasks.length === 0 ? (
        <p className="py-6 text-center text-xs text-muted-foreground">{t('kanban.empty')}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {tasks.map((task) => (
            <KanbanCard key={task.id} task={task} />
          ))}
        </ul>
      )}
    </section>
  );
}

/** Kanban by status (SPEC §11.7): drag a card to the next column to take that action (the server checks it). */
export function TaskKanban({ tasks }: { tasks: TaskListItem[] }) {
  const { t } = useTranslation('tasks');
  const toast = useToast();
  const quick = useQuickAction();
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor),
  );
  const columns: { key: Column; title: string }[] = [
    { key: 'new', title: `${t('status.full.assigned')} / ${t('status.full.to_do')}` },
    { key: 'in_progress', title: t('status.full.in_progress') },
    { key: 'on_hold', title: t('status.full.on_hold') },
    { key: 'submitted', title: t('status.full.submitted') },
    { key: 'closed', title: `${t('status.full.approved')} / ${t('status.full.done')}` },
  ];
  const onDragEnd = (event: DragEndEvent) => {
    const task = event.active.data.current as TaskListItem | undefined;
    const target = event.over?.id as Column | undefined;
    if (!task || !target || columnOf(task.status) === target) return;
    const action = task.actions.find((a) => targetOf[a] === target);
    if (action) quick.run(task, action as TaskAction);
    else toast({ tone: 'error', title: t('kanban.notAllowed') });
  };
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm text-muted-foreground">{t('kanban.dragHint')}</p>
      <DndContext sensors={sensors} onDragEnd={onDragEnd}>
        <div className="flex gap-3 overflow-x-auto pb-2" data-testid="kanban">
          {columns.map((column) => (
            <KanbanColumn
              key={column.key}
              column={column.key}
              title={column.title}
              tasks={tasks.filter((task) => columnOf(task.status) === column.key)}
            />
          ))}
        </div>
      </DndContext>
      {quick.element}
    </div>
  );
}

// ---- Calendar ----------------------------------------------------------------------------------------------------------------

function isoDate(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

/** Month view of due dates (SPEC §11.7). Weeks start on Monday; RTL mirrors the grid through the document direction. */
export function TaskCalendar({
  tasks,
  month,
  onMonthChange,
}: {
  tasks: TaskListItem[];
  month: string;
  onMonthChange: (month: string) => void;
}) {
  const { t, i18n } = useTranslation('tasks');
  const [year, monthIndex] = month.split('-').map(Number) as [number, number];
  const first = new Date(year, monthIndex - 1, 1);
  const offset = (first.getDay() + 6) % 7;
  const days = new Date(year, monthIndex, 0).getDate();
  const cells = useMemo(
    () =>
      Array.from({ length: Math.ceil((offset + days) / 7) * 7 }, (_, i) => {
        const day = i - offset + 1;
        return day >= 1 && day <= days ? new Date(year, monthIndex - 1, day) : undefined;
      }),
    [offset, days, year, monthIndex],
  );
  const byDay = new Map<string, TaskListItem[]>();
  for (const task of tasks) {
    if (!task.dueDate) continue;
    byDay.set(task.dueDate, [...(byDay.get(task.dueDate) ?? []), task]);
  }
  const weekdayFormat = new Intl.DateTimeFormat(i18n.language, { weekday: 'short' });
  const monthFormat = new Intl.DateTimeFormat(i18n.language, { month: 'long', year: 'numeric' });
  const shift = (delta: number) => {
    const date = new Date(year, monthIndex - 1 + delta, 1);
    onMonthChange(`${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}`);
  };
  const today = isoDate(new Date());
  const undated = tasks.filter((task) => !task.dueDate).length;
  return (
    <div className="flex flex-col gap-3" data-testid="task-calendar">
      <div className="flex items-center justify-between gap-2">
        <Button variant="outline" size="sm" onClick={() => shift(-1)} aria-label={t('calendar.previous')}>
          <ChevronLeft className="rtl:rotate-180" aria-hidden />
        </Button>
        <h2 className="text-lg font-semibold">{monthFormat.format(first)}</h2>
        <Button variant="outline" size="sm" onClick={() => shift(1)} aria-label={t('calendar.next')}>
          <ChevronRight className="rtl:rotate-180" aria-hidden />
        </Button>
      </div>
      <div className="grid grid-cols-7 gap-px overflow-hidden rounded-lg border bg-border text-sm">
        {Array.from({ length: 7 }, (_, i) => (
          <div key={i} className="bg-muted px-2 py-1 text-center text-xs font-medium text-muted-foreground">
            {weekdayFormat.format(new Date(2024, 0, 1 + i))}
          </div>
        ))}
        {cells.map((date, i) => {
          const key = date ? isoDate(date) : `empty-${i}`;
          const items = date ? (byDay.get(key) ?? []) : [];
          return (
            <div key={key} className={cn('min-h-24 bg-card p-1', !date && 'bg-muted/30')}>
              {date && (
                <>
                  <span
                    className={cn(
                      'inline-flex size-6 items-center justify-center rounded-full text-xs tabular-nums',
                      key === today && 'bg-primary text-primary-foreground',
                    )}
                  >
                    {new Intl.NumberFormat(i18n.language).format(date.getDate())}
                  </span>
                  <ul className="flex flex-col gap-1">
                    {items.slice(0, 3).map((task) => (
                      <li key={task.id}>
                        <Link
                          to="/tasks/$taskId"
                          params={{ taskId: task.id }}
                          search={{ tab: 'overview' }}
                          className={cn(
                            'block truncate rounded px-1 py-0.5 text-xs hover:underline',
                            task.indicator === 'overdue'
                              ? 'bg-red-100 text-red-900 dark:bg-red-950 dark:text-red-100'
                              : 'bg-primary/10 text-foreground',
                          )}
                          title={pickText(i18n.language, task.titleAr, task.titleEn)}
                        >
                          {pickText(i18n.language, task.titleAr, task.titleEn)}
                        </Link>
                      </li>
                    ))}
                    {items.length > 3 && (
                      <li className="px-1 text-xs text-muted-foreground">
                        {t('calendar.more', { count: items.length - 3 })}
                      </li>
                    )}
                  </ul>
                </>
              )}
            </div>
          );
        })}
      </div>
      {undated > 0 && (
        <p className="text-sm text-muted-foreground">{t('calendar.noDue', { count: undated })}</p>
      )}
    </div>
  );
}
