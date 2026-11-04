import { Link } from '@tanstack/react-router';
import type { EChartsOption } from 'echarts';
import { Sparkles } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature, type AiFeatureKey } from '@/features/ai/api';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { EChart, useRtl } from '@/ui/echart';
import {
  Button,
  EmptyState,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { PortfolioFilters } from './PortfolioFilters';
import {
  methodologyTone,
  overallRag,
  ragTone,
  usePortfolioDashboard,
  usePortfolioInsights,
  type DashboardProject,
  type HeatCell,
  type PortfolioDashboard,
  type PortfolioFilter,
} from './viewsApi';

/** Fixed RAG colours for the charts (ECharts needs concrete colours; readable on light and dark). */
const ragColour: Record<string, string> = {
  green: '#2f9e5b',
  amber: '#d08a1c',
  red: '#d64545',
  grey: '#8a94a6',
};
const dimensions = ['schedule', 'cost', 'scope', 'risk', 'overall'] as const;

/** Portfolio → Dashboard (docs/design/portfolio.md §38): system-level view, numbers scoped to what the viewer may read. */
export function PortfolioDashboardPage() {
  const { t } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const [filter, setFilter] = useState<PortfolioFilter>({});
  const dashboard = usePortfolioDashboard(filter);
  const insightsOn = useAiFeature('portfolio-insights' as AiFeatureKey);
  const insights = usePortfolioInsights();

  return (
    <div className="flex flex-col gap-6" data-testid="portfolio-dashboard">
      <PageHeader
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        actions={
          insightsOn ? (
            <Button
              variant="outline"
              loading={insights.isPending}
              data-testid="portfolio-insights"
              onClick={() => insights.mutate(filter)}
            >
              <Sparkles aria-hidden />
              {t('dashboard.insights')}
            </Button>
          ) : undefined
        }
      />
      <PortfolioFilters value={filter} onChange={setFilter} />
      {insights.isError && <StatusBanner tone="warning" title={errorMessage(tc, insights.error)} />}
      {insights.data && (
        <section
          className="flex flex-col gap-2 rounded-lg border border-info/30 bg-info-soft/40 p-4"
          data-testid="portfolio-insights-draft"
          aria-live="polite"
        >
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="font-semibold">{t('dashboard.insightsTitle')}</h2>
            <AiBadge />
          </div>
          {insights.data.narrative && (
            <p className="text-sm whitespace-pre-line">{insights.data.narrative}</p>
          )}
          {insights.data.recommendations.length > 0 && (
            <>
              <h3 className="text-sm font-medium">{t('dashboard.recommendations')}</h3>
              <ul className="list-disc ps-5 text-sm">
                {insights.data.recommendations.map((r, i) => (
                  <li key={i}>{r}</li>
                ))}
              </ul>
            </>
          )}
          <p className="text-xs text-muted-foreground">{t('dashboard.insightsHint')}</p>
        </section>
      )}
      {dashboard.isPending ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : dashboard.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, dashboard.error)} />
      ) : dashboard.data.total === 0 ? (
        <EmptyState title={t('dashboard.empty')} />
      ) : (
        <DashboardBody data={dashboard.data} />
      )}
    </div>
  );
}

function Card({
  title,
  children,
  testId,
  className,
}: {
  title: string;
  children: ReactNode;
  testId?: string;
  className?: string;
}) {
  return (
    <section
      className={`flex min-w-0 flex-col gap-3 rounded-lg border bg-card p-4 ${className ?? ''}`}
      data-testid={testId}
      aria-label={title}
    >
      <h2 className="font-semibold">{title}</h2>
      {children}
    </section>
  );
}

