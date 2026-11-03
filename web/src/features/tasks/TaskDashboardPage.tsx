import { Link } from '@tanstack/react-router';
import { AlertTriangle, ArrowLeft } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { useLookupValues } from '@/features/settings/lookups';
import { formatNumber, pickText } from '@/lib/format';
import {
  EmptyState,
  PageHeader,
  Skeleton,
  StatusBanner,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { useTaskDashboard, useWorkloadInsights, type TaskDashboard } from './api';
import { defaultTasksSearch } from './api';
import { useStatusLabel } from './TaskChips';

type CountBy = TaskDashboard['byUnit'][number];

/** Horizontal bars (open, with the overdue share in red) — logical properties, so they grow from the start edge in both directions. */
function Bars({ title, rows, label }: { title: string; rows: CountBy[]; label: (key: string) => string }) {
  const { t, i18n } = useTranslation('tasks');
  const max = Math.max(1, ...rows.map((r) => r.open));
  return (
    <section className="flex flex-col gap-3 rounded-lg border bg-card p-5" aria-label={title}>
      <h2 className="font-semibold">{title}</h2>
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('dashboard.empty')}</p>
      ) : (
        <ul className="flex flex-col gap-2">
          {rows.map((row) => (
            <li
              key={row.key}
              className="grid grid-cols-[minmax(6rem,10rem)_1fr_auto] items-center gap-2 text-sm"
            >
              <span className="truncate" title={label(row.key)}>
                {label(row.key)}
              </span>
              <span className="flex h-4 overflow-hidden rounded bg-muted" aria-hidden>
                <span
                  className="h-full bg-sky-500"
                  style={{ inlineSize: `${((row.open - row.overdue) / max) * 100}%` }}
                />
                <span className="h-full bg-red-500" style={{ inlineSize: `${(row.overdue / max) * 100}%` }} />
              </span>
              <span className="tabular-nums text-muted-foreground">
                {formatNumber(row.open, i18n.language)}
                {row.overdue > 0 && (
                  <span className="text-red-600 dark:text-red-400">
                    {' '}
                    ({formatNumber(row.overdue, i18n.language)})
                  </span>
                )}
              </span>
            </li>
          ))}
        </ul>
      )}
      <p className="flex gap-4 text-xs text-muted-foreground">
        <span className="flex items-center gap-1">
          <span className="size-2 rounded-sm bg-sky-500" aria-hidden />
          {t('dashboard.legendOpen')}
        </span>
        <span className="flex items-center gap-1">
          <span className="size-2 rounded-sm bg-red-500" aria-hidden />
          {t('dashboard.legendOverdue')}
        </span>
      </p>
    </section>
  );
}

