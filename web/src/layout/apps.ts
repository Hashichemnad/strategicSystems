import {
  Award,
  BookOpen,
  Boxes,
  BriefcaseBusiness,
  Building2,
  ChartColumn,
  ChartPie,
  ClipboardCheck,
  ClipboardList,
  Compass,
  FileSearch,
  FileText,
  FolderKanban,
  Gauge,
  Globe,
  Inbox,
  Landmark,
  Layers,
  LayoutDashboard,
  Library,
  Lightbulb,
  ListTodo,
  Medal,
  Network,
  Presentation,
  Rocket,
  Scale,
  ScrollText,
  Settings2,
  ShieldAlert,
  ShieldCheck,
  Sparkles,
  Target,
  TrendingUp,
  Trophy,
  UsersRound,
  Workflow,
  type LucideIcon,
} from 'lucide-react';
import type { MyAccess } from '@/features/access/session';
import { settingsPermissions } from './navigation';

/**
 * Application workspaces (SPEC-ADDENDUM §C2, docs/design/themes.md §5, ADR 0172). One definition per app; the Enterprise and Modern
 * shells, the launcher, the command palette and the app home pages read only this registry. Routes are the existing ones: the current
 * app is derived from the URL, so deep links open the same page in every theme.
 */

export type AppCode =
  | 'mywork'
  | 'dashboards'
  | 'portfolio'
  | 'kpi'
  | 'risk'
  | 'teams'
  | 'tasks'
  | 'requests'
  | 'docreg'
  | 'ip'
  | 'bench'
  | 'cap'
  | 'auditmgmt'
  | 'org'
  | 'settings'
  | 'sample';

export type AppGroup = 'systems' | 'shared';

export interface AppTab {
  /** Stable key (Settings → Appearance tab order / hidden tabs). */
  key: string;
  to: string;
  /** Exact match only (the app's root page). */
  exact?: boolean;
  /** Shown when the user holds at least one of these permissions (any scope). */
  permissions?: readonly string[];
}

export interface QuickAction {
  key: string;
  to: string;
  permissions: readonly string[];
}

export interface AppDefinition {
  code: AppCode;
  group: AppGroup;
  /** Access gate: the user must be able to open this system (Settings → Systems, system access). */
  system?: string;
  icon: string;
  accent: string;
  /** URL prefixes that belong to the app (segment-aware: `/audit` does not match `/audit-management`). */
  basePaths: readonly string[];
  tabs: readonly AppTab[];
  /** Phase 17 system dashboard shown on the home page. */
  dashboard?: string;
  quickActions?: readonly QuickAction[];
  /** System apps have a home page at `/apps/<code>`; shared apps open their first tab. */
  hasHome: boolean;
}

const home = (code: AppCode): AppTab => ({ key: 'home', to: `/apps/${code}`, exact: true });

