import type { APIRequestContext, Page } from '@playwright/test';
import { apiPut, expect, recordSection, test, tr, type Language } from './fixtures';

/**
 * Phase 20 — Themes and application workspaces (SPEC-ADDENDUM §C, docs/design/themes.md). The administrator lets users choose their
 * theme; Mariam (every office role) then walks the main screens in Classic, Enterprise and Modern, light and dark: screenshot at desktop
 * width and no horizontal page scroll at 360 px. Then: the launcher shows only the systems a user can open, and the landing app follows
 * the role rule unless the user chose their own. Everything is put back as it was (Classic default, choosing off) at the end.
 * Screenshots: docs/screenshots/phase-20 (E2E_SCREENSHOTS=phase-20).
 */

test.use({ signedInAs: 'mariam' });
test.describe.configure({ mode: 'serial' });

type Theme = 'classic' | 'enterprise' | 'modern';
type Mode = 'light' | 'dark';
const themes: Theme[] = ['classic', 'enterprise', 'modern'];
const modes: Mode[] = ['light', 'dark'];

interface AppearanceDocument {
  value: Record<string, unknown> | null;
  version: number;
}

async function appearance(api: APIRequestContext): Promise<AppearanceDocument> {
  const response = await api.get('/api/v1/settings/appearance');
  expect(response.ok()).toBe(true);
  return (await response.json()) as AppearanceDocument;
}

async function saveAppearance(api: APIRequestContext, value: Record<string, unknown>) {
  const current = await appearance(api);
  const response = await apiPut(api, '/api/v1/settings/appearance', { value, version: current.version });
  expect(response.status(), await response.text()).toBe(200);
}

async function choose(requester: APIRequestContext | Page, body: Record<string, unknown>) {
  const response = await apiPut(requester, '/api/v1/identity/me/preferences', body);
  expect(response.status(), await response.text()).toBe(204);
}

/** Waits for data, fonts and the theme's motion to settle. */
async function settle(page: Page) {
  await page.waitForLoadState('networkidle').catch(() => undefined);
  await page
    .locator('[aria-busy="true"]')
    .first()
    .waitFor({ state: 'detached', timeout: 10_000 })
    .catch(() => undefined);
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(250);
}

async function expectNoHorizontalScroll(page: Page, screen: string) {
  const overflow = await page.evaluate(() => {
    const root = document.documentElement;
    return root.scrollWidth - root.clientWidth;
  });
  expect(overflow, `${screen}: page wider than the 360 px viewport by ${overflow} px`).toBeLessThanOrEqual(0);
}

interface Screen {
  name: string;
  open: (page: Page, language: Language, theme: Theme) => Promise<void>;
}

let projectId: string | undefined;
let original: Record<string, unknown> | null = null;

test.beforeAll(async ({ adminApi }) => {
  original = (await appearance(adminApi)).value;
  await saveAppearance(adminApi, { ...(original ?? {}), allowUserTheme: true, defaultTheme: 'classic' });
  const projects = (await (await adminApi.get('/api/v1/portfolio/projects?page=1&pageSize=1')).json()) as {
    items: { id: string }[];
  };
  projectId = projects.items[0]?.id;
});

test.afterAll(async ({ adminApi }) => {
  await choose(adminApi, { uiTheme: '', theme: 'light', landingApp: '', favouriteApps: [] });
  await saveAppearance(adminApi, original ?? {});
});

const screens: Screen[] = [
  {
    name: 'launcher',
    open: async (page, language, theme) => {
      await page.goto('/my-work');
      if (theme !== 'classic') {
        await page.getByTestId('app-launcher').click();
        await expect(page.getByTestId('app-launcher-dialog')).toBeVisible();
      } else {
        // Classic: the single sidebar is the launcher.
        await expect(page.getByRole('navigation', { name: tr(language, 'shell:navigation') })).toBeVisible();
      }
    },
  },
  { name: 'app-home', open: (page) => page.goto('/apps/portfolio').then(() => undefined) },
  {
    name: 'list',
    open: async (page) => {
      await page.goto('/portfolio/projects');
      const preview = page.getByTestId('project-preview').first();
      if (await preview.isVisible().catch(() => false)) await preview.click();
    },
  },
  {
    name: 'record',
    open: async (page) => {
      await page.goto(projectId ? `/portfolio/projects/${projectId}` : '/portfolio/projects');
    },
  },
  { name: 'wizard', open: (page) => page.goto('/portfolio/requests/new').then(() => undefined) },
  { name: 'my-work', open: (page) => page.goto('/my-work').then(() => undefined) },
  { name: 'executive', open: (page) => page.goto('/dashboards/executive').then(() => undefined) },
  { name: 'appearance', open: (page) => page.goto('/settings/appearance').then(() => undefined) },
];

