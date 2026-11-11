import { getRouteApi, Link } from '@tanstack/react-router';
import { ArrowUpRight, ClipboardCheck, Inbox, Send, Star, Zap } from 'lucide-react';
import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { translateMessageKey } from '@/api/problem';
import { canIn, useAccess, useCan } from '@/features/access/session';
import { DashboardPage } from '@/features/reporting/DashboardPage';
import { useMyWork } from '@/features/workflow/api';
import { AppIcon } from '@/layout/WorkspaceShell';
import { cn } from '@/lib/cn';
import { formatDate, formatNumber, pickText } from '@/lib/format';
import { Button, EmbeddedPage, EmptyState, Skeleton, StatusBanner, StatusChip } from '@/ui';
import { useAppName, useFavouriteApps, useVisibleApps } from './api';

const route = getRouteApi('/app/apps/$app');

/**
 * Home page of an application workspace (SPEC-ADDENDUM §C2, docs/design/themes.md §5.2): the app's header, quick actions, the user's
 * items in this system (from My Work) and the system dashboard (Phase 17). Every part is filtered by the user's access.
 */
export function AppHomePage() {
  const { t } = useTranslation('appearance');
  const { app: code } = route.useParams();
  const search = route.useSearch();
  const apps = useVisibleApps();
  const access = useAccess();
  const appName = useAppName();
  const { favourites, toggle } = useFavouriteApps();
  const canDashboards = useCan('reporting.dashboard.read');
  const translate = t as unknown as (key: string) => string;
  if (!access) return <Skeleton className="h-96 rounded-lg" />;
  const app = apps.find((a) => a.code === code && a.hasHome);
  if (!app) return <StatusBanner tone="warning" title={t('home.notFound')} />;
  const name = appName(app);
  const favourite = favourites.includes(app.code);
  const actions = (app.quickActions ?? []).filter((action) => canIn(access, ...action.permissions));

  return (
    <div className="flex flex-col gap-6" data-testid={`app-home-${app.code}`}>
      <header className="flex flex-col gap-4 rounded-lg border bg-card p-5 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 items-center gap-4">
          <AppIcon app={app} size="lg" />
          <div className="min-w-0">
            <h1 className="text-2xl font-semibold tracking-tight">{name}</h1>
            <p className="text-sm text-muted-foreground">{translate(`apps.${app.code}.description`)}</p>
          </div>
        </div>
        <Button
          variant="outline"
          onClick={() => toggle(app.code)}
          aria-pressed={favourite}
          data-testid="app-home-favourite"
        >
          <Star className={cn(favourite && 'fill-current text-warning')} aria-hidden />
          {t(favourite ? 'launcher.removeFavourite' : 'launcher.addFavourite', { name })}
        </Button>
      </header>

      {actions.length > 0 && (
        <section aria-labelledby="app-quick-actions" className="flex flex-col gap-3">
          <h2 id="app-quick-actions" className="flex items-center gap-2 font-semibold">
            <Zap className="size-4 text-muted-foreground" aria-hidden />
            {t('quickActions.label')}
          </h2>
          <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            {actions.map((action) => (
              <li key={action.key}>
                <Link
                  to={action.to as '/'}
                  className="group flex items-center justify-between gap-2 rounded-lg border bg-card p-4 text-sm font-medium hover:border-app-accent"
                  data-testid={`quick-action-${action.key}`}
                >
                  {translate(`quickActions.${action.key}`)}
                  <ArrowUpRight className="size-4 text-muted-foreground rtl:-scale-x-100" aria-hidden />
                </Link>
              </li>
            ))}
          </ul>
        </section>
      )}

      <MyItems system={app.system ?? app.code} name={name} />

      {app.dashboard && canDashboards && (
        <section aria-labelledby="app-dashboard" className="flex flex-col gap-3">
          <h2 id="app-dashboard" className="font-semibold">
            {t('home.dashboard')}
          </h2>
          <EmbeddedPage>
            <DashboardPage dashboardKey={app.dashboard} search={search} />
          </EmbeddedPage>
        </section>
      )}
    </div>
  );
}

