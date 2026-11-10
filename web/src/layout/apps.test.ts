import { describe, expect, it } from 'vitest';
import ar from '@/i18n/ar/appearance.json';
import en from '@/i18n/en/appearance.json';
import type { MyAccess } from '@/features/access/session';
import {
  appForPath,
  apps,
  appIcons,
  contrastWithWhite,
  isTabActive,
  orderTabs,
  resolveLandingApp,
  visibleApps,
} from './apps';

const access = (systems: string[], permissions: string[], roles: string[] = []): MyAccess => ({
  systems,
  permissions,
  roles,
});

describe('app launcher visibility (SPEC-ADDENDUM §C2)', () => {
  it('shows only systems the user can open and holds a permission of', () => {
    const user = access(['kpi', 'risk'], ['kpi.card.read']);
    const codes = visibleApps(user).map((a) => a.code);
    expect(codes).toContain('kpi');
    // Risk is open for the user but they hold no risk permission: not shown.
    expect(codes).not.toContain('risk');
    expect(codes).not.toContain('portfolio');
    // My Work is for everyone.
    expect(codes).toContain('mywork');
    expect(codes).not.toContain('settings');
  });

  it('never shows a system without system access, even with its permissions', () => {
    const user = access([], ['portfolio.project.read', 'kpi.card.read']);
    expect(visibleApps(user).map((a) => a.code)).toEqual(['mywork']);
  });

  it('shows nothing before access is known', () => {
    expect(visibleApps(undefined)).toEqual([]);
  });

  it('filters tabs by permission and opens system apps on their home page', () => {
    const user = access(['portfolio'], ['portfolio.project.read']);
    const portfolio = visibleApps(user).find((a) => a.code === 'portfolio');
    expect(portfolio?.href).toBe('/apps/portfolio');
    expect(portfolio?.visibleTabs.map((tab) => tab.key)).toEqual([
      'home',
      'projects',
      'dashboard',
      'gantt',
      'portfolios',
    ]);
  });

  it('applies overrides (names, icon, accent, order, hidden tabs) without adding access', () => {
    const user = access(['kpi'], ['kpi.card.read']);
    const [kpi] = visibleApps(user, {
      kpi: {
        nameEn: 'Performance hub',
        icon: 'target',
        accent: '#123456',
        tabOrder: ['cards', 'home'],
        hiddenTabs: ['reports', 'home', 'newYear'],
      },
      risk: { nameEn: 'Risk' },
    }).filter((a) => a.code === 'kpi');
    expect(kpi?.nameEn).toBe('Performance hub');
    expect(kpi?.accent).toBe('#123456');
    expect(kpi?.iconComponent).toBe(appIcons.target);
    // Listed tabs first, home is never hidden, "reports" hidden, "newYear" not permitted anyway.
    expect(kpi?.visibleTabs.map((t) => t.key)).toEqual([
      'cards',
      'home',
      'dashboard',
      'pending',
      'approvals',
      'scorecard',
      'strategyMap',
    ]);
    expect(visibleApps(user, { risk: { nameEn: 'Risk' } }).some((a) => a.code === 'risk')).toBe(false);
  });

  it('orders tabs: listed first, unknown keys ignored, new tabs appended', () => {
    const tabs = [
      { key: 'a', to: '/a' },
      { key: 'b', to: '/b' },
      { key: 'c', to: '/c' },
    ];
    expect(orderTabs(tabs, { tabOrder: ['c', 'zzz'] }).map((t) => t.key)).toEqual(['c', 'a', 'b']);
  });
});

describe('landing app per role (ADR 0173)', () => {
  const visible = [{ code: 'mywork' }, { code: 'kpi' }, { code: 'auditmgmt' }] as const;
  const rules = [
    { roleCode: 'portfolio.pmo', app: 'portfolio' },
    { roleCode: 'auditmgmt.office', app: 'auditmgmt' },
    { roleCode: 'kpi.editor', app: 'kpi' },
  ];

  it.each([
    // roles, user default, expected
    [[], null, 'mywork'],
    [['kpi.editor'], null, 'kpi'],
    [['kpi.editor', 'auditmgmt.office'], null, 'auditmgmt'], // first matching rule wins
    [['portfolio.pmo'], null, 'mywork'], // the rule's app is not accessible
    [['kpi.editor'], 'auditmgmt', 'auditmgmt'], // own default first
    [['kpi.editor'], 'portfolio', 'kpi'], // own default not accessible: rules apply
  ] as const)('roles %j, default %s → %s', (roles, userDefault, expected) => {
    expect(resolveLandingApp(visible, roles, userDefault, rules)).toBe(expected);
  });
});

describe('current app and active tab', () => {
  it.each([
    ['/portfolio/projects/123', 'portfolio'],
    ['/apps/kpi', 'kpi'],
    ['/audit-management/findings', 'auditmgmt'],
    ['/audit', 'settings'],
    ['/settings/appearance', 'settings'],
    ['/documents/abc', 'docreg'],
    ['/my-work', 'mywork'],
    ['/unknown', undefined],
  ])('%s → %s', (path, code) => {
    expect(appForPath(path)).toBe(code);
  });

  it('marks the longest matching tab active', () => {
    const kpi = apps.find((a) => a.code === 'kpi');
    if (!kpi) throw new Error('kpi app');
    const active = (path: string) =>
      kpi.tabs.filter((tab) => isTabActive(tab, path, kpi.tabs)).map((t) => t.key);
    expect(active('/kpi')).toEqual(['dashboard']);
    expect(active('/kpi/cards/42')).toEqual(['cards']);
    expect(active('/apps/kpi')).toEqual(['home']);
  });
});

describe('app definitions', () => {
  const lookup = (bundle: Record<string, unknown>, path: string) =>
    path
      .split('.')
      .reduce<unknown>((node, key) => (node as Record<string, unknown> | undefined)?.[key], bundle);

  it('have names, descriptions and tab labels in Arabic and English', () => {
    for (const bundle of [ar, en]) {
      for (const app of apps) {
        expect(lookup(bundle, `apps.${app.code}.name`), app.code).toBeTypeOf('string');
        expect(lookup(bundle, `apps.${app.code}.description`), app.code).toBeTypeOf('string');
        for (const tab of app.tabs) {
          const key = tab.key === 'home' ? 'tabs.home' : `tabs.${app.code}.${tab.key}`;
          expect(lookup(bundle, key), key).toBeTypeOf('string');
        }
        for (const action of app.quickActions ?? []) {
          expect(lookup(bundle, `quickActions.${action.key}`), action.key).toBeTypeOf('string');
        }
      }
    }
  });

  it('use known icons and accents with at least 3 : 1 contrast for white icons', () => {
    for (const app of apps) {
      expect(appIcons[app.icon], app.code).toBeDefined();
      expect(contrastWithWhite(app.accent), app.code).toBeGreaterThanOrEqual(3);
    }
  });

  it('use the same app codes as the server catalogue', () => {
    // Strategy.Platform.Settings.AppearanceCatalogue.Apps
    expect(apps.map((a) => a.code)).toEqual([
      'mywork',
      'dashboards',
      'portfolio',
      'kpi',
      'risk',
      'tasks',
      'teams',
      'requests',
      'docreg',
      'ip',
      'bench',
      'cap',
      'auditmgmt',
      'org',
      'settings',
      'sample',
    ]);
  });
});