function DashboardBody({ data }: { data: PortfolioDashboard }) {
  const { t, i18n } = useTranslation('portfolioViews');
  const language = i18n.language;
  const rtl = useRtl();
  const n = (value: number) => formatNumber(value, language);
  const pct = (ratio: number) =>
    t('percent', { value: formatNumber(Math.round(ratio * 1000) / 10, language) });
  const unitName = (u: { unitAr: string | null; unitEn: string | null }) =>
    pickText(language, u.unitAr, u.unitEn) || t('dashboard.noUnit');
  const stageLabel = (code: string) =>
    t(`projectStages.${code}`, {
      ns: 'portfolio',
      defaultValue: t(`projectStatus.${code}`, { ns: 'portfolio', defaultValue: code }),
    });
  const projectName = (p: { nameAr: string | null; nameEn: string | null }) =>
    pickText(language, p.nameAr, p.nameEn);
  const legendSide = rtl ? { right: 0 } : { left: 0 };

  const bubbles = useMemo(
    () => data.projects.filter((p) => p.value !== null && p.complexity !== null && p.status === 'active'),
    [data.projects],
  );
  const bubbleOption = useMemo<EChartsOption>(() => {
    const budgets = bubbles.map((p) => p.budget ?? 0);
    const max = Math.max(...budgets, 1);
    const series = (['green', 'amber', 'red', 'grey'] as const).map((rag) => ({
      name: t(`rag.${rag}`),
      type: 'scatter' as const,
      itemStyle: { color: ragColour[rag], opacity: 0.8 },
      data: bubbles
        .filter((p) => (overallRag(p) ?? 'grey') === rag)
        .map((p) => ({
          value: [Math.round((p.value ?? 0) * 1000) / 10, p.complexity ?? 0, p.budget ?? 0],
          name: `${p.number} ${projectName(p)}`,
          symbolSize: p.budget === null ? 14 : 12 + Math.sqrt(p.budget / max) * 38,
        })),
    }));
    return {
      grid: { top: 40, bottom: 48, left: 56, right: 24, containLabel: true },
      legend: { top: 0, ...legendSide },
      tooltip: {
        trigger: 'item',
        formatter: (params: unknown) => {
          const p = params as { name: string; value: number[]; seriesName: string };
          return [
            p.name,
            `${t('dashboard.bubble.value')}: ${t('percent', { value: n(p.value[0] ?? 0) })}`,
            `${t('dashboard.bubble.complexity')}: ${n(p.value[1] ?? 0)}`,
            `${t('dashboard.bubble.budget')}: ${p.value[2] ? n(p.value[2]) : t('dashboard.hidden')}`,
            `${t('dashboard.bubble.health')}: ${p.seriesName}`,
          ].join('<br/>');
        },
      },
      xAxis: {
        type: 'value',
        name: t('dashboard.bubble.value'),
        nameLocation: 'middle',
        nameGap: 28,
        min: 0,
        max: 100,
        inverse: rtl,
      },
      yAxis: {
        type: 'value',
        name: t('dashboard.bubble.complexity'),
        min: 0,
        max: 5,
        interval: 1,
        position: rtl ? 'right' : 'left',
      },
      series,
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [bubbles, rtl, language]);

  const budgetOption = useMemo<EChartsOption>(
    () => ({
      grid: { top: 40, bottom: 24, left: 16, right: 16, containLabel: true },
      legend: { top: 0, ...legendSide },
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: data.budget.map(unitName),
        inverse: rtl,
        axisLabel: { interval: 0, rotate: 20 },
      },
      yAxis: { type: 'value', position: rtl ? 'right' : 'left' },
      series: [
        {
          name: t('dashboard.budget.budget'),
          type: 'bar',
          data: data.budget.map((b) => b.budget),
          itemStyle: { color: '#3b82c4' },
        },
        {
          name: t('dashboard.budget.actual'),
          type: 'bar',
          data: data.budget.map((b) => b.actual),
          itemStyle: { color: '#d08a1c' },
        },
      ],
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data.budget, rtl, language],
  );

  const stageOption = useMemo<EChartsOption>(
    () => ({
      grid: { top: 16, bottom: 24, left: 16, right: 16, containLabel: true },
      tooltip: { trigger: 'axis' },
      xAxis: {
        type: 'category',
        data: data.byStage.map((s) => stageLabel(s.stage)),
        inverse: rtl,
        axisLabel: { interval: 0 },
      },
      yAxis: { type: 'value', minInterval: 1, position: rtl ? 'right' : 'left' },
      series: [
        {
          name: t('dashboard.projects'),
          type: 'bar',
          data: data.byStage.map((s) => s.count),
          itemStyle: { color: '#2a9d8f' },
          label: { show: true, position: 'top' },
        },
      ],
    }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data.byStage, rtl, language],
  );

  const funnelSteps = [
    ['submitted', data.funnel.submitted],
    ['inApproval', data.funnel.inApproval],
    ['approved', data.funnel.approved],
    ['active', data.funnel.active],
    ['completed', data.funnel.completed],
  ] as const;
  const funnelMax = Math.max(...funnelSteps.map(([, v]) => v), 1);

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4" data-testid="portfolio-tiles">
        <Tile label={t('dashboard.tiles.total')} value={n(data.total)} />
        <Tile label={t('dashboard.tiles.active')} value={n(data.active)} />
        <Tile
          label={t('dashboard.tiles.delayed')}
          value={n(data.delayed)}
          tone={data.delayed > 0 ? 'danger' : undefined}
        />
        <Tile label={t('dashboard.tiles.averageProgress')} value={pct(data.averageProgress)} />
      </div>

      {data.healthOn && data.heat.length > 0 && (
        <Card title={t('dashboard.heat.title')} testId="portfolio-heatmap">
          <HeatMap cells={data.heat} unitName={unitName} />
        </Card>
      )}

      <div className="grid gap-4 lg:grid-cols-2">
        <Card title={t('dashboard.bubble.title')} testId="portfolio-bubble">
          {bubbles.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('dashboard.bubble.empty')}</p>
          ) : (
            <EChart
              option={bubbleOption}
              height={320}
              label={t('dashboard.bubble.aria', { count: bubbles.length })}
            />
          )}
          <p className="text-xs text-muted-foreground">{t('dashboard.bubble.hint')}</p>
        </Card>
        <Card title={t('dashboard.budget.title')} testId="portfolio-budget">
          {data.budget.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('dashboard.budget.empty')}</p>
          ) : (
            <EChart option={budgetOption} height={320} label={t('dashboard.budget.title')} />
          )}
          {data.costsHidden > 0 && (
            <p className="text-xs text-muted-foreground" data-testid="portfolio-costs-hidden">
              {t('dashboard.budget.hidden', { count: data.costsHidden })}
            </p>
          )}
        </Card>
        <Card title={t('dashboard.stages.title')} testId="portfolio-stages">
          <EChart option={stageOption} height={260} label={t('dashboard.stages.title')} />
        </Card>
        <Card title={t('dashboard.funnel.title')} testId="portfolio-funnel">
          <ol className="flex flex-col gap-2">
            {funnelSteps.map(([key, value]) => (
              <li key={key} className="flex items-center gap-3 text-sm">
                <span className="w-32 shrink-0 text-muted-foreground">{t(`dashboard.funnel.${key}`)}</span>
                <span className="relative h-6 flex-1 rounded bg-muted">
                  <span
                    className="absolute inset-y-0 start-0 rounded bg-primary/70"
                    style={{ width: `${(value / funnelMax) * 100}%` }}
                  />
                </span>
                <span className="w-12 shrink-0 text-end font-medium tabular-nums">{n(value)}</span>
              </li>
            ))}
          </ol>
        </Card>
        <Card title={t('dashboard.delayed.title')} testId="portfolio-delayed">
          {data.delayedProjects.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('dashboard.delayed.empty')}</p>
          ) : (
            <ul className="flex flex-col divide-y">
              {data.delayedProjects.map((p) => (
                <li key={p.id} className="flex items-center justify-between gap-3 py-2 text-sm">
                  <Link
                    to="/portfolio/projects/$projectId"
                    params={{ projectId: p.id }}
                    className="min-w-0 truncate text-primary hover:underline"
                  >
                    {p.number} · {projectName(p)}
                  </Link>
                  <span className="shrink-0 text-xs text-danger">
                    {t('dashboard.delayed.gap', { progress: pct(p.performance), planned: pct(p.planned) })}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>
        <Card title={t('dashboard.upcoming.title')} testId="portfolio-upcoming">
          {data.upcoming.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('dashboard.upcoming.empty')}</p>
          ) : (
            <ul className="flex flex-col divide-y">
              {data.upcoming.map((m) => (
                <li key={m.milestoneId} className="flex items-center justify-between gap-3 py-2 text-sm">
                  <div className="min-w-0">
                    <Link
                      to="/portfolio/projects/$projectId"
                      params={{ projectId: m.projectId }}
                      className="block truncate text-primary hover:underline"
                    >
                      {m.code} · {pickText(language, m.titleAr, m.titleEn)}
                    </Link>
                    <span className="block truncate text-xs text-muted-foreground">
                      {m.projectNumber} · {pickText(language, m.projectNameAr, m.projectNameEn)}
                    </span>
                  </div>
                  <span className="shrink-0 text-end text-xs">
                    {formatDate(m.plannedEnd, language)}
                    <span className="block text-muted-foreground">{pct(m.completion)}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}
        </Card>
      </div>

      <Card title={t('dashboard.table.title')} testId="portfolio-projects">
        <ProjectsTable projects={data.projects} healthOn={data.healthOn} />
      </Card>
    </>
  );
}

function Tile({ label, value, tone }: { label: string; value: string; tone?: 'danger' | undefined }) {
  return (
    <div className="rounded-lg border bg-card p-4">
      <p className="text-sm text-muted-foreground">{label}</p>
      <p className={`mt-1 text-2xl font-semibold tabular-nums ${tone === 'danger' ? 'text-danger' : ''}`}>
        {value}
      </p>
    </div>
  );
}

const cellTone: Record<string, string> = {
  red: 'bg-danger-soft text-danger',
  amber: 'bg-warning-soft text-warning',
  green: 'bg-success-soft text-success',
  grey: 'bg-muted text-muted-foreground',
};

/** Departments × dimensions with R / A / G counts: the letters carry the meaning, the colour only repeats the worst. */
function HeatMap({
  cells,
  unitName,
}: {
  cells: HeatCell[];
  unitName: (u: { unitAr: string | null; unitEn: string | null }) => string;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const n = (v: number) => formatNumber(v, i18n.language);
  const units = new Map<string, HeatCell[]>();
  for (const cell of cells) {
    const key = cell.unitId ?? '';
    units.set(key, [...(units.get(key) ?? []), cell]);
  }
  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('dashboard.heat.unit')}</TableHeaderCell>
            {dimensions.map((d) => (
              <TableHeaderCell key={d}>{t(`dimensions.${d}`)}</TableHeaderCell>
            ))}
          </TableRow>
        </TableHead>
        <TableBody>
          {[...units.entries()].map(([key, list]) => (
            <TableRow key={key || 'none'}>
              <TableCell className="font-medium">{list[0] ? unitName(list[0]) : ''}</TableCell>
              {dimensions.map((d) => {
                const cell = list.find((c) => c.dimension === d);
                if (!cell) return <TableCell key={d}>—</TableCell>;
                const worst = cell.red ? 'red' : cell.amber ? 'amber' : cell.green ? 'green' : 'grey';
                const text = t('dashboard.heat.cell', {
                  red: n(cell.red),
                  amber: n(cell.amber),
                  green: n(cell.green),
                });
                return (
                  <TableCell key={d} className="p-1">
                    <span
                      className={`block rounded px-2 py-1.5 text-center text-xs font-medium tabular-nums ${cellTone[worst]}`}
                      title={t('dashboard.heat.cellTitle', {
                        red: n(cell.red),
                        amber: n(cell.amber),
                        green: n(cell.green),
                        grey: n(cell.grey),
                      })}
                      data-testid={`heat-${d}`}
                    >
                      {text}
                    </span>
                  </TableCell>
                );
              })}
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <p className="mt-2 text-xs text-muted-foreground">{t('dashboard.heat.legend')}</p>
    </div>
  );
}

function ProjectsTable({ projects, healthOn }: { projects: DashboardProject[]; healthOn: boolean }) {
  const { t, i18n } = useTranslation('portfolioViews');
  const language = i18n.language;
  const pct = (ratio: number) =>
    t('percent', { value: formatNumber(Math.round(ratio * 1000) / 10, language) });
  return (
    <div className="overflow-x-auto">
      <Table>
        <TableHead>
          <TableRow>
            <TableHeaderCell>{t('dashboard.table.project')}</TableHeaderCell>
            <TableHeaderCell>{t('dashboard.table.unit')}</TableHeaderCell>
            <TableHeaderCell>{t('dashboard.table.methodology')}</TableHeaderCell>
            <TableHeaderCell>{t('dashboard.table.stage')}</TableHeaderCell>
            <TableHeaderCell>{t('dashboard.table.progress')}</TableHeaderCell>
            <TableHeaderCell>{t('dashboard.table.planned')}</TableHeaderCell>
            {healthOn && <TableHeaderCell>{t('dashboard.table.health')}</TableHeaderCell>}
          </TableRow>
        </TableHead>
        <TableBody>
          {projects.map((p) => {
            const rag = overallRag(p);
            return (
              <TableRow key={p.id}>
                <TableCell>
                  <Link
                    to="/portfolio/projects/$projectId"
                    params={{ projectId: p.id }}
                    className="text-primary hover:underline"
                  >
                    {p.number}
                  </Link>
                  <span className="block text-xs text-muted-foreground">
                    {pickText(language, p.nameAr, p.nameEn)}
                  </span>
                </TableCell>
                <TableCell>{p.unit ? pickText(language, p.unit.nameAr, p.unit.nameEn) : '—'}</TableCell>
                <TableCell>
                  <StatusChip tone={methodologyTone[p.methodology] ?? 'neutral'}>
                    {t(`methodology.${p.methodology}`, { defaultValue: p.methodology })}
                  </StatusChip>
                </TableCell>
                <TableCell>
                  {p.status === 'active'
                    ? t(`projectStages.${p.stage}`, { ns: 'portfolio', defaultValue: p.stage })
                    : t(`projectStatus.${p.status}`, { ns: 'portfolio', defaultValue: p.status })}
                </TableCell>
                <TableCell className="tabular-nums">
                  <span className={p.progressStatus === 'delayed' ? 'text-danger' : undefined}>
                    {pct(p.performance)}
                    {p.progressStatus === 'delayed' && ` · ${t('dashboard.table.delayed')}`}
                  </span>
                </TableCell>
                <TableCell className="tabular-nums">{pct(p.planned)}</TableCell>
                {healthOn && (
                  <TableCell>
                    {rag ? (
                      <StatusChip tone={ragTone[rag] ?? 'neutral'}>
                        {t(`rag.${rag}`, { defaultValue: rag })}
                      </StatusChip>
                    ) : (
                      '—'
                    )}
                  </TableCell>
                )}
              </TableRow>
            );
          })}
        </TableBody>
      </Table>
    </div>
  );
}
