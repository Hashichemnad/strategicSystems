import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowLeft, FolderKanban, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { CurrentStatusBanner } from '@/features/workflow/ApprovalPanel';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import {
  EmptyState,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  StageStepper,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
} from '@/ui';
import { projectResource, usePortfolioOptions, useProject, useProjects } from './api';

const listRoute = getRouteApi('/app/portfolio/projects');
const projectRoute = getRouteApi('/app/portfolio/projects/$projectId');
const PAGE_SIZE = 25;

/** Projects and initiatives created from approved requests (Phase 8: the record; the lifecycle comes with Phase 9). */
export function ProjectsPage() {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const search = listRoute.useSearch();
  const navigate = listRoute.useNavigate();
  const options = usePortfolioOptions();
  const [text, setText] = useState(search.search ?? '');
  useEffect(() => {
    if (text === (search.search ?? '')) return;
    const timer = window.setTimeout(
      () => void navigate({ search: (p) => ({ ...p, search: text || undefined, page: 1 }) }),
      300,
    );
    return () => window.clearTimeout(timer);
  }, [text, search.search, navigate]);
  const projects = useProjects({
    page: search.page,
    pageSize: PAGE_SIZE,
    ...(search.classification ? { classification: search.classification } : {}),
    ...(search.search ? { search: search.search } : {}),
  });
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('projectsTitle')} description={t('projectsDescription')} />
      <div className="grid gap-3 md:grid-cols-3">
        <div className="relative md:col-span-2">
          <Search
            className="pointer-events-none absolute start-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
            aria-hidden
          />
          <Input
            className="ps-9"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder={t('filters.search')}
            aria-label={t('filters.search')}
          />
        </div>
        <Select
          aria-label={t('filters.classification')}
          value={search.classification ?? '*'}
          onValueChange={(value) =>
            void navigate({
              search: (p) => ({ ...p, classification: value === '*' ? undefined : value, page: 1 }),
            })
          }
          options={[
            { value: '*', label: t('filters.any') },
            { value: 'project', label: t('classification.project') },
            { value: 'initiative', label: t('classification.initiative') },
          ]}
        />
      </div>
      {projects.isPending ? (
        <Skeleton className="h-64 rounded-lg" />
      ) : projects.isError ? (
        <StatusBanner tone="danger" title={errorMessage(tc, projects.error)} />
      ) : projects.data.items.length === 0 ? (
        <EmptyState icon={FolderKanban} title={t('projects.empty')} />
      ) : (
        <>
          <div className="overflow-x-auto rounded-lg border bg-card">
            <Table data-testid="projects-table">
              <TableHead>
                <TableRow>
                  <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.classification')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.stage')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.budget')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
                  <TableHeaderCell>{t('fields.plannedStart')}</TableHeaderCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {projects.data.items.map((p) => (
                  <TableRow key={p.id}>
                    <TableCell>
                      <Link
                        to="/portfolio/projects/$projectId"
                        params={{ projectId: p.id }}
                        className="font-medium text-primary hover:underline"
                      >
                        <bdi>{p.number}</bdi>
                      </Link>
                    </TableCell>
                    <TableCell dir="auto">{pickText(i18n.language, p.nameAr, p.nameEn)}</TableCell>
                    <TableCell>{t(`classification.${p.classification as 'project'}`)}</TableCell>
                    <TableCell>
                      <StatusChip tone="info">{t(`projectStages.${p.stage as 'planning'}`)}</StatusChip>
                    </TableCell>
                    <TableCell className="tabular-nums">
                      {p.budget !== null
                        ? `${formatNumber(p.budget, i18n.language)} ${options.data?.currencyCode ?? ''}`
                        : '—'}
                    </TableCell>
                    <TableCell>
                      {p.unit ? pickText(i18n.language, p.unit.nameAr, p.unit.nameEn) : '—'}
                    </TableCell>
                    <TableCell>{p.plannedStart ? formatDate(p.plannedStart, i18n.language) : '—'}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
          <Pagination
            page={search.page}
            totalPages={Math.max(1, Math.ceil(projects.data.totalCount / PAGE_SIZE))}
            totalCount={projects.data.totalCount}
            onPageChange={(page) => void navigate({ search: (p) => ({ ...p, page }) })}
          />
        </>
      )}
    </div>
  );
}

