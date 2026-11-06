import { apiPut, expect, test, tr } from './fixtures';
import { expectAccessible, settle } from './a11y';
import type { Page } from '@playwright/test';

/**
 * WCAG 2.1 AA audit (docs/accessibility.md): axe-core on every main screen in Arabic (RTL) and English (LTR), record
 * pages with each of their tabs, a dark-theme subset, keyboard operation, dialog focus handling and reflow at 360 px.
 * Run after the other specs on the same database (record pages need data, as the Phase 17 reporting spec does).
 */

interface Screen {
  name: string;
  path: string;
}

/** List, dashboard and settings screens, as the administrator (Mariam holds every office role in the demo seed). */
const screens: Screen[] = [
  { name: 'my-work', path: '/' },
  { name: 'approvals', path: '/approvals' },
  { name: 'notifications', path: '/notifications' },
  { name: 'profile', path: '/profile' },
  { name: 'ai-assistant', path: '/ai' },
  { name: 'dashboards', path: '/dashboards' },
  { name: 'executive-dashboard', path: '/dashboards/executive' },
  { name: 'system-dashboard', path: '/dashboards/kpi.scorecard' },
  { name: 'reports', path: '/reports' },
  { name: 'report-builder', path: '/reports/new' },
  { name: 'report-schedules', path: '/reports/schedules' },
  { name: 'org-units', path: '/org/units' },
  { name: 'org-chart', path: '/org/chart' },
  { name: 'org-employees', path: '/org/employees' },
  { name: 'tasks', path: '/tasks' },
  { name: 'tasks-dashboard', path: '/tasks/dashboard' },
  { name: 'teams', path: '/teams' },
  { name: 'teams-dashboard', path: '/teams/dashboard' },
  { name: 'portfolio', path: '/portfolio' },
  { name: 'portfolio-requests', path: '/portfolio/requests' },
  { name: 'portfolio-request-new', path: '/portfolio/requests/new' },
  { name: 'portfolio-projects', path: '/portfolio/projects' },
  { name: 'portfolio-dashboard', path: '/portfolio/dashboard' },
  { name: 'portfolio-gantt', path: '/portfolio/gantt' },
  { name: 'portfolio-capacity', path: '/portfolio/capacity' },
  { name: 'portfolio-portfolios', path: '/portfolio/portfolios' },
  { name: 'portfolio-reviews', path: '/portfolio/reviews' },
  { name: 'portfolio-vendors', path: '/portfolio/vendors' },
  { name: 'kpi', path: '/kpi' },
  { name: 'kpi-cards', path: '/kpi/cards' },
  { name: 'kpi-card-new', path: '/kpi/cards/new' },
  { name: 'kpi-pending', path: '/kpi/pending' },
  { name: 'kpi-approvals', path: '/kpi/approvals' },
  { name: 'kpi-scorecard', path: '/kpi/scorecard' },
  { name: 'kpi-strategy-map', path: '/kpi/strategy-map' },
  { name: 'kpi-reports', path: '/kpi/reports' },
  { name: 'risk', path: '/risk' },
  { name: 'risk-register', path: '/risk/risks' },
  { name: 'risk-new', path: '/risk/risks/new' },
  { name: 'risk-project-risks', path: '/risk/project-risks' },
  { name: 'risk-my-actions', path: '/risk/my-actions' },
  { name: 'risk-mechanism', path: '/risk/mechanism' },
  { name: 'risk-reports', path: '/risk/reports' },
  { name: 'requests', path: '/requests' },
  { name: 'requests-mine', path: '/requests/mine' },
  { name: 'requests-queue', path: '/requests/queue' },
  { name: 'docreg', path: '/docreg' },
  { name: 'documents', path: '/documents' },
  { name: 'documents-dashboard', path: '/documents/dashboard' },
  { name: 'ip', path: '/ip' },
  { name: 'ip-dashboard', path: '/ip/dashboard' },
  { name: 'bench', path: '/bench' },
  { name: 'bench-plan', path: '/bench/plan' },
  { name: 'bench-studies', path: '/bench/studies' },
  { name: 'bench-practices', path: '/bench/practices' },
  { name: 'bench-comparators', path: '/bench/comparators' },
  { name: 'cap', path: '/cap' },
  { name: 'cap-remarks', path: '/cap/remarks' },
  { name: 'cap-my-actions', path: '/cap/my-actions' },
  { name: 'cap-sources', path: '/cap/sources' },
  { name: 'cap-suggestions', path: '/cap/suggestions' },
  { name: 'audit-management', path: '/audit-management' },
  { name: 'audit-findings', path: '/audit-management/findings' },
  { name: 'audit-plans', path: '/audit-management/plans' },
  { name: 'audit-universe', path: '/audit-management/universe' },
  { name: 'audit-calendar', path: '/audit-management/calendar' },
  { name: 'audit-reports', path: '/audit-management/reports' },
  { name: 'audit-my-department', path: '/audit-management/my-department' },
  { name: 'audit-log', path: '/audit' },
  { name: 'audit-verification', path: '/audit/verification' },
  { name: 'sample', path: '/sample' },
];