export const apps: readonly AppDefinition[] = [
  {
    code: 'mywork',
    group: 'shared',
    icon: 'inbox',
    accent: '#0f766e',
    basePaths: ['/my-work', '/notifications', '/profile'],
    tabs: [
      { key: 'mywork', to: '/my-work', exact: true },
      { key: 'notifications', to: '/notifications' },
      { key: 'profile', to: '/profile' },
    ],
    hasHome: false,
  },
  {
    code: 'dashboards',
    group: 'shared',
    icon: 'presentation',
    accent: '#7e22ce',
    basePaths: ['/dashboards', '/reports'],
    tabs: [
      { key: 'executive', to: '/dashboards/executive', permissions: ['reporting.executive.read'] },
      { key: 'dashboards', to: '/dashboards', exact: true, permissions: ['reporting.dashboard.read'] },
      { key: 'reports', to: '/reports', permissions: ['reporting.report.run'] },
    ],
    hasHome: false,
  },
  {
    code: 'portfolio',
    group: 'systems',
    system: 'portfolio',
    icon: 'folder-kanban',
    accent: '#2563eb',
    basePaths: ['/portfolio', '/apps/portfolio'],
    tabs: [
      home('portfolio'),
      { key: 'requests', to: '/portfolio/requests', permissions: ['portfolio.request.read'] },
      { key: 'projects', to: '/portfolio/projects', permissions: ['portfolio.project.read'] },
      { key: 'dashboard', to: '/portfolio/dashboard', permissions: ['portfolio.project.read'] },
      { key: 'gantt', to: '/portfolio/gantt', permissions: ['portfolio.project.read'] },
      { key: 'portfolios', to: '/portfolio/portfolios', permissions: ['portfolio.project.read'] },
      { key: 'vendors', to: '/portfolio/vendors', permissions: ['portfolio.vendor.read'] },
      { key: 'capacity', to: '/portfolio/capacity', permissions: ['portfolio.capacity.read'] },
      { key: 'reviews', to: '/portfolio/reviews', permissions: ['portfolio.review.read'] },
    ],
    dashboard: 'portfolio.overview',
    quickActions: [
      { key: 'newRequest', to: '/portfolio/requests/new', permissions: ['portfolio.request.create'] },
      { key: 'projects', to: '/portfolio/projects', permissions: ['portfolio.project.read'] },
    ],
    hasHome: true,
  },
  {
    code: 'kpi',
    group: 'systems',
    system: 'kpi',
    icon: 'gauge',
    accent: '#7c3aed',
    basePaths: ['/kpi', '/apps/kpi'],
    tabs: [
      home('kpi'),
      { key: 'dashboard', to: '/kpi', exact: true, permissions: ['kpi.card.read'] },
      { key: 'cards', to: '/kpi/cards', permissions: ['kpi.card.read'] },
      { key: 'pending', to: '/kpi/pending', permissions: ['kpi.card.read'] },
      { key: 'approvals', to: '/kpi/approvals', permissions: ['kpi.card.read'] },
      { key: 'scorecard', to: '/kpi/scorecard', permissions: ['kpi.scorecard.read', 'kpi.card.read'] },
      { key: 'strategyMap', to: '/kpi/strategy-map', permissions: ['kpi.card.read'] },
      { key: 'reports', to: '/kpi/reports', permissions: ['kpi.report.export', 'kpi.card.read'] },
      { key: 'newYear', to: '/kpi/new-year', permissions: ['kpi.plan.manage'] },
    ],
    dashboard: 'kpi.scorecard',
    quickActions: [
      { key: 'newKpi', to: '/kpi/cards/new', permissions: ['kpi.card.manage'] },
      { key: 'pendingUpdates', to: '/kpi/pending', permissions: ['kpi.card.read'] },
    ],
    hasHome: true,
  },
  {
    code: 'risk',
    group: 'systems',
    system: 'risk',
    icon: 'shield-alert',
    accent: '#dc2626',
    basePaths: ['/risk', '/apps/risk'],
    tabs: [
      home('risk'),
      { key: 'dashboard', to: '/risk', exact: true, permissions: ['risk.dashboard.read'] },
      { key: 'register', to: '/risk/risks', permissions: ['risk.risk.read'] },
      { key: 'myActions', to: '/risk/my-actions', permissions: ['risk.risk.read'] },
      { key: 'projectRisks', to: '/risk/project-risks', permissions: ['risk.project-risks.read'] },
      { key: 'mechanism', to: '/risk/mechanism', permissions: ['risk.risk.read'] },
      { key: 'reports', to: '/risk/reports', permissions: ['risk.report.export'] },
    ],
    dashboard: 'risk.profile',
    quickActions: [
      { key: 'newRisk', to: '/risk/risks/new', permissions: ['risk.risk.create'] },
      { key: 'myActions', to: '/risk/my-actions', permissions: ['risk.risk.read'] },
    ],
    hasHome: true,
  },
  {
    code: 'tasks',
    group: 'shared',
    system: 'tasks',
    icon: 'list-todo',
    accent: '#c2410c',
    basePaths: ['/tasks', '/apps/tasks'],
    tabs: [
      home('tasks'),
      { key: 'tasks', to: '/tasks', exact: true, permissions: ['tasks.task.read'] },
      { key: 'dashboard', to: '/tasks/dashboard', permissions: ['tasks.dashboard.read'] },
    ],
    dashboard: 'tasks.performance',
    quickActions: [{ key: 'tasks', to: '/tasks', permissions: ['tasks.task.read'] }],
    hasHome: true,
  },
  {
    code: 'teams',
    group: 'shared',
    system: 'teams',
    icon: 'users-round',
    accent: '#0e7490',
    basePaths: ['/teams', '/apps/teams'],
    tabs: [
      home('teams'),
      { key: 'teams', to: '/teams', exact: true, permissions: ['teams.team.read'] },
      { key: 'dashboard', to: '/teams/dashboard', permissions: ['teams.dashboard.read'] },
    ],
    dashboard: 'teams.overview',
    quickActions: [{ key: 'newTeam', to: '/teams/new', permissions: ['teams.team.request'] }],
    hasHome: true,
  },
  {
    code: 'requests',
    group: 'systems',
    system: 'requests',
    icon: 'inbox',
    accent: '#0f766e',
    basePaths: ['/requests', '/apps/requests'],
    tabs: [
      home('requests'),
      {
        key: 'catalogue',
        to: '/requests',
        exact: true,
        permissions: ['requests.request.submit', 'requests.request.read'],
      },
      {
        key: 'mine',
        to: '/requests/mine',
        permissions: ['requests.request.submit', 'requests.request.read'],
      },
      { key: 'queue', to: '/requests/queue', permissions: ['requests.request.process'] },
    ],
    quickActions: [
      {
        key: 'catalogue',
        to: '/requests',
        permissions: ['requests.request.submit', 'requests.request.read'],
      },
    ],
    hasHome: true,
  },
  {
    code: 'docreg',
    group: 'systems',
    system: 'docreg',
    icon: 'library',
    accent: '#4f46e5',
    basePaths: ['/documents', '/apps/docreg'],
    tabs: [
      home('docreg'),
      { key: 'library', to: '/documents', exact: true, permissions: ['docreg.document.read'] },
      { key: 'dashboard', to: '/documents/dashboard', permissions: ['docreg.dashboard.read'] },
    ],
    dashboard: 'docreg.library',
    quickActions: [{ key: 'library', to: '/documents', permissions: ['docreg.document.read'] }],
    hasHome: true,
  },
  {
    code: 'ip',
    group: 'systems',
    system: 'ip',
    icon: 'lightbulb',
    accent: '#a16207',
    basePaths: ['/ip', '/apps/ip'],
    tabs: [
      home('ip'),
      { key: 'register', to: '/ip', exact: true, permissions: ['ip.record.read'] },
      { key: 'dashboard', to: '/ip/dashboard', permissions: ['ip.dashboard.read'] },
    ],
    dashboard: 'ip.register',
    quickActions: [{ key: 'register', to: '/ip', permissions: ['ip.record.read'] }],
    hasHome: true,
  },
  {
    code: 'bench',
    group: 'systems',
    system: 'bench',
    icon: 'scale',
    accent: '#047857',
    basePaths: ['/bench', '/apps/bench'],
    tabs: [
      home('bench'),
      { key: 'dashboard', to: '/bench', exact: true, permissions: ['bench.study.read'] },
      { key: 'plan', to: '/bench/plan', permissions: ['bench.plan.read', 'bench.study.read'] },
      { key: 'studies', to: '/bench/studies', permissions: ['bench.study.read'] },
      {
        key: 'comparators',
        to: '/bench/comparators',
        permissions: ['bench.comparator.read', 'bench.study.read'],
      },
      { key: 'practices', to: '/bench/practices', permissions: ['bench.practice.read', 'bench.study.read'] },
    ],
    dashboard: 'bench.plan',
    quickActions: [{ key: 'studies', to: '/bench/studies', permissions: ['bench.study.read'] }],
    hasHome: true,
  },
  {
    code: 'cap',
    group: 'systems',
    system: 'cap',
    icon: 'clipboard-list',
    accent: '#be185d',
    basePaths: ['/cap', '/apps/cap'],
    tabs: [
      home('cap'),
      { key: 'dashboard', to: '/cap', exact: true, permissions: ['cap.remark.read'] },
      { key: 'sources', to: '/cap/sources', permissions: ['cap.remark.read'] },
      { key: 'remarks', to: '/cap/remarks', permissions: ['cap.remark.read'] },
      { key: 'actions', to: '/cap/my-actions', permissions: ['cap.remark.read'] },
      { key: 'suggestions', to: '/cap/suggestions', permissions: ['cap.office.manage'] },
    ],
    dashboard: 'cap.closure',
    quickActions: [{ key: 'myActions', to: '/cap/my-actions', permissions: ['cap.remark.read'] }],
    hasHome: true,
  },
  {
    code: 'auditmgmt',
    group: 'systems',
    system: 'auditmgmt',
    icon: 'file-search',
    accent: '#475569',
    basePaths: ['/audit-management', '/apps/auditmgmt'],
    tabs: [
      home('auditmgmt'),
      {
        key: 'dashboard',
        to: '/audit-management',
        exact: true,
        permissions: ['auditmgmt.dashboard.read', 'auditmgmt.finding.read'],
      },
      { key: 'department', to: '/audit-management/my-department', permissions: ['auditmgmt.finding.read'] },
      { key: 'reports', to: '/audit-management/reports', permissions: ['auditmgmt.finding.read'] },
      { key: 'findings', to: '/audit-management/findings', permissions: ['auditmgmt.finding.read'] },
      { key: 'calendar', to: '/audit-management/calendar', permissions: ['auditmgmt.office.manage'] },
      { key: 'universe', to: '/audit-management/universe', permissions: ['auditmgmt.lifecycle.manage'] },
      { key: 'plans', to: '/audit-management/plans', permissions: ['auditmgmt.plan.read'] },
    ],
    dashboard: 'auditmgmt.closure',
    quickActions: [
      { key: 'findings', to: '/audit-management/findings', permissions: ['auditmgmt.finding.read'] },
    ],
    hasHome: true,
  },
  {
    code: 'org',
    group: 'shared',
    icon: 'building',
    accent: '#0369a1',
    basePaths: ['/org'],
    tabs: [
      { key: 'units', to: '/org/units', permissions: ['org.unit.read'] },
      { key: 'chart', to: '/org/chart', permissions: ['org.unit.read'] },
      { key: 'employees', to: '/org/employees', permissions: ['org.employee.read'] },
      { key: 'levels', to: '/org/levels', permissions: ['org.unit.manage'] },
      { key: 'import', to: '/org/import', permissions: ['org.import.manage'] },
    ],
    hasHome: false,
  },
  {
    code: 'settings',
    group: 'shared',
    icon: 'settings',
    accent: '#52525b',
    basePaths: ['/settings', '/admin', '/audit', '/setup/configure'],
    tabs: [
      { key: 'settings', to: '/settings', permissions: settingsPermissions },
      { key: 'auditLog', to: '/audit', exact: true, permissions: ['audit.log.view'] },
      { key: 'verification', to: '/audit/verification', permissions: ['audit.chain.verify'] },
    ],
    hasHome: false,
  },
  {
    code: 'sample',
    group: 'systems',
    system: 'sample',
    icon: 'boxes',
    accent: '#64748b',
    basePaths: ['/sample'],
    tabs: [{ key: 'sample', to: '/sample' }],
    hasHome: false,
  },
];

