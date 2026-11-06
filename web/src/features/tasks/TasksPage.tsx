import { getRouteApi, Link } from '@tanstack/react-router';
import { CalendarDays, Columns3, LayoutDashboard, List, ListTodo, Plus, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useCan } from '@/features/access/session';
import { cn } from '@/lib/cn';
import {
  Button,
  Checkbox,
  EmptyState,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StatusBanner,
  Tabs,
  TabsList,
  TabsTrigger,
  type SortDirection,
} from '@/ui';
import { useTaskCounts, useTasks, type TaskListQuery } from './api';
import { NewTaskDialog } from './NewTaskDialog';
import { TaskCalendar, TaskKanban, TaskTable } from './TaskViews';

const route = getRouteApi('/app/tasks');
const PAGE_SIZE = 20;

function currentMonth(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`;
}

function monthRange(month: string): { dueFrom: string; dueTo: string } {
  const [year, m] = month.split('-').map(Number) as [number, number];
  const last = new Date(year, m, 0).getDate();
  return { dueFrom: `${month}-01`, dueTo: `${month}-${String(last).padStart(2, '0')}` };
}

/** My Tasks (SPEC §11.7): tabs by relation, counts, list / Kanban / calendar, quick actions. State lives in the URL. */
export function TasksPage() {
  const { t } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const { t: te } = useTranslation('errors');
  const search = route.useSearch();
  const navigate = route.useNavigate();
  const canDashboard = useCan('tasks.dashboard.read');
  const counts = useTaskCounts();
  const [searchText, setSearchText] = useState(search.search ?? '');
  const [creating, setCreating] = useState(search.new === true);
  const month = search.month ?? currentMonth();

  useEffect(() => {
    if (searchText === (search.search ?? '')) return;
    const timer = window.setTimeout(
      () => void navigate({ search: (prev) => ({ ...prev, search: searchText || undefined, page: 1 }) }),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [searchText, search.search, navigate]);

  const board = search.view !== 'list';
  const query: TaskListQuery = {
    view: search.tab,
    page: board ? 1 : search.page,
    pageSize: board ? 200 : PAGE_SIZE,
    sort: search.sort,
    ...(search.search ? { search: search.search } : {}),
    ...(search.status ? { status: search.status } : {}),
    ...(search.priority ? { priority: search.priority } : {}),
    ...(search.indicator ? { indicator: search.indicator } : {}),
    ...(search.view === 'calendar' ? monthRange(month) : {}),
    ...(search.view === 'list' && search.openOnly ? { openOnly: true } : {}),
  };
  const tasks = useTasks(query);

  const sortDirection = (field: string): SortDirection =>
    search.sort === field ? 'asc' : search.sort === `-${field}` ? 'desc' : null;
  const toggleSort = (field: string) =>
    void navigate({
      search: (prev) => ({ ...prev, sort: prev.sort === field ? `-${field}` : field, page: 1 }),
    });

  const c = counts.data;
  const tiles = [
    { key: 'open', value: c?.assignedOpen, tone: '' },
    { key: 'overdue', value: c?.assignedOverdue, tone: 'text-red-700 dark:text-red-300' },
    { key: 'awaitingApproval', value: c?.awaitingMyApproval, tone: 'text-sky-700 dark:text-sky-300' },
    { key: 'followUp', value: c?.followUp, tone: '' },
    { key: 'team', value: c?.team, tone: '' },
  ] as const;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('title')}
        description={t('description')}
        actions={
          <>
            {canDashboard && (
              <Button variant="outline" asChild>
                <Link to="/tasks/dashboard">
                  <LayoutDashboard aria-hidden />
                  {t('dashboardLink')}
                </Link>
              </Button>
            )}
            <Button onClick={() => setCreating(true)} data-testid="new-task">
              <Plus aria-hidden />
              {t('new')}
            </Button>
          </>
        }
      />

      <dl className="grid grid-cols-2 gap-3 sm:grid-cols-5" aria-busy={counts.isPending}>
        {tiles.map((tile) => (
          <div key={tile.key} className="rounded-lg border bg-card p-4">
            <dt className="text-sm text-muted-foreground">{t(`counts.${tile.key}`)}</dt>
            <dd className={cn('mt-1 text-2xl font-semibold tabular-nums', tile.tone)}>{tile.value ?? '—'}</dd>
          </div>
        ))}
      </dl>

      <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
        <Tabs
          value={search.tab}
          onValueChange={(value) =>
            void navigate({ search: (prev) => ({ ...prev, tab: value as 'assigned', page: 1 }) })
          }
        >
          <TabsList>
            {(['assigned', 'created', 'follow-up', 'team'] as const).map((tab) => (
              <TabsTrigger filter key={tab} value={tab}>
                {t(`tabs.${tab}`)}
              </TabsTrigger>
            ))}
          </TabsList>
        </Tabs>
        <div className="flex gap-1 rounded-md border p-1" role="group" aria-label={t('views.label')}>
          {(
            [
              ['list', List],
              ['kanban', Columns3],
              ['calendar', CalendarDays],
            ] as const
          ).map(([view, Icon]) => (
            <Button
              key={view}
              size="sm"
              variant={search.view === view ? 'secondary' : 'ghost'}
              aria-pressed={search.view === view}
              onClick={() => void navigate({ search: (prev) => ({ ...prev, view, page: 1 }) })}
            >
              <Icon aria-hidden />
              {t(`views.${view}`)}
            </Button>
          ))}
        </div>
      </div>

      <div className="flex flex-col gap-3 md:flex-row md:items-center">
        <div className="relative flex-1">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            type="search"
            value={searchText}
            onChange={(event) => setSearchText(event.target.value)}
            placeholder={t('filters.search')}
            aria-label={t('filters.search')}
            className="ps-9"
          />
        </div>
        <Select
          className="md:w-44"
          aria-label={t('fields.priority')}
          value={search.priority ?? 'all'}
          onValueChange={(value) =>
            void navigate({
              search: (prev) => ({
                ...prev,
                priority: value === 'all' ? undefined : (value as 'high'),
                page: 1,
              }),
            })
          }
          options={[
            { value: 'all', label: t('filters.allPriorities') },
            ...(['high', 'medium', 'low'] as const).map((p) => ({ value: p, label: t(`priority.${p}`) })),
          ]}
        />
        <Select
          className="md:w-44"
          aria-label={t('fields.indicator')}
          value={search.indicator ?? 'all'}
          onValueChange={(value) =>
            void navigate({
              search: (prev) => ({
                ...prev,
                indicator: value === 'all' ? undefined : (value as 'overdue'),
                page: 1,
              }),
            })
          }
          options={[
            { value: 'all', label: t('filters.allIndicators') },
            ...(['overdue', 'due_soon', 'on_time', 'on_hold'] as const).map((i) => ({
              value: i,
              label: t(`indicator.${i}`),
            })),
          ]}
        />
        {search.view === 'list' && (
          <Checkbox
            label={t('filters.openOnly')}
            checked={search.openOnly}
            onChange={(e) =>
              void navigate({ search: (prev) => ({ ...prev, openOnly: e.target.checked, page: 1 }) })
            }
          />
        )}
      </div>

      {tasks.isError ? (
        <StatusBanner tone="danger" title={te('title')} description={errorMessage(tc, tasks.error)} />
      ) : !tasks.data ? (
        <Skeleton className="h-64 w-full" />
      ) : search.view === 'kanban' ? (
        <TaskKanban tasks={tasks.data.items} />
      ) : search.view === 'calendar' ? (
        <TaskCalendar
          tasks={tasks.data.items}
          month={month}
          onMonthChange={(value) => void navigate({ search: (prev) => ({ ...prev, month: value }) })}
        />
      ) : tasks.data.items.length === 0 ? (
        <EmptyState icon={ListTodo} title={t('myWork.empty')} />
      ) : (
        <div className="flex flex-col gap-3">
          <TaskTable
            tasks={tasks.data.items}
            sortDirection={sortDirection}
            onSort={toggleSort}
            showAssignee={search.tab !== 'assigned'}
            showCreator={search.tab !== 'created'}
          />
          <Pagination
            page={tasks.data.page}
            totalPages={tasks.data.totalPages ?? 1}
            totalCount={tasks.data.totalCount}
            onPageChange={(page) => void navigate({ search: (prev) => ({ ...prev, page }) })}
          />
        </div>
      )}

      <NewTaskDialog open={creating} onOpenChange={setCreating} />
    </div>
  );
}
