import { Link, useNavigate } from '@tanstack/react-router';
import { Sparkles } from 'lucide-react';
import { useMemo, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useSystemDashboard } from '@/features/reporting/SystemDashboard';
import { errorMessage } from '@/api/problem';
import { AiBadge } from '@/features/ai/AiDraft';
import { useAiFeature, type AiFeatureKey } from '@/features/ai/api';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  barOption,
  bubbleOption,
  Button,
  Chart,
  EmptyState,
  funnelOption,
  RagGrid,
  token,
  Tile,
  type Rag,
  type RagGridCell,
  type RagGridRow,
  Skeleton,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  DashboardFrame,
  OptionalPeriodSelector,
  periodToDates,
  type PeriodValue,
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

/** Health RAG → chart tone (colours from the design tokens, light and dark). */
const ragChartTone = { green: 'success', amber: 'warning', red: 'danger', grey: 'neutral' } as const;
const dimensions = ['schedule', 'cost', 'scope', 'risk', 'overall'] as const;

/** Portfolio → Dashboard (docs/design/portfolio.md §38): system-level view, numbers scoped to what the viewer may read. */
export function PortfolioDashboardPage() {
  const { t } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const [filter, setFilter] = useState<PortfolioFilter>({});
  const [period, setPeriod] = useState<PeriodValue | null>(null);
  const range = period ? periodToDates(period) : undefined;
  const dashboard = usePortfolioDashboard(filter, range);
  const system = useSystemDashboard('portfolio.overview', period, filter.unitId);
  const insightsOn = useAiFeature('portfolio-insights' as AiFeatureKey);
  const insights = usePortfolioInsights();

  return (
    <DashboardFrame
      testId="portfolio-dashboard"
      title={t('dashboard.title')}
      description={t('dashboard.description')}
      exports={system.exports}
      filters={<OptionalPeriodSelector value={period} onChange={setPeriod} allowCompare={false} />}
      actions={
        <>
          {system.action}
          {insightsOn && (
            <Button
              variant="outline"
              loading={insights.isPending}
              data-testid="portfolio-insights"
              onClick={() => insights.mutate(filter)}
            >
              <Sparkles aria-hidden />
              {t('dashboard.insights')}
            </Button>
          )}
        </>
      }
    >
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
    </DashboardFrame>
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
  const navigate = useNavigate();
  const language = i18n.language;
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

  const bubbles = useMemo(
    () => data.projects.filter((p) => p.value !== null && p.complexity !== null && p.status === 'active'),
    [data.projects],
  );
  const bubble = useMemo(
    () =>
      bubbleOption({
        groups: (['green', 'amber', 'red', 'grey'] as const).map((rag) => ({
          name: t(`rag.${rag}`),
          color: token(ragChartTone[rag]),
          points: bubbles
            .filter((p) => (overallRag(p) ?? 'grey') === rag)
            .map((p) => ({
              key: p.id,
              label: `${p.number} ${projectName(p)}`,
              x: Math.round((p.value ?? 0) * 1000) / 10,
              y: p.complexity ?? 0,
              size: p.budget,
            })),
        })),
        x: {
          name: t('dashboard.bubble.value'),
          min: 0,
          max: 100,
          format: (v) => t('percent', { value: n(v) }),
        },
        y: { name: t('dashboard.bubble.complexity'), min: 0, max: 5, interval: 1, format: n },
        size: { name: t('dashboard.bubble.budget'), format: n, hidden: t('dashboard.hidden') },
        itemHeader: t('dashboard.table.project'),
        groupHeader: t('dashboard.bubble.health'),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [bubbles, language],
  );

  const budget = useMemo(
    () =>
      barOption({
        categories: data.budget.map((b, i) => ({ key: b.unitId ?? `none-${i}`, label: unitName(b) })),
        series: [
          {
            name: t('dashboard.budget.budget'),
            values: data.budget.map((b) => b.budget),
            color: token('info'),
          },
          {
            name: t('dashboard.budget.actual'),
            values: data.budget.map((b) => b.actual),
            color: token('warning'),
          },
        ],
        format: n,
        categoryHeader: t('dashboard.heat.unit'),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data.budget, language],
  );

  const stages = useMemo(
    () =>
      barOption({
        categories: data.byStage.map((s) => ({ key: s.stage, label: stageLabel(s.stage) })),
        series: [{ name: t('dashboard.projects'), values: data.byStage.map((s) => s.count) }],
        integer: true,
        format: n,
        categoryHeader: t('dashboard.table.stage'),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data.byStage, language],
  );

  const funnel = useMemo(
    () =>
      funnelOption({
        steps: (['submitted', 'inApproval', 'approved', 'active', 'completed'] as const).map((key) => ({
          key,
          label: t(`dashboard.funnel.${key}`),
          value: data.funnel[key],
        })),
        format: n,
        categoryHeader: t('dashboard.funnel.title'),
        valueHeader: t('dashboard.projects'),
      }),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [data.funnel, language],
  );

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4" data-testid="portfolio-tiles">
        <Tile label={t('dashboard.tiles.total')} value={data.total} />
        <Tile label={t('dashboard.tiles.active')} value={data.active} />
        <Tile
          label={t('dashboard.tiles.delayed')}
          value={data.delayed}
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
            <Chart
              {...bubble}
              height={320}
              label={t('dashboard.bubble.aria', { count: bubbles.length })}
              onClick={(e) =>
                e.key && void navigate({ to: '/portfolio/projects/$projectId', params: { projectId: e.key } })
              }
            />
          )}
          <p className="text-xs text-muted-foreground">{t('dashboard.bubble.hint')}</p>
        </Card>
        <Card title={t('dashboard.budget.title')} testId="portfolio-budget">
          {data.budget.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('dashboard.budget.empty')}</p>
          ) : (
            <Chart {...budget} height={320} label={t('dashboard.budget.title')} />
          )}
          {data.costsHidden > 0 && (
            <p className="text-xs text-muted-foreground" data-testid="portfolio-costs-hidden">
              {t('dashboard.budget.hidden', { count: data.costsHidden })}
            </p>
          )}
        </Card>
        <Card title={t('dashboard.stages.title')} testId="portfolio-stages">
          <Chart {...stages} height={260} label={t('dashboard.stages.title')} />
        </Card>
        <Card title={t('dashboard.funnel.title')} testId="portfolio-funnel">
          <Chart {...funnel} height={260} label={t('dashboard.funnel.title')} />
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
  const rows: RagGridRow[] = [...units.entries()].map(([key, list]) => {
    const name = list[0] ? unitName(list[0]) : '';
    const rowCells: Record<string, RagGridCell> = {};
    for (const d of dimensions) {
      const cell = list.find((c) => c.dimension === d);
      if (!cell) continue;
      const worst: Rag = cell.red ? 'red' : cell.amber ? 'amber' : cell.green ? 'green' : 'grey';
      const text = t('dashboard.heat.cell', { red: n(cell.red), amber: n(cell.amber), green: n(cell.green) });
      rowCells[d] = {
        rag: worst,
        value: text,
        title: t('dashboard.heat.cellTitle', {
          red: n(cell.red),
          amber: n(cell.amber),
          green: n(cell.green),
          grey: n(cell.grey),
        }),
      };
    }
    return { key: key || 'none', label: name, text: name, cells: rowCells };
  });
  return (
    <div className="flex flex-col gap-2">
      <RagGrid
        label={t('dashboard.heat.title')}
        rowHeader={t('dashboard.heat.unit')}
        columns={dimensions.map((d) => ({ key: d, label: t(`dimensions.${d}`) }))}
        rows={rows}
      />
      <p className="text-xs text-muted-foreground">{t('dashboard.heat.legend')}</p>
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