/** Icons an administrator can choose for an app (Settings → Appearance), by Lucide name. */
export const appIcons: Record<string, LucideIcon> = {
  award: Award,
  'book-open': BookOpen,
  boxes: Boxes,
  'briefcase-business': BriefcaseBusiness,
  building: Building2,
  'chart-column': ChartColumn,
  'chart-pie': ChartPie,
  'clipboard-check': ClipboardCheck,
  'clipboard-list': ClipboardList,
  compass: Compass,
  'file-search': FileSearch,
  'file-text': FileText,
  'folder-kanban': FolderKanban,
  gauge: Gauge,
  globe: Globe,
  inbox: Inbox,
  landmark: Landmark,
  layers: Layers,
  'layout-dashboard': LayoutDashboard,
  library: Library,
  lightbulb: Lightbulb,
  'list-todo': ListTodo,
  medal: Medal,
  network: Network,
  presentation: Presentation,
  rocket: Rocket,
  scale: Scale,
  'scroll-text': ScrollText,
  settings: Settings2,
  'shield-alert': ShieldAlert,
  'shield-check': ShieldCheck,
  sparkles: Sparkles,
  target: Target,
  'trending-up': TrendingUp,
  trophy: Trophy,
  'users-round': UsersRound,
  workflow: Workflow,
};