const settingsScreens: Screen[] = [
  '/settings/regional',
  '/settings/branding',
  '/settings/systems',
  '/settings/features',
  '/settings/lookups',
  '/settings/numbering',
  '/settings/translations',
  '/settings/notifications',
  '/settings/files',
  '/settings/jobs',
  '/settings/health',
  '/settings/approvals',
  '/settings/ai',
  '/settings/mcp',
  '/settings/dashboards',
  '/settings/reporting',
  '/settings/tasks',
  '/settings/tasks/reassign',
  '/settings/teams',
  '/settings/portfolio',
  '/settings/kpi',
  '/settings/risk',
  '/settings/request-types',
  '/settings/docreg',
  '/settings/ip',
  '/settings/cap',
  '/settings/bench',
  '/settings/audit-management',
  '/settings/business-hours',
  '/settings/holidays',
  '/settings/access/roles',
  '/settings/access/assignments',
  '/settings/access/system-access',
  '/settings/access/transfer',
  '/settings/access/review',
  '/admin/users',
  '/admin/providers',
  '/admin/security',
  '/admin/email',
  '/org/levels',
  '/org/import',
  '/setup/configure',
].map((path) => ({ name: `settings${path.replace(/\//gu, '-')}`, path }));

const projectHref = /^\/portfolio\/projects\/[0-9a-f-]{36}$/u;

/** Record pages: the first record of a list, with every tab of the record. */
const records: { name: string; list: string; href: RegExp; optional?: boolean }[] = [
  { name: 'portfolio-request', list: '/portfolio/requests', href: /^\/portfolio\/requests\/[0-9a-f-]{36}$/u },
  { name: 'portfolio-project', list: '/portfolio/projects', href: projectHref },
  { name: 'portfolio-vendor', list: '/portfolio/vendors', href: /^\/portfolio\/vendors\/[0-9a-f-]{36}$/u },
  { name: 'kpi-card', list: '/kpi/cards', href: /^\/kpi\/cards\/[0-9a-f-]{36}$/u },
  { name: 'risk', list: '/risk/risks', href: /^\/risk\/risks\/[0-9a-f-]{36}$/u },
  { name: 'team', list: '/teams', href: /^\/teams\/[0-9a-f-]{36}$/u },
  { name: 'task', list: '/tasks?tab=created', href: /^\/tasks\/[0-9a-f-]{36}$/u },
  { name: 'request', list: '/requests/queue', href: /^\/requests\/[0-9a-f-]{36}$/u, optional: true },
  { name: 'document', list: '/documents', href: /^\/documents\/[0-9a-f-]{36}$/u },
  { name: 'ip-record', list: '/ip', href: /^\/ip\/records\/[0-9a-f-]{36}$/u },
  { name: 'bench-study', list: '/bench/studies', href: /^\/bench\/studies\/[0-9a-f-]{36}$/u },
  { name: 'cap-remark', list: '/cap/remarks', href: /^\/cap\/remarks\/[0-9a-f-]{36}$/u },
  { name: 'cap-source', list: '/cap/sources', href: /^\/cap\/sources\/[0-9a-f-]{36}$/u },
  {
    name: 'audit-finding',
    list: '/audit-management/findings',
    href: /^\/audit-management\/findings\/[0-9a-f-]{36}$/u,
  },
  {
    name: 'audit-plan',
    list: '/audit-management/plans',
    href: /^\/audit-management\/plans\/[0-9a-f-]{36}$/u,
    optional: true,
  },
  { name: 'employee', list: '/org/employees', href: /^\/org\/employees\/[0-9a-f-]{36}$/u },
  { name: 'sample-item', list: '/sample', href: /^\/sample\/items\/[0-9a-f-]{36}$/u },
];