/** Approvals, action items and submissions of My Work that belong to this system. */
function MyItems({ system, name }: { system: string; name: string }) {
  const { t, i18n } = useTranslation('appearance');
  const { t: tc } = useTranslation();
  const work = useMyWork();
  const language = i18n.language;
  const approvals = (work.data?.approvals ?? []).filter((item) => item.systemCode === system);
  const actionItems = (work.data?.actionItems ?? []).filter((item) => item.systemCode === system);
  const submissions = (work.data?.submissions ?? []).filter(
    (item) => item.systemCode === system && !item.completedAt,
  );
  const empty = approvals.length + actionItems.length + submissions.length === 0;

  return (
    <section aria-labelledby="app-my-items" className="flex flex-col gap-3" data-testid="app-my-items">
      <h2 id="app-my-items" className="font-semibold">
        {t('home.myItems', { name })}
      </h2>
      {work.isPending ? (
        <Skeleton className="h-32 rounded-lg" />
      ) : empty ? (
        <EmptyState title={t('home.noItems')} />
      ) : (
        <div className="grid gap-4 lg:grid-cols-3">
          <ItemList
            icon={<ClipboardCheck aria-hidden />}
            title={t('home.approvals')}
            count={approvals.length}
          >
            {approvals.map((item) => (
              <Item
                key={item.taskId}
                to={item.link}
                number={item.number}
                title={pickText(language, item.titleAr, item.titleEn)}
                meta={pickText(language, item.stepNameAr, item.stepNameEn)}
                tone={item.overdue ? 'danger' : undefined}
                due={item.dueAt ? formatDate(item.dueAt, language) : undefined}
              />
            ))}
          </ItemList>
          <ItemList icon={<Inbox aria-hidden />} title={t('home.actionItems')} count={actionItems.length}>
            {actionItems.map((item) => (
              <Item
                key={`${item.resourceType}:${item.resourceId}:${item.messageKey}`}
                to={item.link}
                number={item.number}
                title={pickText(language, item.titleAr, item.titleEn)}
                meta={translateMessageKey(tc, item.messageKey)}
                due={item.dueDate ? formatDate(item.dueDate, language) : undefined}
              />
            ))}
          </ItemList>
          <ItemList icon={<Send aria-hidden />} title={t('home.submissions')} count={submissions.length}>
            {submissions.map((item) => (
              <Item
                key={item.instanceId}
                to={item.link}
                number={item.number}
                title={pickText(language, item.titleAr, item.titleEn)}
                meta={pickText(language, item.stepNameAr, item.stepNameEn)}
              />
            ))}
          </ItemList>
        </div>
      )}
    </section>
  );
}

function ItemList({
  icon,
  title,
  count,
  children,
}: {
  icon: ReactNode;
  title: string;
  count: number;
  children: ReactNode;
}) {
  const { i18n } = useTranslation();
  return (
    <div className="flex flex-col gap-2 rounded-lg border bg-card p-4">
      <h3 className="flex items-center gap-2 text-sm font-semibold [&_svg]:size-4 [&_svg]:text-muted-foreground">
        {icon}
        {title}
        <StatusChip tone={count > 0 ? 'info' : 'neutral'} className="ms-auto">
          {formatNumber(count, i18n.language)}
        </StatusChip>
      </h3>
      {count > 0 && <ul className="flex flex-col divide-y">{children}</ul>}
    </div>
  );
}

function Item({
  to,
  number,
  title,
  meta,
  due,
  tone,
}: {
  to: string;
  number: string | null;
  title: string;
  meta?: string;
  due?: string;
  tone?: 'danger';
}) {
  return (
    <li className="py-2">
      <Link to={to as '/'} className="flex flex-col gap-0.5 text-sm hover:underline">
        <span className="font-medium">
          {number && <bdi className="text-muted-foreground">{number} · </bdi>}
          <span dir="auto">{title}</span>
        </span>
        {(meta ?? due) && (
          <span className={cn('text-xs text-muted-foreground', tone === 'danger' && 'text-danger')}>
            {[meta, due].filter(Boolean).join(' · ')}
          </span>
        )}
      </Link>
    </li>
  );
}
