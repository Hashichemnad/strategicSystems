import { useMemo, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { EmptyState, Gantt, PageHeader, Skeleton, StatusBanner } from '@/ui';
import { GanttExportButtons } from './GanttExportButtons';
import { PortfolioFilters } from './PortfolioFilters';
import { usePortfolioGantt, type PortfolioFilter } from './viewsApi';

/** Portfolio → Gantt (docs/design/portfolio.md §36): one row per project with its phases / top-level milestones. */
export function PortfolioGanttPage() {
  const { t } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const [filter, setFilter] = useState<PortfolioFilter>({});
  const gantt = usePortfolioGantt(filter);
  const svgRef = useRef<SVGSVGElement>(null);
  const projectIds = useMemo(
    () => (gantt.data?.rows ?? []).filter((r) => r.kind === 'project').map((r) => r.id),
    [gantt.data],
  );
  // Many projects start collapsed so the portfolio fits on a screen; a few show their phases.
  const collapsed = projectIds.length > 8 ? projectIds : [];
  const key = `${JSON.stringify(filter)}-${String(projectIds.length)}`;

  return (
    <div className="flex flex-col gap-6" data-testid="portfolio-gantt">
      <PageHeader
        title={t('portfolioGantt.title')}
        description={t('portfolioGantt.description')}
        actions={
          <GanttExportButtons
            svgRef={svgRef}
            fileName="portfolio-gantt"
            disabled={!gantt.data || gantt.data.rows.length === 0}
          />
        }
      />
      <PortfolioFilters value={filter} onChange={setFilter} />
      {gantt.isPending ? (
        <Skeleton className="h-96 rounded-lg" />
      ) : gantt.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, gantt.error)} />
      ) : gantt.data.rows.length === 0 ? (
        <EmptyState title={t('portfolioGantt.empty')} />
      ) : (
        <Gantt
          key={key}
          rows={gantt.data.rows}
          dependencies={gantt.data.dependencies}
          start={gantt.data.start}
          end={gantt.data.end}
          today={gantt.data.today}
          title={t('portfolioGantt.title')}
          testId="portfolio-gantt-chart"
          defaultCollapsed={collapsed}
          svgRef={svgRef}
        />
      )}
    </div>
  );
}