async function firstRecordHref(page: Page, list: string, href: RegExp, skip = 0): Promise<string | null> {
  await page.goto(list);
  await settle(page);
  const hrefs = await page
    .locator('a[href]')
    .evaluateAll((anchors) => anchors.map((a) => new URL((a as HTMLAnchorElement).href).pathname));
  return hrefs.filter((h) => href.test(h))[skip] ?? null;
}

/** Audits the record page, then each of its tabs in turn. */
async function auditRecord(page: Page, name: string, path: string) {
  await page.goto(path);
  await expectAccessible(page, name);
  const tabs = page.getByRole('tab');
  const count = await tabs.count();
  for (let i = 1; i < count; i++) {
    const tab = tabs.nth(i);
    if (!(await tab.isVisible()) || (await tab.isDisabled())) continue;
    const label = (await tab.textContent())?.trim() || `tab-${i}`;
    await tab.click();
    await expectAccessible(page, `${name} › ${label}`);
  }
}

test.describe('accessibility — WCAG 2.1 AA (axe-core)', () => {
  test.use({ signedInAs: 'mariam' });

  test('signed-out pages: sign-in, forgot password, Wasel sign-in', async ({
    browser,
    baseURL,
    language,
  }) => {
    const context = await browser.newContext({ baseURL, locale: language === 'ar' ? 'ar-AE' : 'en-GB' });
    await context.addInitScript((lang) => localStorage.setItem('strategy.language', lang), language);
    const page = await context.newPage();
    for (const [name, path] of [
      ['login', '/login'],
      ['forgot-password', '/forgot-password'],
      ['wasel-login', '/wasel/login'],
    ] as const) {
      await page.goto(path);
      await expectAccessible(page, name);
    }
    await context.close();
  });

  test('main screens', async ({ page }) => {
    test.setTimeout(900_000);
    for (const screen of screens) {
      await page.goto(screen.path);
      await expectAccessible(page, screen.name);
    }
  });

  test('settings and administration', async ({ page }) => {
    test.setTimeout(900_000);
    for (const screen of settingsScreens) {
      await page.goto(screen.path);
      await expectAccessible(page, screen.name);
    }
  });

  test('record pages with every tab', async ({ page, language }) => {
    test.setTimeout(1_800_000);
    const missing: string[] = [];
    for (const record of records) {
      const href = await firstRecordHref(page, record.list, record.href);
      if (!href) {
        // Optional: the main suite does not always leave one (no open request in the queue, no audit plan).
        if (record.optional)
          test.info().annotations.push({ type: 'skipped record', description: record.name });
        else missing.push(record.name);
        continue;
      }
      await auditRecord(page, record.name, href);
    }
    // An agile project (board, backlog, sprints) when the first project is not one.
    for (let i = 0; i < 15; i++) {
      const href = await firstRecordHref(page, '/portfolio/projects', projectHref, i);
      if (!href) break;
      await page.goto(`${href}?tab=board`);
      await settle(page);
      const selected = page.getByRole('tab', {
        selected: true,
        name: tr(language, 'portfolio:project.tabs.board'),
      });
      if (await selected.count()) {
        await auditRecord(page, 'portfolio-agile-project', `${href}?tab=board`);
        break;
      }
    }
    expect(missing, 'lists without a record (run the other specs on this database first)').toEqual([]);
  });

  test('dark theme subset', async ({ page, language }) => {
    test.setTimeout(600_000);
    await apiPut(page, '/api/v1/identity/me/preferences', { language, theme: 'dark' });
    await page.addInitScript(() => localStorage.setItem('strategy.theme', 'dark'));
    try {
      for (const path of [
        '/',
        '/dashboards/executive',
        '/portfolio/projects',
        '/kpi/scorecard',
        '/risk/risks',
        '/tasks',
        '/settings/health',
        '/settings/branding',
      ]) {
        await page.goto(path);
        await expect(page.locator('html')).toHaveClass(/dark/u);
        await expectAccessible(page, `dark${path === '/' ? '-home' : path.replace(/\//gu, '-')}`);
      }
      const project = await firstRecordHref(page, '/portfolio/projects', projectHref);
      if (project) {
        await page.goto(project);
        await expectAccessible(page, 'dark-portfolio-project');
      }
    } finally {
      await apiPut(page, '/api/v1/identity/me/preferences', { language, theme: 'light' });
    }
  });
});

