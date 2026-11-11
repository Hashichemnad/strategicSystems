import * as Popover from '@radix-ui/react-popover';
import { Link, Outlet, useRouterState } from '@tanstack/react-router';
import { Clock, Menu, NotebookPen, PanelLeftClose, PanelLeftOpen, Star, Target, X } from 'lucide-react';
import { useEffect, useState, type CSSProperties } from 'react';
import { useTranslation } from 'react-i18next';
import { useAppearance } from '@/app/theme';
import { AskButton } from '@/features/ai/AskDialog';
import { useAuditOptions } from '@/features/auditmgmt/api';
import { useKpiOptions } from '@/features/kpi/api';
import { useRiskOptions } from '@/features/risk/api';
import { useMe } from '@/features/auth/session';
import { useAppName, useVisibleApps } from '@/features/appearance/api';
import { NotificationBell } from '@/features/notifications/NotificationBell';
import { useRecentItems } from '@/features/search/api';
import { GlobalSearch } from '@/features/search/GlobalSearch';
import { useBranding } from '@/features/settings/publicSettings';
import { cn } from '@/lib/cn';
import { pickText } from '@/lib/format';
import { readPreference, writePreference } from '@/lib/preferences';
import { Button, ScrollRegion } from '@/ui';
import { AppLauncher } from './AppLauncher';
import { appForPath, gateTabs, isTabActive, type AppCode, type AppTab, type ResolvedApp } from './apps';
import { SkipLink } from './SkipLink';
import { CommandPalette } from './CommandPalette';
import { LanguageToggle, ProfileMenu, ThemeMenu } from './TopBar';

/** The label of an app tab (`appearance:tabs.<app>.<tab>`; "Home" for every app's home tab). */
export function useTabLabel(): (app: AppCode, tab: Pick<AppTab, 'key'>) => string {
  const { t } = useTranslation('appearance');
  const translate = t as unknown as (key: string) => string;
  return (app, tab) => (tab.key === 'home' ? t('tabs.home') : translate(`tabs.${app}.${tab.key}`));
}

/**
 * The current app's tabs, also filtered by the module's own navigation rules (Audit Office-only pages, lifecycle layer, dashboard and
 * export rights): only the current app's options are requested.
 */
export function useGatedTabs(app: ResolvedApp | undefined): AppTab[] {
  const audit = useAuditOptions(app?.code === 'auditmgmt').data;
  const risk = useRiskOptions(app?.code === 'risk').data;
  const kpi = useKpiOptions(app?.code === 'kpi').data;
  return app ? gateTabs(app.code, app.visibleTabs, { auditmgmt: audit, risk, kpi }) : [];
}

/** The app of the current page; pages outside every app keep the last app's frame (My Work at first). */
function useCurrentApp(): ResolvedApp | undefined {
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const visible = useVisibleApps();
  const code = appForPath(pathname);
  const [last, setLast] = useState<AppCode>('mywork');
  useEffect(() => {
    if (code) setLast(code);
  }, [code]);
  return visible.find((app) => app.code === (code ?? last)) ?? visible.find((app) => app.code === 'mywork');
}

/**
 * Application-workspace shell of the Enterprise and Modern themes (SPEC-ADDENDUM §C2–C4, docs/design/themes.md §4–5). Same routes and
 * pages as Classic; only the frame differs: app launcher, the current app's identity and tabs (Enterprise: tab bar; Modern: collapsible
 * sidebar), the shared top bar, and the Enterprise utility bar.
 */
export function WorkspaceShell() {
  const { uiTheme } = useAppearance();
  const app = useCurrentApp();
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const [menuOpen, setMenuOpen] = useState(false);
  useEffect(() => setMenuOpen(false), [pathname]);
  const enterprise = uiTheme === 'enterprise';
  const style = (app ? { '--app-accent': app.accent } : {}) as CSSProperties;

  return (
    <div
      className="flex min-h-dvh flex-col"
      style={style}
      data-layout="workspace"
      data-app={app?.code}
      data-testid="workspace-shell"
    >
      <SkipLink />
      <WorkspaceTopBar app={app} onOpenMenu={enterprise ? undefined : () => setMenuOpen(true)} />
      {enterprise ? (
        <>
          {app && <AppTabBar app={app} />}
          <main
            id="main"
            tabIndex={-1}
            className="mx-auto w-full max-w-[100rem] flex-1 px-3 py-4 outline-none md:px-6 md:pb-16"
          >
            <Outlet />
          </main>
          <UtilityBar />
        </>
      ) : (
        <div className="flex min-w-0 flex-1">
          <AppSidebar app={app} open={menuOpen} onClose={() => setMenuOpen(false)} />
          <main
            id="main"
            tabIndex={-1}
            className="mx-auto w-full max-w-7xl min-w-0 flex-1 px-4 py-6 outline-none md:px-10 md:py-10"
          >
            <div className="motion-panel" key={app?.code}>
              <Outlet />
            </div>
          </main>
        </div>
      )}
    </div>
  );
}

