import { Link, Outlet } from '@tanstack/react-router';
import { useTranslation } from 'react-i18next';
import { canIn, useAccess } from '@/features/access/session';
import { EmptyState, PageHeader, Skeleton } from '@/ui';
import { settingsGroups } from './settingsNav';

function useVisibleGroups() {
  const access = useAccess();
  return {
    loading: !access,
    groups: settingsGroups
      .map((group) => ({ ...group, pages: group.pages.filter((page) => canIn(access, ...page.permissions)) }))
      .filter((group) => group.pages.length > 0),
  };
}

/** Settings centre frame: a secondary navigation on the start side and the selected page. */
export function SettingsLayout() {
  const { t } = useTranslation('settings');
  const { groups } = useVisibleGroups();
  return (
    <div className="flex flex-col gap-6 lg:flex-row">
      <nav aria-label={t('navigation')} className="flex shrink-0 flex-col gap-4 lg:w-56">
        <Link
          to="/settings"
          className="text-sm font-semibold hover:underline"
          activeOptions={{ exact: true }}
        >
          {t('title')}
        </Link>
        {groups.map((group) => (
          <div key={group.key} className="flex flex-col gap-0.5">
            <p className="px-2 pb-1 text-xs font-semibold tracking-wide text-muted-foreground uppercase">
              {t(`groups.${group.key}`)}
            </p>
            {group.pages.map((page) => (
              <Link
                key={page.key}
                to={page.to}
                className="flex items-center gap-2 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:bg-muted hover:text-foreground"
                activeProps={{ className: 'bg-accent text-accent-foreground', 'aria-current': 'page' }}
              >
                <page.icon className="size-4" aria-hidden />
                {t(`pages.${page.key}.title`)}
              </Link>
            ))}
          </div>
        ))}
      </nav>
      <div className="min-w-0 flex-1">
        <Outlet />
      </div>
    </div>
  );
}

/** The Settings centre home: one card per page the user may open. */
export function SettingsHubPage() {
  const { t } = useTranslation('settings');
  const { loading, groups } = useVisibleGroups();
  if (loading) return <Skeleton className="h-96" />;
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t('title')} description={t('description')} />
      {groups.length === 0 && <EmptyState title={t('nothing')} />}
      {groups.map((group) => (
        <section key={group.key} className="flex flex-col gap-3" aria-labelledby={`settings-${group.key}`}>
          <h2 id={`settings-${group.key}`} className="text-sm font-semibold text-muted-foreground">
            {t(`groups.${group.key}`)}
          </h2>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {group.pages.map((page) => (
              <Link
                key={page.key}
                to={page.to}
                className="flex gap-3 rounded-lg border bg-card p-4 transition-colors hover:border-primary"
              >
                <page.icon className="mt-0.5 size-5 shrink-0 text-primary" aria-hidden />
                <span className="flex flex-col gap-1">
                  <span className="font-medium">{t(`pages.${page.key}.title`)}</span>
                  <span className="text-sm text-muted-foreground">{t(`pages.${page.key}.description`)}</span>
                </span>
              </Link>
            ))}
          </div>
        </section>
      ))}
    </div>
  );
}
