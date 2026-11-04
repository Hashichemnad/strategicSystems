import { useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { pickText } from '@/lib/format';
import { Gantt, Skeleton, StatusBanner } from '@/ui';
import type { ProjectFull } from '../projectApi';
import { GanttExportButtons } from './GanttExportButtons';
import { useProjectGantt } from './viewsApi';

/** Project page → Gantt tab (docs/design/portfolio.md §36): phases, milestones (and releases / sprints), baseline, export. */
export function ProjectGanttTab({
  project,
}: {
  project: Pick<ProjectFull, 'id' | 'number' | 'nameAr' | 'nameEn'>;
}) {
  const { t, i18n } = useTranslation('portfolioViews');
  const { t: tc } = useTranslation();
  const gantt = useProjectGantt(project.id);
  const svgRef = useRef<SVGSVGElement>(null);
  const title = `${project.number} ${pickText(i18n.language, project.nameAr, project.nameEn)}`.trim();
  return (
    <section className="flex flex-col gap-4" data-testid="project-gantt">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h2 className="text-lg font-semibold">{t('gantt.title')}</h2>
          {gantt.data?.baselineShown && (
            <p className="text-sm text-muted-foreground">{t('gantt.baselineShown')}</p>
          )}
        </div>
        <GanttExportButtons
          svgRef={svgRef}
          fileName={`${project.number}-gantt`}
          disabled={!gantt.data || gantt.data.rows.length === 0}
        />
      </div>
      {gantt.isPending ? (
        <Skeleton className="h-80 rounded-lg" />
      ) : gantt.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, gantt.error)} />
      ) : (
        <Gantt
          rows={gantt.data.rows}
          dependencies={gantt.data.dependencies}
          start={gantt.data.start}
          end={gantt.data.end}
          today={gantt.data.today}
          title={title}
          testId="project-gantt-chart"
          svgRef={svgRef}
        />
      )}
    </section>
  );
}