/** Icon tile in the app's accent colour (white icon, accents chosen ≥ 3 : 1 — ADR 0172). */
export function AppIcon({ app, size = 'md' }: { app: ResolvedApp; size?: 'sm' | 'md' | 'lg' }) {
  const Icon = app.iconComponent;
  return (
    <span
      className={cn(
        'flex shrink-0 items-center justify-center rounded-md text-white',
        size === 'sm' && 'size-7 [&_svg]:size-4',
        size === 'md' && 'size-8 [&_svg]:size-4',
        size === 'lg' && 'size-12 rounded-lg [&_svg]:size-6',
      )}
      style={{ backgroundColor: app.accent }}
      aria-hidden
    >
      <Icon />
    </span>
  );
}

function WorkspaceTopBar({ app, onOpenMenu }: { app: ResolvedApp | undefined; onOpenMenu?: () => void }) {
  const { t } = useTranslation('shell');
  const { t: tc } = useTranslation();
  const { uiTheme } = useAppearance();
  const branding = useBranding();
  const appName = useAppName();
  return (
    <header
      className={cn(
        'sticky top-0 z-20 flex h-14 items-center gap-1 border-b px-2 sm:gap-2 sm:px-4',
        uiTheme === 'modern' ? 'bg-background/90 backdrop-blur' : 'bg-header',
      )}
    >
      {onOpenMenu && (
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={onOpenMenu}
          aria-label={t('openMenu')}
        >
          <Menu aria-hidden />
        </Button>
      )}
      <AppLauncher />
      <Link
        to="/"
        className="hidden items-center gap-2 rounded-md px-1 font-semibold lg:flex"
        aria-label={branding.name ?? tc('appName')}
      >
        {branding.logoUrl ? (
          <img src={branding.logoUrl} alt="" className="size-7 rounded object-contain" />
        ) : (
          <span className="flex size-7 items-center justify-center rounded bg-primary text-primary-foreground">
            <Target className="size-4" aria-hidden />
          </span>
        )}
      </Link>
      {app && (
        <Link
          to={app.href as '/'}
          className={cn(
            'flex min-w-0 items-center gap-2 rounded-md px-1 py-1 hover:bg-muted',
            // Enterprise shows the app's name in its tab bar on wide screens.
            uiTheme === 'enterprise' && 'md:hidden',
            // Modern on phones: the app's sidebar (menu button) names the app; the top bar keeps room for its actions at 360 px.
            uiTheme === 'modern' && 'max-sm:hidden',
          )}
          data-testid="workspace-app"
        >
          <AppIcon app={app} size="sm" />
          <span className="hidden truncate text-sm font-semibold sm:inline">{appName(app)}</span>
          <span className="sr-only sm:hidden">{appName(app)}</span>
        </Link>
      )}
      {uiTheme === 'modern' ? <div className="flex-1" /> : null}
      {uiTheme === 'modern' ? <CommandPalette /> : <GlobalSearch />}
      {uiTheme !== 'modern' && <div className="flex-1" />}
      <AskButton />
      <NotificationBell />
      <LanguageToggle saveToProfile />
      {/* Modern on phones: one button more than fits at 360 px (menu + launcher + palette); light / dark stays in Profile → Preferences. */}
      <div className={cn('contents', uiTheme === 'modern' && 'max-sm:hidden')}>
        <ThemeMenu />
      </div>
      <ProfileMenu />
    </header>
  );
}

/** Enterprise: the current app's own navigation tabs under the top bar (only that system's areas). */
function AppTabBar({ app }: { app: ResolvedApp }) {
  const { t } = useTranslation('appearance');
  const appName = useAppName();
  const label = useTabLabel();
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const tabs = useGatedTabs(app);
  return (
    <nav
      aria-label={t('workspace.tabs', { name: appName(app) })}
      className="border-b bg-card shadow-[var(--shadow-card)]"
      data-testid="app-tabs"
    >
      <ScrollRegion className="flex items-stretch gap-1 overflow-x-auto px-3 md:px-6">
        <span className="hidden items-center gap-2 pe-4 text-base font-semibold md:flex">{appName(app)}</span>
        {tabs.map((tab) => {
          const active = isTabActive(tab, pathname, tabs);
          return (
            <Link
              key={tab.key}
              to={tab.to as '/'}
              className={cn(
                'flex h-10 shrink-0 items-center border-b-[3px] px-3 text-sm whitespace-nowrap',
                active
                  ? 'border-app-accent font-semibold text-foreground'
                  : 'border-transparent text-muted-foreground hover:border-border hover:text-foreground',
              )}
              aria-current={active ? 'page' : undefined}
              data-testid={`app-tab-${tab.key}`}
            >
              {label(app.code, tab)}
            </Link>
          );
        })}
      </ScrollRegion>
    </nav>
  );
}