/** Per-app overrides from Settings → Appearance (the server document's `apps`). */
export interface AppOverride {
  nameAr?: string | null;
  nameEn?: string | null;
  icon?: string | null;
  accent?: string | null;
  tabOrder?: readonly string[] | null;
  hiddenTabs?: readonly string[] | null;
  dashboard?: string | null;
}

export type AppOverrides = Partial<Record<string, AppOverride>>;

/** An app as one user sees it: overrides applied, tabs filtered by their permissions. */
export interface ResolvedApp extends AppDefinition {
  nameAr: string | null;
  nameEn: string | null;
  iconComponent: LucideIcon;
  visibleTabs: AppTab[];
  /** Where the app opens: its home page, or its first visible tab. */
  href: string;
}

function holds(access: MyAccess | undefined, permissions: readonly string[] | undefined): boolean {
  return !permissions || permissions.some((permission) => access?.permissions.includes(permission));
}

/** Applies the admin's tab order (listed tabs first, in that order; the others after, in their default order) and hidden tabs. */
export function orderTabs(tabs: readonly AppTab[], override: AppOverride | undefined): AppTab[] {
  const hidden = new Set(override?.hiddenTabs ?? []);
  const order = override?.tabOrder ?? [];
  const rank = (tab: AppTab) => {
    const index = order.indexOf(tab.key);
    return index < 0 ? order.length + tabs.indexOf(tab) : index;
  };
  // Home is never hidden: it is the app's landing page.
  return [...tabs]
    .filter((tab) => tab.key === 'home' || !hidden.has(tab.key))
    .sort((a, b) => rank(a) - rank(b));
}

