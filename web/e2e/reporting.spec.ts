import type { APIRequestContext, Page } from '@playwright/test';
import { apiPost, apiPut, expect, mailpitUrl, screenshotPath, test, tr, useMailpit, type Language } from './fixtures';

/**
 * Phase 17 — Dashboards and reporting (docs/design/reporting.md). Mariam (demo Director General: executive viewer, reporting administrator,
 * report author) sees My Work cards from every system, the executive dashboard with the department scorecard and the AI brief, drills down to a
 * system dashboard, builds and publishes a role dashboard, builds a report and schedules it by e-mail. Run after the other specs so every
 * system has data. Screenshots: docs/screenshots/phase-17 (E2E_SCREENSHOTS=phase-17).
 */

test.use({ signedInAs: 'mariam' });

const systemDashboards = [
  'portfolio.overview',
  'kpi.scorecard',
  'risk.profile',
  'teams.overview',
  'tasks.performance',
  'docreg.library',
  'ip.register',
  'bench.plan',
  'cap.closure',
  'auditmgmt.closure',
  'requests.overview',
];

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

/** AI on with the offline demo provider (drafts only). */
async function ensureDemoAi(admin: APIRequestContext) {
  const current = await json<{ settings: { enabled: boolean; defaultProviderId: string | null } & Record<string, unknown>; version: number }>(
    await admin.get('/api/v1/ai/settings'),
  );
  if (current.settings.enabled && current.settings.defaultProviderId) return;
  const provider = await json<{ id: string }>(
    await apiPost(admin, '/api/v1/ai/providers', { name: `Demo ${Date.now()}`, type: 'demo', defaultModel: 'demo-large' }),
  );
  await json(
    await apiPut(admin, '/api/v1/ai/settings', {
      settings: { ...current.settings, enabled: true, defaultProviderId: provider.id },
      version: current.version,
    }),
  );
}

/** Waits until the refresh job has written the aggregates (it runs every minute; a manual request marks every system dirty). */
async function waitForAggregates(admin: APIRequestContext) {
  await apiPost(admin, '/api/v1/reporting/refresh');
  await expect
    .poll(
      async () => {
        const status = await json<{ systemCode: string; at: string | null }[]>(await admin.get('/api/v1/reporting/refresh'));
        return status.filter((s) => s.at).length;
      },
      { timeout: 150_000, intervals: [5_000] },
    )
    .toBeGreaterThan(5);
}

async function settle(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => document.fonts.ready);
}