for (const theme of themes) {
  for (const mode of modes) {
    test(`${theme} ${mode}: main screens render, at 360 px without horizontal scroll`, async ({
      page,
      language,
      shot,
    }) => {
      test.setTimeout(240_000);
      await choose(page, { uiTheme: theme, theme: mode, language });
      for (const screen of screens) {
        await page.setViewportSize({ width: 1366, height: 860 });
        await screen.open(page, language, theme);
        await settle(page);
        await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
        await expect(page.locator('html')).toHaveClass(mode === 'dark' ? /dark/ : /^(?!.*dark).*$/);
        await expect(page.locator('html')).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr');
        if (theme !== 'classic') await expect(page.getByTestId('workspace-shell')).toBeVisible();
        await shot(`${theme}-${mode}-${screen.name}`);

        await page.keyboard.press('Escape');
        await page.setViewportSize({ width: 360, height: 760 });
        await settle(page);
        await expectNoHorizontalScroll(page, `${theme} ${mode} ${screen.name}`);
      }
    });
  }
}

test('Enterprise: highlights panel, path bar, related lists and per-app tabs; deep links keep working', async ({
  page,
  language,
  shot,
}) => {
  test.skip(!projectId, 'needs a project (run after the portfolio specs or with the demo pack)');
  await choose(page, { uiTheme: 'enterprise', theme: 'light' });
  await page.goto(`/portfolio/projects/${projectId}?tab=risks`);
  await expect(page.getByTestId('record-highlights')).toBeVisible();
  await expect(page.getByTestId('path-guidance')).toBeVisible();
  // The deep link still opens the Risks tab.
  await expect(recordSection(page, language, tr(language, 'portfolio:project.tabs.risks'))).toHaveAttribute(
    'aria-current',
    'page',
  );
  // App tabs of Portfolio only, Projects active.
  const tabs = page.getByTestId('app-tabs');
  await expect(tabs.getByTestId('app-tab-projects')).toHaveAttribute('aria-current', 'page');
  await expect(tabs.getByTestId('app-tab-cards')).toHaveCount(0);
  await recordSection(page, language, tr(language, 'portfolio:project.tabs.milestones')).click();
  await expect(page).toHaveURL(/tab=milestones/);
  await expect(page.getByTestId('utility-bar')).toBeVisible();
  await shot('enterprise-light-record-milestones');
});

test('Modern: command palette (Ctrl+K) finds pages, actions and records', async ({
  page,
  language,
  shot,
}) => {
  await choose(page, { uiTheme: 'modern', theme: 'light' });
  await page.goto('/my-work');
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'modern');
  await expect(page.getByTestId('command-palette-open')).toBeVisible();
  await page.keyboard.press('Control+k');
  const palette = page.getByTestId('command-palette');
  await expect(palette).toBeVisible();
  const input = palette.getByRole('combobox');
  await expect(input).toBeFocused();
  // Empty: pages (apps) are offered.
  await expect(
    palette.getByRole('option', { name: tr(language, 'appearance:apps.kpi.name') }).first(),
  ).toBeVisible();
  await input.fill(tr(language, 'appearance:tabs.portfolio.projects'));
  await expect(palette.getByRole('option').first()).toBeVisible();
  await shot('modern-light-palette');
  await page.keyboard.press('Enter');
  await expect(palette).toBeHidden();
  await expect(page).toHaveURL(/\/portfolio\/projects/);

  // A record by its number, through the Phase 19 search.
  if (projectId) {
    const project = (await (await page.request.get(`/api/v1/portfolio/projects/${projectId}`)).json()) as {
      number: string;
    };
    await page.keyboard.press('Control+k');
    await page.getByTestId('command-palette').getByRole('combobox').fill(project.number);
    await expect(
      page
        .getByTestId('command-palette')
        .getByRole('option', { name: new RegExp(project.number) })
        .first(),
    ).toBeVisible();
    await page.keyboard.press('Escape');
  }
});