/**
 * The apps a user can open. An app is visible when the user may open its system (if any) and at least one of its tabs (other than
 * home) is allowed by their permissions. Overrides change names, icons, colours and order — never access.
 */
export function visibleApps(access: MyAccess | undefined, overrides: AppOverrides = {}): ResolvedApp[] {
  if (!access) return [];
  return apps.flatMap((app) => {
    if (app.system && !access.systems.includes(app.system)) return [];
    const override = overrides[app.code];
    const allowed = app.tabs.filter((tab) => tab.key === 'home' || holds(access, tab.permissions));
    if (!allowed.some((tab) => tab.key !== 'home')) return [];
    const visibleTabs = orderTabs(allowed, override);
    const first = visibleTabs.find((tab) => tab.key !== 'home') ?? visibleTabs[0];
    const icon = (override?.icon && appIcons[override.icon]) || appIcons[app.icon] || LayoutDashboard;
    return [
      {
        ...app,
        accent: override?.accent ?? app.accent,
        dashboard: override?.dashboard ?? app.dashboard,
        nameAr: override?.nameAr?.trim() || null,
        nameEn: override?.nameEn?.trim() || null,
        iconComponent: icon,
        visibleTabs,
        href: app.hasHome ? `/apps/${app.code}` : (first?.to ?? '/'),
      },
    ];
  });
}

function matches(pathname: string, base: string): boolean {
  return pathname === base || pathname.startsWith(`${base}/`);
}

/** The app a URL belongs to (longest matching base path); undefined for pages outside every app. */
export function appForPath(pathname: string): AppCode | undefined {
  let best: { code: AppCode; length: number } | undefined;
  for (const app of apps) {
    for (const base of app.basePaths) {
      if (matches(pathname, base) && (!best || base.length > best.length))
        best = { code: app.code, length: base.length };
    }
  }
  return best?.code;
}

/** Whether a tab is the active one for the URL (exact tabs only on their own path; others by prefix). */
export function isTabActive(tab: AppTab, pathname: string, tabs: readonly AppTab[]): boolean {
  if (tab.exact) return pathname === tab.to;
  if (!matches(pathname, tab.to)) return false;
  // A longer tab path wins (e.g. /kpi/cards over a /kpi prefix tab).
  return !tabs.some(
    (other) => other !== tab && other.to.length > tab.to.length && matches(pathname, other.to),
  );
}

export interface LandingRule {
  roleCode: string;
  app: string;
}

/**
 * The app a signed-in user lands on at `/` in the Enterprise and Modern themes (docs/design/themes.md §6, ADR 0173): their own default
 * app if they can open it; else the first admin rule for one of their roles whose app they can open; else My Work.
 */
export function resolveLandingApp(
  visible: readonly Pick<ResolvedApp, 'code'>[],
  roles: readonly string[],
  userDefault: string | null | undefined,
  rules: readonly LandingRule[],
): AppCode {
  const canOpen = (code: string | null | undefined): code is AppCode =>
    !!code && visible.some((app) => app.code === code);
  if (canOpen(userDefault)) return userDefault;
  for (const rule of rules) {
    if (roles.includes(rule.roleCode) && canOpen(rule.app)) return rule.app;
  }
  return 'mywork';
}

/** The home URL of an app the user can open (My Work for anything else). */
export function appHref(visible: readonly ResolvedApp[], code: AppCode): string {
  return visible.find((app) => app.code === code)?.href ?? '/my-work';
}

/**
 * WCAG contrast ratio of white text / icons on a hex colour (≥ 3 : 1 for icons on an accent tile). Used by the Appearance page to warn
 * about an admin-chosen accent.
 */
export function contrastWithWhite(hex: string): number {
  const match = /^#([0-9a-f]{6})$/i.exec(hex);
  if (!match?.[1]) return 1;
  const value = parseInt(match[1], 16);
  const channel = (shift: number) => {
    const c = ((value >> shift) & 0xff) / 255;
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };
  const luminance = 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0);
  return 1.05 / (luminance + 0.05);
}