test.describe('dashboards and reporting', () => {
  test('My Work cards, executive dashboard, scorecard drill-down, system dashboards, builder, report, schedule, AI brief', async ({
    page,
    language,
    adminApi,
    baseURL,
    shot,
  }) => {
    test.setTimeout(900_000);
    const r = (key: string, params: Record<string, string | number> = {}) => tr(language, `reporting:${key}`, params);
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    await ensureDemoAi(adminApi);
    await waitForAggregates(adminApi);

    // ---- Level 1: My Work with a card per system, then customised -------------------------------------------------
    await page.goto('/');
    await expect(page.getByTestId('my-work-cards')).toBeVisible();
    await expect(page.getByTestId('my-work-card-workflow.approvals').or(page.getByTestId('my-work-card-notifications.latest')).first()).toBeVisible();
    await settle(page);
    await shot('my-work', { fullPage: true });
    await page.getByTestId('my-work-customise').click();
    await expect(page.getByText(r('myWork.customiseHint'))).toBeVisible();
    await shot('my-work-customise');
    await page.getByTestId('my-work-customise').click();

    // ---- Level 4: executive dashboard, department scorecard, AI brief ----------------------------------------------
    await page.goto('/dashboards/executive?kind=quarter');
    await expect(page.getByTestId('dashboard-executive')).toBeVisible();
    await expect(page.getByTestId('widget-org-score')).toBeVisible();
    await settle(page);
    await shot('executive-dashboard', { fullPage: true });
    const scorecard = page.getByTestId('department-scorecard');
    await expect(scorecard).toBeVisible();
    await scorecard.scrollIntoViewIfNeeded();
    await scorecard.screenshot({ path: screenshotPath(language, 'department-scorecard') });

    await page.getByTestId('brief-generate').click();
    await expect(page.getByTestId('brief-attention')).toBeVisible({ timeout: 60_000 });
    await expect(page.getByText(r('brief.aiDraft'))).toBeVisible();
    await page.getByTestId('ai-brief').scrollIntoViewIfNeeded();
    await page.getByTestId('ai-brief').screenshot({ path: screenshotPath(language, 'ai-executive-brief') });

    // Drill-down: a department cell opens that system's dashboard filtered to the department.
    const cell = scorecard.locator('[data-testid^="rag-cell"], td button, [role="gridcell"] button').first();
    if (await cell.count()) {
      await cell.click();
    } else {
      await page.goto('/dashboards/kpi.scorecard?kind=quarter');
    }
    await expect(page.locator('[data-testid^="dashboard-"]').first()).toBeVisible();
    await settle(page);
    await shot('drill-down-system-dashboard', { fullPage: true });
    // ... and a list item opens its record.
    const recordLink = page.getByTestId('widget-list-link').first();
    if (await recordLink.count()) {
      const href = await recordLink.getAttribute('href');
      await recordLink.click();
      await expect(page).toHaveURL(new RegExp((href ?? '/').replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      await settle(page);
      await shot('drill-down-record');
    }

    // ---- Level 3: one dashboard per system --------------------------------------------------------------------
    for (const code of systemDashboards) {
      await page.goto(`/dashboards/${code}?kind=year`);
      await expect(page.getByTestId(`dashboard-${code}`)).toBeVisible();
      await settle(page);
      await shot(`system-${code.replace('.', '-')}`, { fullPage: true });
    }

    // ---- Dashboard builder: a role dashboard published without code ---------------------------------------------
    await page.goto('/settings/dashboards');
    await page.getByTestId('builder-new').click();
    await page.getByTestId('builder-title-ar').fill(`لوحة الإدارة ${tag}`);
    await page.getByTestId('builder-title-en').fill(`Department dashboard ${tag}`);
    await page.getByTestId('builder-add-widget').click();
    await page.getByTestId('builder-add-widget').click();
    await expect(page.getByTestId('builder-widget')).toHaveCount(2);
    await shot('dashboard-builder', { fullPage: true });
    await page.getByTestId('builder-publish').click();
    await expect(page.getByText(r('builder2.published'))).toBeVisible();

    // ---- Report builder: object → columns → run → save → schedule ----------------------------------------------
    await page.goto('/reports/new');
    await expect(page.getByTestId('report-builder')).toBeVisible();
    await page.getByRole('combobox', { name: r('builder.object') }).click();
    await page.getByRole('option').first().click();
    const columns = page.getByTestId('report-columns').getByRole('checkbox');
    await columns.nth(0).check();
    await columns.nth(1).check();
    await columns.nth(2).check();
    await page.getByTestId('report-run').click();
    await expect(page.getByTestId('report-preview')).toBeVisible();
    await page.getByTestId('report-name').fill(`Report ${tag}`);
    await page.getByTestId('report-save').click();
    await expect(page).toHaveURL(/\/reports\/[0-9a-f-]{36}/);
    await settle(page);
    await shot('report-builder', { fullPage: true });

    // Scheduled e-mail: daily, one minute from now (installation time zone), sent to Mariam in her language.
    await useMailpit(adminApi, baseURL ?? '');
    const reportId = /\/reports\/([0-9a-f-]{36})/.exec(page.url())?.[1] ?? '';
    const me = await json<{ id: string }>(await adminApi.get('/api/v1/identity/me'));
    const regional = await json<{ value: { timeZone: string } | null }>(await adminApi.get('/api/v1/settings/regional'));
    const zone = regional.value?.timeZone ?? 'Asia/Dubai';
    const at = new Date(Date.now() + 70_000);
    const time = new Intl.DateTimeFormat('en-GB', { timeZone: zone, hour: '2-digit', minute: '2-digit', hour12: false }).format(at);
    await page.getByTestId('schedule-open').click();
    await expect(page.getByTestId('schedule-dialog')).toBeVisible();
    await shot('schedule-dialog');
    await page.keyboard.press('Escape');
    const schedule = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/reporting/schedules', {
        name: `Daily ${tag}`,
        targetKind: 'report',
        targetId: reportId,
        frequency: 'daily',
        time,
        day: 1,
        periodRule: 'current-month',
        format: 'xlsx',
        recipients: [{ kind: 'user', ref: me.id }],
        active: true,
      }),
    );
    expect(schedule.id).toBeTruthy();
    const since = new Date();
    await page.waitForTimeout(Math.max(0, at.getTime() - Date.now() + 5_000));
    await apiPost(adminApi, '/api/v1/scheduling/jobs/reporting.deliver/run');
    let messageId = '';
    await expect
      .poll(
        async () => {
          const response = await page.request.get(`${mailpitUrl}/api/v1/search`, { params: { query: `subject:"${tag}"`, limit: '5' } });
          const { messages } = (await response.json()) as { messages: { ID: string; Created: string }[] };
          messageId = messages.find((m) => new Date(m.Created) >= since)?.ID ?? '';
          return messageId;
        },
        { timeout: 120_000, intervals: [3_000] },
      )
      .not.toBe('');
    const mail = await page.context().newPage();
    await mail.goto(`${mailpitUrl}/view/${messageId}`);
    await mail.waitForLoadState('networkidle');
    await mail.screenshot({ path: screenshotPath(language as Language, 'scheduled-report-email'), fullPage: true });
    await mail.close();
    await page.goto('/reports/schedules');
    await expect(page.getByTestId('schedules-table')).toBeVisible();
    await shot('schedules');
  });
});