/** Modern: collapsible sidebar inside the current app — its tabs, favourite apps and recent items. */
function AppSidebar({
  app,
  open,
  onClose,
}: {
  app: ResolvedApp | undefined;
  open: boolean;
  onClose: () => void;
}) {
  const { t, i18n } = useTranslation('appearance');
  const { t: ts } = useTranslation('shell');
  const appName = useAppName();
  const label = useTabLabel();
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const visible = useVisibleApps();
  const me = useMe();
  const favourites = (me?.appearance.favouriteApps ?? [])
    .map((code) => visible.find((a) => a.code === code))
    .filter((a): a is ResolvedApp => !!a);
  const [collapsed, setCollapsed] = useState(() => readPreference('sidebarCollapsed') === '1');
  const recent = useRecentItems(!collapsed);
  const tabs = useGatedTabs(app);
  const toggle = () => {
    writePreference('sidebarCollapsed', collapsed ? '0' : '1');
    setCollapsed(!collapsed);
  };

  if (collapsed) {
    return (
      <aside className="sticky top-14 hidden h-[calc(100dvh-3.5rem)] w-14 shrink-0 flex-col items-center gap-2 border-e bg-sidebar py-3 md:flex">
        <Button variant="ghost" size="icon" onClick={toggle} aria-label={t('workspace.expand')}>
          <PanelLeftOpen className="rtl:-scale-x-100" aria-hidden />
        </Button>
        {app && (
          <Link to={app.href as '/'} aria-label={appName(app)}>
            <AppIcon app={app} />
          </Link>
        )}
      </aside>
    );
  }

  return (
    <>
      {open && <div className="fixed inset-0 z-30 bg-black/40 md:hidden" onClick={onClose} aria-hidden />}
      <aside
        className={cn(
          'fixed inset-y-0 start-0 z-40 flex w-72 shrink-0 flex-col border-e bg-sidebar transition-transform md:sticky md:top-14 md:z-auto md:h-[calc(100dvh-3.5rem)] md:w-64',
          !open && 'max-md:invisible max-md:ltr:-translate-x-full max-md:rtl:translate-x-full',
        )}
        data-testid="app-sidebar"
      >
        <div className="flex items-start justify-between gap-2 p-4 pb-2">
          {app && (
            <div className="flex min-w-0 items-center gap-3">
              <AppIcon app={app} />
              <div className="min-w-0">
                <p className="truncate font-semibold [font-family:var(--font-heading)]">{appName(app)}</p>
              </div>
            </div>
          )}
          <Button
            variant="ghost"
            size="icon"
            className="hidden size-8 md:inline-flex"
            onClick={toggle}
            aria-label={t('workspace.collapse')}
          >
            <PanelLeftClose className="rtl:-scale-x-100" aria-hidden />
          </Button>
          <Button
            variant="ghost"
            size="icon"
            className="size-8 md:hidden"
            onClick={onClose}
            aria-label={ts('closeMenu')}
          >
            <X aria-hidden />
          </Button>
        </div>
        <div className="flex flex-1 flex-col gap-5 overflow-y-auto px-3 pb-4">
          {app && (
            <nav aria-label={t('workspace.tabs', { name: appName(app) })} data-testid="app-tabs">
              <ul className="flex flex-col gap-0.5">
                {tabs.map((tab) => {
                  const active = isTabActive(tab, pathname, tabs);
                  return (
                    <li key={tab.key}>
                      <Link
                        to={tab.to as '/'}
                        className={cn(
                          'relative flex items-center rounded-lg px-3 py-2 text-sm',
                          active
                            ? 'bg-card font-semibold text-foreground shadow-[var(--shadow-card)] before:absolute before:inset-y-2 before:start-0 before:w-1 before:rounded-full before:bg-app-accent'
                            : 'text-muted-foreground hover:bg-muted hover:text-foreground',
                        )}
                        aria-current={active ? 'page' : undefined}
                        data-testid={`app-tab-${tab.key}`}
                      >
                        {label(app.code, tab)}
                      </Link>
                    </li>
                  );
                })}
              </ul>
            </nav>
          )}
          {favourites.length > 0 && (
            <section aria-labelledby="sidebar-favourites">
              <h2
                id="sidebar-favourites"
                className="flex items-center gap-1.5 px-3 pb-1 text-xs font-semibold text-muted-foreground"
              >
                <Star className="size-3.5" aria-hidden />
                {t('workspace.favourites')}
              </h2>
              <ul className="flex flex-col gap-0.5">
                {favourites.map((fav) => (
                  <li key={fav.code}>
                    <Link
                      to={fav.href as '/'}
                      className="flex items-center gap-2 rounded-lg px-3 py-1.5 text-sm text-muted-foreground hover:bg-muted hover:text-foreground"
                    >
                      <AppIcon app={fav} size="sm" />
                      <span className="truncate">{appName(fav)}</span>
                    </Link>
                  </li>
                ))}
              </ul>
            </section>
          )}
          <section aria-labelledby="sidebar-recent">
            <h2
              id="sidebar-recent"
              className="flex items-center gap-1.5 px-3 pb-1 text-xs font-semibold text-muted-foreground"
            >
              <Clock className="size-3.5" aria-hidden />
              {t('workspace.recent')}
            </h2>
            {(recent.data ?? []).length === 0 ? (
              <p className="px-3 text-xs text-muted-foreground">{t('workspace.noRecent')}</p>
            ) : (
              <ul className="flex flex-col gap-0.5">
                {(recent.data ?? []).slice(0, 6).map((r) => (
                  <li key={`${r.item.type}:${r.item.id}`}>
                    <Link
                      to={r.item.link as '/'}
                      className="block rounded-lg px-3 py-1.5 text-sm hover:bg-muted"
                    >
                      <span className="block truncate">
                        {pickText(i18n.language, r.item.titleAr, r.item.titleEn) || r.item.number}
                      </span>
                      {r.item.number && (
                        <bdi className="block truncate text-xs text-muted-foreground">{r.item.number}</bdi>
                      )}
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </section>
        </div>
      </aside>
    </>
  );
}

/**
 * Enterprise utility bar (SPEC-ADDENDUM §C3): recent items and a quick note at the bottom of the screen on wide screens. The note stays
 * in this browser only (never sent to the server).
 */
function UtilityBar() {
  const { t, i18n } = useTranslation('appearance');
  const [recentOpen, setRecentOpen] = useState(false);
  const recent = useRecentItems(recentOpen);
  const [note, setNote] = useState(() => readPreference('quickNote') ?? '');
  return (
    <div
      role="toolbar"
      aria-label={t('utility.label')}
      className="fixed inset-x-0 bottom-0 z-20 hidden h-9 items-center gap-1 border-t bg-card px-4 shadow-[0_-1px_2px_rgb(0_0_0/0.06)] md:flex"
      data-testid="utility-bar"
    >
      <Popover.Root open={recentOpen} onOpenChange={setRecentOpen}>
        <Popover.Trigger asChild>
          <Button variant="ghost" size="sm" className="h-7">
            <Clock aria-hidden />
            {t('utility.recent')}
          </Button>
        </Popover.Trigger>
        <Popover.Portal>
          <Popover.Content
            side="top"
            align="start"
            sideOffset={6}
            className="z-50 w-80 rounded-md border bg-card p-2 shadow-lg"
          >
            <p className="px-2 pb-1 text-xs font-semibold text-muted-foreground">{t('utility.recent')}</p>
            {(recent.data ?? []).length === 0 ? (
              <p className="px-2 py-3 text-sm text-muted-foreground">{t('workspace.noRecent')}</p>
            ) : (
              <ul>
                {(recent.data ?? []).slice(0, 10).map((r) => (
                  <li key={`${r.item.type}:${r.item.id}`}>
                    <Link
                      to={r.item.link as '/'}
                      onClick={() => setRecentOpen(false)}
                      className="flex gap-2 rounded px-2 py-1.5 text-sm hover:bg-muted"
                    >
                      {r.item.number && <bdi className="shrink-0 text-muted-foreground">{r.item.number}</bdi>}
                      <span className="truncate">
                        {pickText(i18n.language, r.item.titleAr, r.item.titleEn)}
                      </span>
                    </Link>
                  </li>
                ))}
              </ul>
            )}
          </Popover.Content>
        </Popover.Portal>
      </Popover.Root>
      <Popover.Root>
        <Popover.Trigger asChild>
          <Button variant="ghost" size="sm" className="h-7">
            <NotebookPen aria-hidden />
            {t('utility.note')}
          </Button>
        </Popover.Trigger>
        <Popover.Portal>
          <Popover.Content
            side="top"
            align="start"
            sideOffset={6}
            className="z-50 w-80 rounded-md border bg-card p-3 shadow-lg"
          >
            <label className="flex flex-col gap-1 text-sm font-medium">
              {t('utility.note')}
              <textarea
                className="min-h-32 rounded-md border border-input bg-card p-2 text-sm font-normal"
                value={note}
                placeholder={t('utility.notePlaceholder')}
                onChange={(event) => {
                  setNote(event.target.value);
                  writePreference('quickNote', event.target.value);
                }}
              />
            </label>
          </Popover.Content>
        </Popover.Portal>
      </Popover.Root>
    </div>
  );
}
