import { Link } from '@tanstack/react-router';
import { AlertTriangle, ArrowLeft } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { useAiFeature } from '@/features/ai/api';
import { useLookupValues } from '@/features/settings/lookups';
import { formatNumber, pickText } from '@/lib/format';
import {
  barOption,
  Chart,
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
  Section,
  Tile,
  token,
} from '@/ui';
import { useTaskDashboard, useWorkloadInsights, type TaskDashboard } from './api';
import { defaultTasksSearch } from './api';
import { useStatusLabel } from './TaskChips';

type CountBy = TaskDashboard['byUnit'][number];
type TileKey =
  'open' | 'overdue' | 'dueSoon' | 'closed90' | 'avgAccept' | 'avgComplete' | 'onTimeRate' | 'avgFeedback';

/** Open tasks per key as horizontal stacked bars: on time and overdue (ECharts through the shared wrapper, ADR 0148). */
function Bars({ title, rows, label }: { title: string; rows: CountBy[]; label: (key: string) => string }) {
  const { t, i18n } = useTranslation('tasks');
  const f = (v: number) => formatNumber(v, i18n.language);
  return (
    <Section title={title}>
      {rows.length === 0 ? (
        <p className="text-sm text-muted-foreground">{t('dashboard.empty')}</p>
      ) : (
        <Chart
          label={title}
          height={Math.max(160, rows.length * 34 + 60)}
          {...barOption({
            orientation: 'horizontal',
            stacked: true,
            categories: rows.map((r) => ({ key: r.key, label: label(r.key) })),
            series: [
              {
                name: t('dashboard.legendOpen'),
                values: rows.map((r) => r.open - r.overdue),
                color: token('info'),
              },
              {
                name: t('dashboard.legendOverdue'),
                values: rows.map((r) => r.overdue),
                color: token('danger'),
              },
            ],
            integer: true,
            format: f,
          })}
        />
      )}
    </Section>
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
  const tiles: [TileKey, number | string, ('danger' | undefined)?][] = [
    ['open', d.open],
    ['overdue', d.overdue, d.overdue > 0 ? 'danger' : undefined],
    ['dueSoon', d.dueSoon],
    ['closed90', d.closedLast90Days],
    ['avgAccept', hours(d.averageHoursToAccept)],
    ['avgComplete', hours(d.averageHoursToComplete)],
    [
      'onTimeRate',
      d.onTimeRate === null ? '—' : `${formatNumber(Math.round(d.onTimeRate * 100), i18n.language)}%`,
    ],
    ['avgFeedback', d.averageFeedback === null ? '—' : d.averageFeedback],
  ];
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
      <div className="grid grid-cols-2 gap-3 md:grid-cols-4" data-testid="task-dashboard">
        {tiles.map(([key, value, tone]) => (
          <Tile key={key} label={t(`dashboard.${key}`)} value={value} tone={tone} />
        ))}
      </div>
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
