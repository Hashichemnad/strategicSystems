import { Link, useNavigate } from '@tanstack/react-router';
import { LayoutDashboard, Pencil, Pin, PinOff } from 'lucide-react';
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { api } from '@/api/client';
import { errorMessage } from '@/api/problem';
import { UnitSelect } from '@/features/access/pickers';
import { useCan } from '@/features/access/session';
import { pickText } from '@/lib/format';
import { Button, EmptyState, PageHeader, Select, Skeleton, StatusBanner, StatusChip, useToast } from '@/ui';
import { DashboardFrame, currentPeriod, type PeriodValue } from '@/ui/dashboard';
import {
  useDashboardData,
  useDashboards,
  usePin,
  usePins,
  useSavedFilters,
  useSaveFilter,
  useUnpin,
  type DashboardSummary,
} from './api';
import { BriefPanel } from './BriefPanel';
import { ScheduleDialog } from './ScheduleDialog';
import { WidgetView } from './WidgetRenderer';

export interface DashboardSearch {
  kind?: 'month' | 'quarter' | 'year' | 'custom';
  from?: string;
  to?: string;
  compare?: boolean;
  unit?: string;
}

function periodFrom(search: DashboardSearch): PeriodValue {
  const fallback = currentPeriod(search.kind ?? 'quarter', new Date(), search.compare ?? true);
  return {
    kind: search.kind ?? fallback.kind,
    from: search.from ?? fallback.from,
    to: search.to ?? search.from ?? fallback.to,
    compare: search.compare ?? true,
  };
}

/**
 * A dashboard from the builder (system, executive, role or custom; docs/design/reporting.md §3–§5): one request for all widgets, period
 * selector with comparison, unit filter (unit and below), export (PDF / Excel), pin, saved filters, schedule, drill-down.
 */