/** A project / initiative created from a request: header, stage path, current status, the copied data and the request link. */
export function ProjectPage() {
  const { t, i18n } = useTranslation('portfolio');
  const { t: tc } = useTranslation();
  const { projectId } = projectRoute.useParams();
  const project = useProject(projectId);
  const options = usePortfolioOptions();
  if (project.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (project.isError) return <StatusBanner tone="danger" title={errorMessage(tc, project.error)} />;
  const p = project.data;
  const lang = i18n.language;
  const money = (value: number | null) =>
    value === null ? '—' : `${formatNumber(value, lang)} ${options.data?.currencyCode ?? ''}`;
  const objective = options.data?.objectives.find((o) => o.code === p.strategicObjectiveCode);
  const rows: [string, string][] = [
    [t('fields.classification'), t(`classification.${p.classification as 'project'}`)],
    [t('fields.unit'), p.unit ? pickText(lang, p.unit.nameAr, p.unit.nameEn) : '—'],
    [t('fields.owner'), p.owner ? pickText(lang, p.owner.nameAr, p.owner.nameEn) : '—'],
    [t('fields.requester'), p.requester ? pickText(lang, p.requester.nameAr, p.requester.nameEn) : '—'],
    [
      t('fields.strategicObjectiveCode'),
      objective ? pickText(lang, objective.labelAr, objective.labelEn) : (p.strategicObjectiveCode ?? '—'),
    ],
    [t('fields.budget'), money(p.budget)],
    [t('fields.plannedStart'), p.plannedStart ? formatDate(p.plannedStart, lang) : '—'],
    [t('fields.plannedEnd'), p.plannedEnd ? formatDate(p.plannedEnd, lang) : '—'],
  ];
  return (
    <div className="flex flex-col gap-6" data-testid="project-page">
      <Link
        to="/portfolio/projects"
        search={{ page: 1 }}
        className="flex items-center gap-1 text-sm text-primary hover:underline"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden />
        {t('projectsTitle')}
      </Link>
      <PageHeader
        title={<span dir="auto">{pickText(lang, p.nameAr, p.nameEn)}</span>}
        description={<bdi>{p.number}</bdi>}
        meta={
          <>
            <StatusChip tone="success">{t(`projectStatus.${p.status as 'active'}`)}</StatusChip>
            <StatusChip tone="info">{t(`classification.${p.classification as 'project'}`)}</StatusChip>
          </>
        }
      />
      <StageStepper
        stages={p.stagePath.map((key) => ({ key, label: t(`projectStages.${key as 'planning'}`) }))}
        current={p.stage}
      />
      <CurrentStatusBanner resourceType={projectResource} resourceId={p.id} onAction={() => undefined} />
      <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
        <section className="flex flex-col gap-4 rounded-lg border bg-card p-6">
          <dl className="grid gap-4 md:grid-cols-2">
            {rows.map(([label, value]) => (
              <div key={label}>
                <dt className="text-sm text-muted-foreground">{label}</dt>
                <dd className="mt-1" dir="auto">
                  {value}
                </dd>
              </div>
            ))}
          </dl>
          {[
            [t('fields.description'), p.description],
            [t('fields.scopeOfWork'), p.scopeOfWork],
          ].map(([label, value]) => (
            <div key={label}>
              <h3 className="text-sm text-muted-foreground">{label}</h3>
              <p className="mt-1 whitespace-pre-line" dir="auto">
                {value ?? '—'}
              </p>
            </div>
          ))}
          <p className="text-sm">
            <Link
              to="/portfolio/requests/$requestId"
              params={{ requestId: p.requestId }}
              search={{ tab: 'overview' }}
              className="text-primary hover:underline"
            >
              {t('projects.fromRequest', { number: p.requestNumber ?? '' })}
            </Link>
          </p>
        </section>
        <section className="flex flex-col gap-2 rounded-lg border bg-card p-4">
          <h3 className="font-medium">{t('projects.plannedCosts')}</h3>
          {(
            [
              ['year1Cost', p.year1PlannedCost],
              ['year2Cost', p.year2PlannedCost],
              ['year3Cost', p.year3PlannedCost],
              ['year4Cost', p.year4PlannedCost],
            ] as const
          ).map(([key, value]) => (
            <div key={key} className="flex justify-between text-sm">
              <span className="text-muted-foreground">{t(`fields.${key}`)}</span>
              <span className="tabular-nums">{money(value)}</span>
            </div>
          ))}
          <p className="mt-2 text-xs text-muted-foreground">{t('projects.nextPhase')}</p>
        </section>
      </div>
    </div>
  );
}