/** Task dashboard (SPEC §11.7): over the tasks the viewer may see. */
export function TaskDashboardPage() {
  const { t, i18n } = useTranslation('tasks');
  const { t: tc } = useTranslation();
  const dashboard = useTaskDashboard();
  const workloadAi = useAiFeature('task-workload');
  const insights = useWorkloadInsights(workloadAi);
  const sources = useLookupValues('tasks.source').data ?? [];
  const statusLabel = useStatusLabel();
  if (dashboard.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (dashboard.isError) return <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />;
  const d = dashboard.data;
  const hours = (value: number | null | undefined) =>
    value === null || value === undefined
      ? '—'
      : t('dashboard.hours', { value: formatNumber(value, i18n.language) });
  const tiles = [
    ['open', formatNumber(d.open, i18n.language)],
    ['overdue', formatNumber(d.overdue, i18n.language)],
    ['dueSoon', formatNumber(d.dueSoon, i18n.language)],
    ['closed90', formatNumber(d.closedLast90Days, i18n.language)],
    ['avgAccept', hours(d.averageHoursToAccept)],
    ['avgComplete', hours(d.averageHoursToComplete)],
    [
      'onTimeRate',
      d.onTimeRate === null ? '—' : `${formatNumber(Math.round(d.onTimeRate * 100), i18n.language)}%`,
    ],
    ['avgFeedback', d.averageFeedback === null ? '—' : formatNumber(d.averageFeedback, i18n.language)],
  ] as const;
  return (
    <div className="flex flex-col gap-6">
      <Link
        to="/tasks"
        search={{ ...defaultTasksSearch, tab: 'assigned' }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('title')}
      </Link>
      <PageHeader title={t('dashboard.title')} description={t('dashboard.description')} />
      <dl className="grid grid-cols-2 gap-3 md:grid-cols-4" data-testid="task-dashboard">
        {tiles.map(([key, value]) => (
          <div key={key} className="rounded-lg border bg-card p-4">
            <dt className="text-sm text-muted-foreground">{t(`dashboard.${key}`)}</dt>
            <dd
              className={`mt-1 text-2xl font-semibold tabular-nums ${key === 'overdue' && d.overdue > 0 ? 'text-red-700 dark:text-red-300' : ''}`}
            >
              {value}
            </dd>
          </div>
        ))}
      </dl>
      <div className="grid gap-4 lg:grid-cols-2">
        <Bars
          title={t('dashboard.byUnit')}
          rows={d.byUnit}
          label={(key) =>
            d.unitNames[key]
              ? pickText(i18n.language, d.unitNames[key].ar, d.unitNames[key].en)
              : t('dashboard.noUnit')
          }
        />
        <Bars
          title={t('dashboard.bySource')}
          rows={d.bySource}
          label={(key) => {
            const value = sources.find((s) => s.code === key);
            return value ? pickText(i18n.language, value.labelAr, value.labelEn) : key;
          }}
        />
        <Bars title={t('dashboard.byStatus')} rows={d.byStatus} label={(key) => statusLabel('full', key)} />
        <Bars
          title={t('dashboard.byPriority')}
          rows={d.byPriority}
          label={(key) => t(`priority.${key}` as 'priority.high', { defaultValue: key })}
        />
      </div>
      <section className="flex flex-col gap-3">
        <h2 className="font-semibold">{t('dashboard.workload')}</h2>
        {d.workload.length === 0 ? (
          <EmptyState title={t('dashboard.empty')} />
        ) : (
          <Table>
            <TableHead>
              <tr>
                <TableHeaderCell>{t('dashboard.employee')}</TableHeaderCell>
                <TableHeaderCell className="text-end">{t('dashboard.openCount')}</TableHeaderCell>
                <TableHeaderCell className="text-end">{t('dashboard.overdueCount')}</TableHeaderCell>
                <TableHeaderCell className="text-end">{t('dashboard.onHoldCount')}</TableHeaderCell>
              </tr>
            </TableHead>
            <TableBody>
              {d.workload.map((row) => (
                <TableRow key={row.employee.employeeId}>
                  <TableCell>{pickText(i18n.language, row.employee.nameAr, row.employee.nameEn)}</TableCell>
                  <TableCell className="text-end tabular-nums">
                    {formatNumber(row.open, i18n.language)}
                  </TableCell>
                  <TableCell className="text-end tabular-nums">
                    {formatNumber(row.overdue, i18n.language)}
                  </TableCell>
                  <TableCell className="text-end tabular-nums">
                    {formatNumber(row.onHold, i18n.language)}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </section>
      {workloadAi && (
        <section className="flex flex-col gap-3 rounded-lg border bg-card p-5">
          <h2 className="font-semibold">{t('dashboard.insights')}</h2>
          {insights.data?.length === 0 && (
            <p className="text-sm text-muted-foreground">{t('dashboard.insightsEmpty')}</p>
          )}
          <ul className="flex flex-col gap-2 text-sm">
            {insights.data?.map((insight) => (
              <li key={insight.employee.employeeId} className="flex items-center gap-2">
                <AlertTriangle className="size-4 text-amber-500" aria-hidden />
                <span className="font-medium">
                  {pickText(i18n.language, insight.employee.nameAr, insight.employee.nameEn)}
                </span>
                <span className="text-muted-foreground">
                  {insight.kind === 'overloaded'
                    ? t('dashboard.overloaded', { open: insight.open })
                    : t('dashboard.chronicOverdue', { late: insight.late, closed: insight.closedRecently })}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