export function DashboardPage({ dashboardKey, search }: { dashboardKey: string; search: DashboardSearch }) {
  const { t, i18n } = useTranslation('reporting');
  const { t: tc } = useTranslation();
  const navigate = useNavigate();
  const toast = useToast();
  const period = periodFrom(search);
  const data = useDashboardData(dashboardKey, { kind: period.kind, from: period.from, to: period.to, compare: period.compare }, search.unit);
  const pins = usePins();
  const pin = usePin();
  const unpin = useUnpin();
  const canExport = useCan('reporting.report.export');
  const canSchedule = useCan('reporting.schedule.manage');
  const filters = useSavedFilters(data.data?.id);
  const saveFilter = useSaveFilter(data.data?.id ?? '');
  const [filterName, setFilterName] = useState('');

  const setSearch = (next: Partial<DashboardSearch>) =>
    void navigate({ to: '/dashboards/$key', params: { key: dashboardKey }, search: { ...search, ...next } as never, replace: true });

  const pinned = pins.data?.find((p) => p.kind === 'dashboard' && p.refId === data.data?.id);
  const download = async (format: 'pdf' | 'xlsx') => {
    const { data: blob } = await api.GET('/api/v1/reporting/dashboards/{key}/export', {
      params: { path: { key: dashboardKey }, query: { format, kind: period.kind, from: period.from, to: period.to, unit: search.unit, lang: i18n.language } },
      parseAs: 'blob',
    });
    if (!blob) throw new Error('export failed');
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${dashboardKey}.${format}`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const ordered = useMemo(() => {
    if (!data.data) return [];
    return data.data.widgets
      .map((config, index) => ({ config, data: data.data!.data[index]! }))
      .sort((a, b) => a.config.y - b.config.y || a.config.x - b.config.x);
  }, [data.data]);

  if (data.isPending) return <Skeleton className="h-96 rounded-lg" />;
  if (data.isError || !data.data) return <StatusBanner tone="danger" title={errorMessage(tc, data.error)} />;
  const dashboard = data.data;
  const selectedUnit = dashboard.units.find((u) => u.id === search.unit);
  const asOf = dashboard.data.map((w) => w.asOf).filter(Boolean).sort().at(-1) ?? null;

  return (
    <DashboardFrame
      testId={`dashboard-${dashboard.code}`}
      title={pickText(i18n.language, dashboard.titleAr, dashboard.titleEn)}
      description={selectedUnit ? t('dashboards.filteredBy', { unit: pickText(i18n.language, selectedUnit.nameAr, selectedUnit.nameEn) }) : undefined}
      period={period}
      onPeriodChange={(p) => setSearch({ kind: p.kind, from: p.from, to: p.to, compare: p.compare })}
      asOf={asOf}
      exports={canExport ? { onPdf: () => download('pdf'), onExcel: () => download('xlsx'), onError: (e) => toast({ tone: 'error', title: errorMessage(tc, e) }) } : undefined}
      filters={
        <div className="flex flex-wrap items-end gap-2">
          <div className="w-64">
            <UnitSelect value={search.unit} allowEmpty onChange={(unit) => setSearch({ unit })} />
          </div>
          {filters.data && filters.data.length > 0 && (
            <div className="w-48">
              <Select
                aria-label={t('dashboards.savedFilters')}
                placeholder={t('dashboards.savedFilters')}
                value={undefined}
                onValueChange={(id) => {
                  const saved = filters.data?.find((f) => f.id === id);
                  if (saved) setSearch(saved.filters as DashboardSearch);
                }}
                options={filters.data.map((f) => ({ value: f.id, label: f.name }))}
              />
            </div>
          )}
          <input
            className="h-10 w-40 rounded-md border border-input bg-card px-2 text-sm"
            aria-label={t('dashboards.filterName')}
            placeholder={t('dashboards.filterName')}
            value={filterName}
            onChange={(e) => setFilterName(e.target.value)}
          />
          <Button
            size="sm"
            variant="outline"
            disabled={!filterName.trim()}
            onClick={() =>
              saveFilter.mutate(
                { name: filterName.trim(), filters: { ...search, kind: period.kind, from: period.from, to: period.to }, isDefault: false },
                { onSuccess: () => { setFilterName(''); toast({ title: t('dashboards.filterSaved') }); } },
              )
            }
          >
            {t('dashboards.saveFilter')}
          </Button>
        </div>
      }
      actions={
        <div className="flex flex-wrap gap-2">
          <Button
            size="sm"
            variant="outline"
            data-testid="dashboard-pin"
            aria-pressed={!!pinned}
            onClick={() => (pinned ? unpin.mutate(pinned.id) : pin.mutate({ kind: 'dashboard', refId: dashboard.id }))}
          >
            {pinned ? <PinOff className="size-4" aria-hidden /> : <Pin className="size-4" aria-hidden />}
            {pinned ? t('dashboards.unpin') : t('dashboards.pin')}
          </Button>
          {canSchedule && <ScheduleDialog targetKind="dashboard" targetId={dashboard.id} defaultName={pickText(i18n.language, dashboard.titleAr, dashboard.titleEn)} />}
        </div>
      }
    >
      {selectedUnit && (
        <div className="flex items-center gap-2">
          <StatusChip tone="info">{pickText(i18n.language, selectedUnit.nameAr, selectedUnit.nameEn)}</StatusChip>
          <Button size="sm" variant="ghost" onClick={() => setSearch({ unit: undefined })}>
            {t('dashboards.clearUnit')}
          </Button>
        </div>
      )}
      <div className="grid grid-cols-12 gap-4">
        {ordered.map(({ config, data: widget }) =>
          config.type === 'brief' ? (
            <div key={config.id} className="col-span-12">
              <BriefPanel period={period} unitId={search.unit} />
            </div>
          ) : (
            <div
              key={config.id}
              className="col-span-12 md:col-span-6 lg:[grid-column:var(--span)]"
              style={{ ['--span' as string]: `span ${config.w} / span ${config.w}` }}
            >
              <WidgetView config={config} data={widget} units={dashboard.units} unitId={search.unit} period={period} />
            </div>
          ),
        )}
      </div>
    </DashboardFrame>
  );
}

/** "Dashboards": role defaults, system dashboards, the executive dashboard, custom ones; pinned first. */
export function DashboardsPage() {
  const { t, i18n } = useTranslation('reporting');
  const dashboards = useDashboards();
  const canBuild = useCan('reporting.dashboard.build');
  const groups: { key: string; items: DashboardSummary[] }[] = useMemo(() => {
    const list = dashboards.data ?? [];
    return [
      { key: 'pinned', items: list.filter((d) => d.pinned) },
      { key: 'defaults', items: list.filter((d) => d.isRoleDefault && !d.pinned) },
      { key: 'executive', items: list.filter((d) => d.level === 'executive' && !d.pinned && !d.isRoleDefault) },
      { key: 'system', items: list.filter((d) => d.level === 'system' && !d.pinned && !d.isRoleDefault) },
      { key: 'other', items: list.filter((d) => (d.level === 'role' || d.level === 'custom') && !d.pinned && !d.isRoleDefault) },
    ].filter((g) => g.items.length > 0);
  }, [dashboards.data]);
  return (
    <div className="flex flex-col gap-6" data-testid="dashboards-page">
      <PageHeader
        title={t('dashboards.title')}
        description={t('dashboards.description')}
        actions={
          canBuild && (
            <Button asChild variant="outline">
              <Link to="/settings/dashboards">
                <Pencil className="size-4" aria-hidden />
                {t('dashboards.builder')}
              </Link>
            </Button>
          )
        }
      />
      {dashboards.isPending && <Skeleton className="h-40" />}
      {dashboards.data?.length === 0 && <EmptyState icon={LayoutDashboard} title={t('dashboards.empty')} />}
      {groups.map((group) => (
        <section key={group.key} className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold">{t(`dashboards.groups.${group.key}` as 'dashboards.groups.system')}</h2>
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {group.items.map((d) => (
              <Link
                key={d.id}
                to="/dashboards/$key"
                params={{ key: d.code }}
                className="flex flex-col gap-1 rounded-lg border bg-card p-4 hover:border-primary"
                data-testid={`dashboard-link-${d.code}`}
              >
                <span className="font-semibold">{pickText(i18n.language, d.titleAr, d.titleEn)}</span>
                <span className="text-sm text-muted-foreground">{pickText(i18n.language, d.descriptionAr, d.descriptionEn)}</span>
                {d.status !== 'published' && <StatusChip tone="warning">{t('dashboards.draft')}</StatusChip>}
              </Link>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}