/** As the regular demo user (own session; the administrator's shared session stays untouched). */
test.describe('as a regular user', () => {
  test.use({ signedInAs: 'ahmed' });

  test('app launcher shows only the systems the user can open; favourites come first', async ({
    page,
    adminApi,
    language,
    shot,
  }) => {
    await choose(page, { uiTheme: 'enterprise', theme: 'light', language, favouriteApps: [] });
    const access = (await (await page.request.get('/api/v1/access/me')).json()) as { systems: string[] };
    await page.goto('/my-work');
    await page.getByTestId('app-launcher').click();
    const dialog = page.getByTestId('app-launcher-dialog');
    await expect(dialog).toBeVisible();
    const systemApps = [
      'portfolio',
      'kpi',
      'risk',
      'requests',
      'docreg',
      'ip',
      'bench',
      'cap',
      'auditmgmt',
      'sample',
    ];
    for (const app of systemApps) {
      const tile = dialog.getByTestId(`app-tile-${app}`);
      if (!access.systems.includes(app)) await expect(tile, app).toHaveCount(0);
    }
    // Every system tile shown is a system the user can open.
    for (const testId of await dialog
      .locator('[data-testid^="app-tile-"]')
      .evaluateAll((tiles) => tiles.map((tile) => tile.getAttribute('data-testid') ?? ''))) {
      const app = testId.replace('app-tile-', '');
      if (systemApps.includes(app)) expect(access.systems, app).toContain(app);
    }
    // Administration is not offered to a regular user.
    await expect(dialog.getByTestId('app-tile-settings')).toHaveCount(0);
    await dialog.getByTestId('app-favourite-mywork').click();
    // Pinned: shown first, in Favourites (and still in its own group).
    await expect(dialog.getByTestId('app-tile-mywork')).toHaveCount(2);
    await expect(
      dialog.getByRole('heading', { name: tr(language, 'appearance:launcher.favourites') }),
    ).toBeVisible();
    await shot('enterprise-light-launcher-user');
    await page.keyboard.press('Escape');

    // The administrator (Settings permissions) does get the Settings app.
    const adminAccess = (await (await adminApi.get('/api/v1/access/me')).json()) as { permissions: string[] };
    expect(adminAccess.permissions).toContain('settings.general.manage');
    await choose(page, { favouriteApps: [] });
  });

  test('landing app: My Work when no rule matches, the role rule applies in workspace themes, the user’s own default wins, Classic stays on My Work', async ({
    page,
    adminApi,
  }) => {
    const access = (await (await page.request.get('/api/v1/access/me')).json()) as { roles: string[] };
    const role = access.roles[0];
    test.skip(!role, 'the demo user holds no role');
    const current = (await appearance(adminApi)).value ?? {};
    try {
      await choose(page, { uiTheme: 'enterprise', theme: 'light', landingApp: '' });

      // No rule matches any of the user's roles (none configured, or only for a role they do not hold): My Work at "/".
      await saveAppearance(adminApi, { ...current, landingRules: [] });
      await page.goto('/');
      await expect(page.getByTestId('workspace-shell')).toBeVisible();
      await expect(page).toHaveURL(/\/$/);
      await expect(page.getByTestId('workspace-app')).toHaveAttribute('href', '/my-work');
      await saveAppearance(adminApi, {
        ...current,
        landingRules: [{ roleCode: 'no-such.role', app: 'sample' }],
      });
      await page.goto('/');
      await expect(page.getByTestId('workspace-shell')).toBeVisible();
      await expect(page).toHaveURL(/\/$/);

      // A rule for one of the user's roles: they land on that app.
      await saveAppearance(adminApi, { ...current, landingRules: [{ roleCode: role, app: 'sample' }] });
      await page.goto('/');
      await expect(page).toHaveURL(/\/sample(\?|$)/);

      // Own default app first.
      await choose(page, { landingApp: 'mywork' });
      await page.goto('/');
      await expect(page).toHaveURL(/\/$/);

      // Classic keeps My Work at "/" whatever the rule says.
      await choose(page, { uiTheme: 'classic', landingApp: '' });
      await page.goto('/');
      await expect(page).toHaveURL(/\/$/);
    } finally {
      await choose(page, { uiTheme: '', landingApp: '' });
      await saveAppearance(adminApi, { ...current, landingRules: current.landingRules ?? [] });
    }
  });

  test('the theme menu offers themes only while choosing is allowed', async ({
    page,
    adminApi,
    language,
  }) => {
    await choose(page, { uiTheme: '', theme: 'light' });
    await page.goto('/my-work');
    await page.getByTestId('theme-menu').click();
    await expect(page.getByTestId('theme-option-modern')).toBeVisible();
    await page.getByTestId('theme-option-modern').click();
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'modern');

    const current = (await appearance(adminApi)).value ?? {};
    await saveAppearance(adminApi, { ...current, allowUserTheme: false });
    try {
      await page.reload();
      // Choosing off: the organisation default applies again and the menu has no theme choice.
      await expect(page.locator('html')).toHaveAttribute('data-theme', 'classic');
      await page.getByTestId('theme-menu').click();
      await expect(page.getByTestId('theme-option-modern')).toHaveCount(0);
      await expect(page.getByRole('menuitemradio', { name: tr(language, 'shell:theme.dark') })).toBeVisible();
    } finally {
      await saveAppearance(adminApi, { ...current, allowUserTheme: true });
      await choose(page, { uiTheme: '' });
    }
  });
});
