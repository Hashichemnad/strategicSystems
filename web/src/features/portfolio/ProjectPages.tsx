import { getRouteApi, Link } from '@tanstack/react-router';
import { FolderKanban, Search } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { cn } from '@/lib/cn';
import { formatNumber, pickText } from '@/lib/format';
import {
  Button,
  EmptyState,
  ListViewToggle,
  Input,
  PageHeader,
  Pagination,
  Select,
  Skeleton,
  SplitView,
  StatusBanner,
  StatusChip,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  useListView,
} from '@/ui';
import { usePortfolioOptions, useProjects } from './api';
import { toPercent } from './projectApi';
import { projectStatusTone } from './project/ProjectPage';

const listRoute = getRouteApi('/app/portfolio/projects');
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
    ...(search.view && search.view !== 'all' ? { view: search.view } : {}),
    ...(search.status ? { status: search.status } : {}),
  });
  // Table or list + preview (Enterprise toggle, Modern default; Classic: table only — ADR 0174).
  const listView = useListView('portfolio.projects');
  const [selectedId, setSelectedId] = useState<string>();
  // The first row is previewed until the user picks another.
  const selected = projects.data?.items.find((p) => p.id === selectedId) ?? projects.data?.items[0];
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('projectsTitle')}
        description={t('projectsDescription')}
        actions={listView.available && <ListViewToggle value={listView.view} onChange={listView.setView} />}
      />
      <div className="grid gap-3 md:grid-cols-5">
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
        <Select
          aria-label={t('project.list.view')}
          value={search.view ?? 'all'}
          onValueChange={(value) =>
            void navigate({ search: (p) => ({ ...p, view: value as 'all', page: 1 }) })
          }
          options={(['all', 'mine', 'unclaimed'] as const).map((v) => ({
            value: v,
            label: t(`project.list.views.${v}`),
          }))}
        />
        <Select
          aria-label={t('filters.status')}
          value={search.status ?? '*'}
          onValueChange={(value) =>
            void navigate({ search: (p) => ({ ...p, status: value === '*' ? undefined : value, page: 1 }) })
          }
          options={[
            { value: '*', label: t('filters.any') },
            ...(['active', 'on-hold', 'cancelled', 'completed'] as const).map((s) => ({
              value: s,
              label: t(`projectStatus.${s}`),
            })),
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
          {listView.view === 'split' ? (
            <SplitView
              hasSelection={!!selected}
              list={
                <ul className="flex flex-col gap-2" data-testid="projects-cards">
                  {projects.data.items.map((p) => (
                    <ProjectCard
                      key={p.id}
                      project={p}
                      selected={p.id === selected?.id}
                      onPreview={() => setSelectedId(p.id)}
                    />
                  ))}
                </ul>
              }
              preview={
                selected && <ProjectPreview project={selected} currency={options.data?.currencyCode} />
              }
            />
          ) : (
            <div className="overflow-x-auto rounded-lg border bg-card">
              <Table data-testid="projects-table">
                <TableHead>
                  <TableRow>
                    <TableHeaderCell>{t('fields.number')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.name')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.classification')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.stage')}</TableHeaderCell>
                    <TableHeaderCell>{t('project.pm')}</TableHeaderCell>
                    <TableHeaderCell>{t('project.progress.performance')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.budget')}</TableHeaderCell>
                    <TableHeaderCell>{t('fields.unit')}</TableHeaderCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {projects.data.items.map((p) => (
                    <TableRow key={p.id}>
                      <TableCell>
                        <Link
                          to="/portfolio/projects/$projectId"
                          params={{ projectId: p.id }}
                          search={{}}
                          className="font-medium text-primary hover:underline"
                        >
                          <bdi>{p.number}</bdi>
                        </Link>
                      </TableCell>
                      <TableCell dir="auto">{pickText(i18n.language, p.nameAr, p.nameEn)}</TableCell>
                      <TableCell>{t(`classification.${p.classification as 'project'}`)}</TableCell>
                      <TableCell>
                        <div className="flex flex-wrap gap-1">
                          <StatusChip tone="info">{t(`projectStages.${p.stage as 'planning'}`)}</StatusChip>
                          {p.status !== 'active' && (
                            <StatusChip tone={projectStatusTone[p.status] ?? 'neutral'}>
                              {t(`projectStatus.${p.status as 'active'}`)}
                            </StatusChip>
                          )}
                          <span className="text-xs text-muted-foreground">
                            {t(`project.subStates.${p.subState as 'execution'}`)}
                          </span>
                        </div>
                      </TableCell>
                      <TableCell>{p.pm ? pickText(i18n.language, p.pm.nameAr, p.pm.nameEn) : '—'}</TableCell>
                      <TableCell className="tabular-nums">
                        <StatusChip tone={p.progressStatus === 'delayed' ? 'danger' : 'success'}>
                          {formatNumber(toPercent(p.performance) ?? 0, i18n.language)} %
                        </StatusChip>
                      </TableCell>
                      <TableCell className="tabular-nums">
                        {p.budget !== null
                          ? `${formatNumber(p.budget, i18n.language)} ${options.data?.currencyCode ?? ''}`
                          : '—'}
                      </TableCell>
                      <TableCell>
                        {p.unit ? pickText(i18n.language, p.unit.nameAr, p.unit.nameEn) : '—'}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          )}
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

type ProjectListItem = NonNullable<ReturnType<typeof useProjects>['data']>['items'][number];

/** A project in the split view's list: number and name open the record; *Preview* shows it beside the list on wide screens. */
function ProjectCard({
  project: p,
  selected,
  onPreview,
}: {
  project: ProjectListItem;
  selected: boolean;
  onPreview: () => void;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: ta } = useTranslation('appearance');
  const performance = toPercent(p.performance) ?? 0;
  return (
    <li
      className={cn(
        'flex flex-col gap-2 rounded-lg border bg-card p-4',
        selected && 'border-app-accent ring-1 ring-app-accent',
      )}
      data-testid="project-card"
    >
      <div className="flex items-start justify-between gap-3">
        <Link
          to="/portfolio/projects/$projectId"
          params={{ projectId: p.id }}
          search={{}}
          className="min-w-0 font-medium hover:underline"
        >
          <bdi className="block text-xs text-muted-foreground">{p.number}</bdi>
          <span dir="auto" className="block">
            {pickText(i18n.language, p.nameAr, p.nameEn)}
          </span>
        </Link>
        <Button
          variant="ghost"
          size="sm"
          className="hidden shrink-0 lg:inline-flex"
          aria-pressed={selected}
          onClick={onPreview}
          data-testid="project-preview"
        >
          {ta('split.preview')}
        </Button>
      </div>
      <div className="flex flex-wrap items-center gap-1.5">
        <StatusChip tone="info">{t(`projectStages.${p.stage as 'planning'}`)}</StatusChip>
        {p.status !== 'active' && (
          <StatusChip tone={projectStatusTone[p.status] ?? 'neutral'}>
            {t(`projectStatus.${p.status as 'active'}`)}
          </StatusChip>
        )}
        <span className="text-xs text-muted-foreground">
          {p.pm ? pickText(i18n.language, p.pm.nameAr, p.pm.nameEn) : '—'}
        </span>
      </div>
      <div className="flex items-center gap-2">
        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-muted" aria-hidden>
          <div
            className={cn('h-full rounded-full', p.progressStatus === 'delayed' ? 'bg-danger' : 'bg-success')}
            style={{ inlineSize: `${Math.min(100, Math.max(0, performance))}%` }}
          />
        </div>
        <span className="text-xs tabular-nums">
          {formatNumber(performance, i18n.language)} % ·{' '}
          {t(`project.progress.${p.progressStatus === 'delayed' ? 'delayed' : 'onTrack'}`)}
        </span>
      </div>
    </li>
  );
}

/** The split view's preview of the selected project (list fields only; the record page has everything). */
function ProjectPreview({
  project: p,
  currency,
}: {
  project: ProjectListItem;
  currency: string | undefined;
}) {
  const { t, i18n } = useTranslation('portfolio');
  const { t: ta } = useTranslation('appearance');
  const lang = i18n.language;
  const rows: [string, React.ReactNode][] = [
    [t('fields.number'), <bdi key="n">{p.number}</bdi>],
    [t('fields.classification'), t(`classification.${p.classification as 'project'}`)],
    [t('fields.stage'), t(`projectStages.${p.stage as 'planning'}`)],
    [t('project.pm'), p.pm ? pickText(lang, p.pm.nameAr, p.pm.nameEn) : '—'],
    [t('fields.unit'), p.unit ? pickText(lang, p.unit.nameAr, p.unit.nameEn) : '—'],
    [t('fields.budget'), p.budget !== null ? `${formatNumber(p.budget, lang)} ${currency ?? ''}` : '—'],
    [t('project.progress.performance'), `${formatNumber(toPercent(p.performance) ?? 0, lang)} %`],
  ];
  return (
    <div className="flex flex-col gap-4" data-testid="project-preview-panel">
      <h2 className="text-lg font-semibold" dir="auto">
        {pickText(lang, p.nameAr, p.nameEn)}
      </h2>
      <dl className="grid grid-cols-1 gap-3">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="text-sm font-medium">{value}</dd>
          </div>
        ))}
      </dl>
      <Button asChild>
        <Link to="/portfolio/projects/$projectId" params={{ projectId: p.id }} search={{}}>
          {ta('split.open')}
        </Link>
      </Button>
    </div>
  );
}