test.describe('accessibility — keyboard, focus and reflow', () => {
  test.use({ signedInAs: 'mariam' });

  test('keyboard: skip link, visible focus on every stop of the shell, no focus trap', async ({ page }) => {
    await page.goto('/');
    await settle(page);
    const seen = new Set<string>();
    for (let i = 0; i < 25; i++) {
      await page.keyboard.press('Tab');
      const focus = await page.evaluate(() => {
        const el = document.activeElement as HTMLElement | null;
        if (!el || el === document.body) return null;
        const style = getComputedStyle(el);
        const rect = el.getBoundingClientRect();
        return {
          key: `${el.tagName}:${el.getAttribute('href') ?? ''}:${el.textContent.trim().slice(0, 30)}`,
          visibleOutline:
            (style.outlineStyle !== 'none' && parseFloat(style.outlineWidth) > 0) ||
            style.boxShadow !== 'none',
          onScreen: rect.width > 0 && rect.height > 0,
        };
      });
      if (!focus) throw new Error(`Tab ${i + 1} did not reach an element`);
      expect(focus.visibleOutline, `focus indicator on ${focus.key}`).toBe(true);
      expect(focus.onScreen, `${focus.key} is on screen`).toBe(true);
      seen.add(focus.key);
    }
    expect(seen.size, 'Tab moves through different elements').toBeGreaterThan(10);
  });

  test('keyboard: a dialog keeps focus inside, Escape closes it and focus returns to the trigger', async ({
    page,
    language,
  }) => {
    await page.goto('/sample');
    await settle(page);
    const trigger = page.getByRole('button', { name: language === 'ar' ? 'بند جديد' : 'New item' });
    await trigger.focus();
    await page.keyboard.press('Enter');
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await expectAccessible(page, 'dialog-sample-new-item');
    for (let i = 0; i < 15; i++) {
      await page.keyboard.press('Tab');
      expect(await dialog.evaluate((d) => d.contains(document.activeElement))).toBe(true);
    }
    await page.keyboard.press('Escape');
    await expect(dialog).toBeHidden();
    await expect(trigger).toBeFocused();
  });

  test('keyboard: the request wizard is reached, filled and moved through without a mouse', async ({
    page,
    language,
  }) => {
    await page.goto('/portfolio/requests/new');
    await settle(page);
    // Tab to "Fill manually" and choose it with Enter.
    const manual = page.getByRole('button', {
      name: new RegExp(tr(language, 'portfolio:start.manual'), 'u'),
    });
    let reached = false;
    for (let i = 0; i < 60 && !reached; i++) {
      await page.keyboard.press('Tab');
      reached = await manual.evaluate((el) => el === document.activeElement).catch(() => false);
    }
    expect(reached, 'Tab reaches "Fill manually"').toBe(true);
    await page.keyboard.press('Enter');
    const stepper = page.getByRole('navigation', { name: tr(language, 'stepper.label') });
    await expect(stepper).toBeVisible();
    const current = async () => (await stepper.locator('[aria-current="step"]').textContent()) ?? '';
    const first = await current();
    await expectAccessible(page, 'portfolio-request-wizard');

    // Tab through the step to Next (every stop visible), then Enter: the wizard moves on or announces what is missing.
    const next = page.getByRole('button', { name: tr(language, 'actions.next') });
    reached = false;
    for (let i = 0; i < 80 && !reached; i++) {
      await page.keyboard.press('Tab');
      reached = await next.evaluate((el) => el === document.activeElement).catch(() => false);
    }
    expect(reached, 'Tab reaches Next').toBe(true);
    await page.keyboard.press('Enter');
    await expect
      .poll(async () => (await current()) !== first || (await page.getByRole('alert').count()) > 0)
      .toBe(true);
  });

  test('reflow: no horizontal scrolling at 360 px and at 200 % zoom', async ({ page }) => {
    test.setTimeout(600_000);
    const paths = [
      '/',
      '/portfolio/projects',
      '/kpi/cards',
      '/risk/risks',
      '/tasks',
      '/cap/remarks',
      '/settings/health',
      '/dashboards/executive',
    ];
    for (const viewport of [
      { width: 360, height: 780 },
      { width: 683, height: 430 }, // 1366 × 860 at 200 % zoom
    ]) {
      await page.setViewportSize(viewport);
      for (const path of paths) {
        await page.goto(path);
        await settle(page);
        const overflow = await page.evaluate(
          () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
        );
        expect(overflow, `${path} at ${viewport.width}px overflows by ${overflow}px`).toBeLessThanOrEqual(1);
      }
    }
  });
});
