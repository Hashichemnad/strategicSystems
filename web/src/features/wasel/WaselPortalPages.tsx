import { Link, useNavigate } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { errorMessage } from '@/api/problem';
import { waselProjectRoute } from '@/app/router';
import { formatDate, pickText } from '@/lib/format';
import {
  EmptyState,
  PageHeader,
  Skeleton,
  StatusBanner,
  StatusChip,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '@/ui';
import { formatPercent } from './waselLogic';
import { useVendorProject, useWaselPortal, type VendorProject } from './waselApi';
import { WaselMonthlyTab } from './WaselMonthlyTab';
import { WaselCommentsTab, WaselDeliverablesTab, WaselItemsTab, WaselMeetingsTab } from './WaselOtherTabs';
import { WaselPlanTab } from './WaselPlanTab';

/** /wasel: the vendor's assigned projects (only those with an active assignment and a valid grant). */
export function WaselHomePage() {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const portal = useWaselPortal();
  if (portal.isPending) return <Skeleton className="h-40" />;
  if (portal.error) return <StatusBanner tone="danger" title={errorMessage(tc, portal.error)} />;
  const me = portal.data;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader
        title={t('home.title')}
        description={`${pickText(i18n.language, me.vendorNameAr, me.vendorNameEn)} · ${me.vendorNumber}`}
      />
      {me.projects.length === 0 ? (
        <EmptyState title={t('home.empty')} description={t('home.emptyHint')} />
      ) : (
        <ul className="grid gap-4 md:grid-cols-2" data-testid="wasel-projects">
          {me.projects.map((project) => (
            <li key={project.id}>
              <Link
                to="/wasel/projects/$projectId"
                params={{ projectId: project.id }}
                search={{ tab: undefined }}
                className="flex h-full flex-col gap-3 rounded-xl border bg-card p-5 shadow-sm hover:border-primary"
                data-testid="wasel-project-card"
              >
                <div className="flex items-start justify-between gap-2">
                  <div className="min-w-0">
                    <p className="text-xs text-muted-foreground" dir="ltr">
                      {project.number}
                    </p>
                    <p className="font-semibold">{pickText(i18n.language, project.nameAr, project.nameEn)}</p>
                  </div>
                  {project.openActions > 0 && (
                    <StatusChip tone="warning">
                      {t('home.openActions', { count: project.openActions })}
                    </StatusChip>
                  )}
                </div>
                <div className="flex flex-wrap gap-2">
                  <StatusChip tone="info">
                    {t(`stage.${project.stage}`, { defaultValue: project.stage })}
                  </StatusChip>
                  <StatusChip>{t(`role.${project.role}`, { defaultValue: project.role })}</StatusChip>
                  <StatusChip>
                    {t(`mode.${project.updateMode}`, { defaultValue: project.updateMode })}
                  </StatusChip>
                </div>
                {project.accessUntil && (
                  <p className="text-xs text-muted-foreground">
                    {t('home.accessUntil', { date: formatDate(project.accessUntil, i18n.language) })}
                  </p>
                )}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

const TABS = ['overview', 'plan', 'monthly', 'deliverables', 'items', 'meetings', 'comments'] as const;
type Tab = (typeof TABS)[number];

export function WaselProjectPage() {
  const { t, i18n } = useTranslation('wasel');
  const { t: tc } = useTranslation();
  const { projectId } = waselProjectRoute.useParams();
  const { tab } = waselProjectRoute.useSearch();
  const navigate = useNavigate();
  const project = useVendorProject(projectId);
  const active: Tab = TABS.includes(tab as Tab) ? (tab as Tab) : 'overview';

  if (project.isPending) return <Skeleton className="h-60" />;
  if (project.error) {
    return (
      <div className="flex flex-col gap-4">
        <StatusBanner tone="danger" title={errorMessage(tc, project.error)} />
        <Link to="/wasel" className="text-sm text-primary underline">
          {t('project.back')}
        </Link>
      </div>
    );
  }
  const p = project.data;
  return (
    <div className="flex flex-col gap-6" data-testid="wasel-project">
      <div className="flex flex-col gap-2">
        <Link to="/wasel" className="text-sm text-primary underline">
          {t('project.back')}
        </Link>
        <PageHeader title={pickText(i18n.language, p.nameAr, p.nameEn) || p.number} description={p.number} />
        <div className="flex flex-wrap gap-2">
          <StatusChip tone="info">{t(`stage.${p.stage}`, { defaultValue: p.stage })}</StatusChip>
          <StatusChip>{t(`role.${p.role}`, { defaultValue: p.role })}</StatusChip>
          <StatusChip>{t(`mode.${p.updateMode}`, { defaultValue: p.updateMode })}</StatusChip>
        </div>
      </div>
      <Tabs
        value={active}
        onValueChange={(value) =>
          void navigate({ to: '/wasel/projects/$projectId', params: { projectId }, search: { tab: value } })
        }
      >
        <TabsList className="flex-wrap">
          {TABS.map((key) => (
            <TabsTrigger key={key} value={key} data-testid={`wasel-tab-${key}`}>
              {t(`tabs.${key}`)}
            </TabsTrigger>
          ))}
        </TabsList>
        <TabsContent value="overview">
          <Overview project={p} />
        </TabsContent>
        <TabsContent value="plan">{active === 'plan' && <WaselPlanTab project={p} />}</TabsContent>
        <TabsContent value="monthly">{active === 'monthly' && <WaselMonthlyTab project={p} />}</TabsContent>
        <TabsContent value="deliverables">
          {active === 'deliverables' && <WaselDeliverablesTab project={p} />}
        </TabsContent>
        <TabsContent value="items">{active === 'items' && <WaselItemsTab project={p} />}</TabsContent>
        <TabsContent value="meetings">
          {active === 'meetings' && <WaselMeetingsTab project={p} />}
        </TabsContent>
        <TabsContent value="comments">
          {active === 'comments' && <WaselCommentsTab project={p} />}
        </TabsContent>
      </Tabs>
    </div>
  );
}

function Overview({ project: p }: { project: VendorProject }) {
  const { t, i18n } = useTranslation('wasel');
  const date = (value: string | null) => (value ? formatDate(value, i18n.language) : '—');
  const rows: [string, string | null][] = [
    [t('overview.number'), p.number],
    [t('overview.nameAr'), p.nameAr],
    [t('overview.nameEn'), p.nameEn],
    [t('overview.plannedStart'), date(p.plannedStart)],
    [t('overview.plannedEnd'), date(p.plannedEnd)],
    [t('overview.methodology'), t(`methodology.${p.methodology}`, { defaultValue: p.methodology })],
    [t('overview.status'), t(`status.${p.status}`, { defaultValue: p.status })],
    [t('overview.accessUntil'), date(p.accessUntil)],
  ];
  if (p.progress !== null) rows.push([t('overview.progress'), formatPercent(p.progress, i18n.language)]);
  if (p.budget !== null) {
    rows.push([
      t('overview.budget'),
      new Intl.NumberFormat(i18n.language === 'ar' ? 'ar-AE' : 'en-GB').format(p.budget),
    ]);
  }
  if (p.approvalStatus !== null) rows.push([t('overview.approvalStatus'), p.approvalStatus]);
  if (p.openWindowPeriod) rows.push([t('overview.window'), p.openWindowPeriod]);
  return (
    <div className="grid gap-4 pt-4 lg:grid-cols-3" data-testid="wasel-overview">
      <dl className="grid grid-cols-1 gap-x-6 gap-y-3 rounded-xl border bg-card p-5 text-sm sm:grid-cols-2 lg:col-span-2">
        {rows.map(([label, value]) => (
          <div key={label}>
            <dt className="text-xs text-muted-foreground">{label}</dt>
            <dd className="font-medium">{value ?? '—'}</dd>
          </div>
        ))}
        <div className="sm:col-span-2">
          <dt className="text-xs text-muted-foreground">{t('overview.scope')}</dt>
          <dd className="whitespace-pre-line">{p.scope ?? '—'}</dd>
        </div>
        {p.description && (
          <div className="sm:col-span-2">
            <dt className="text-xs text-muted-foreground">{t('overview.description')}</dt>
            <dd className="whitespace-pre-line">{p.description}</dd>
          </div>
        )}
      </dl>
      <div className="flex flex-col gap-3 rounded-xl border bg-card p-5 text-sm">
        <h2 className="font-semibold">{t('overview.contacts')}</h2>
        {p.projectManager && (
          <div data-testid="wasel-pm-contact">
            <p className="text-xs text-muted-foreground">{t('overview.projectManager')}</p>
            <p className="font-medium">
              {pickText(i18n.language, p.projectManager.nameAr, p.projectManager.nameEn)}
            </p>
            {p.projectManager.email && (
              <a className="text-primary underline" href={`mailto:${p.projectManager.email}`} dir="ltr">
                {p.projectManager.email}
              </a>
            )}
          </div>
        )}
        {p.owner && (
          <div>
            <p className="text-xs text-muted-foreground">{t('overview.owner')}</p>
            <p className="font-medium">{pickText(i18n.language, p.owner.nameAr, p.owner.nameEn)}</p>
          </div>
        )}
      </div>
    </div>
  );
}
