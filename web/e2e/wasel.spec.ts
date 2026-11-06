import { expectAccessible } from './a11y';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import type { APIRequestContext, Browser, Page } from '@playwright/test';
import {
  apiPost,
  apiPut,
  expect,
  latestEmail,
  nextTotp,
  screenshotPath,
  signInViaApi,
  test,
  useMailpit,
  type Language,
} from './fixtures';

/**
 * Phase 11 — Wasel vendor portal and Portfolio completion (docs/design/portfolio.md §46–§62), in Arabic (RTL) and English (LTR).
 * The PM invites a vendor; the vendor sets a password and signs in at /wasel with mandatory MFA, sees only the assigned project, proposes the plan
 * and submits the monthly update; the PM reviews side by side (edit-then-accept / return) and the vendor sees the outcome; vendor rating,
 * capacity view, quarterly portfolio review and the Gantt export in Arabic. Screenshots: docs/screenshots/phase-11 (E2E_SCREENSHOTS=phase-11).
 */

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

const year = new Date().getFullYear();
const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');
const base = (id: string) => `/api/v1/portfolio/projects/${id}`;
const wasel = '/api/wasel/v1';
const vendorPassword = 'Vendor#Portal2026';

async function waselHeaders(api: APIRequestContext) {
  const token = (await (await api.get(`${wasel}/antiforgery/token`)).json()) as {
    token: string;
    headerName: string;
  };
  return { [token.headerName]: token.token, 'X-Client': 'spa' };
}

const waselPost = async (api: APIRequestContext, url: string, data: unknown = {}) =>
  api.post(url, { data, headers: await waselHeaders(api) });

const waselPut = async (api: APIRequestContext, url: string, data: unknown) =>
  api.put(url, { data, headers: await waselHeaders(api) });

async function antiforgery(api: APIRequestContext) {
  const token = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [token.headerName]: token.token, 'X-Client': 'spa' };
}

async function approve(admin: APIRequestContext, subject: string, id: string, times: number) {
  for (let i = 0; i < times; i++) {
    const panel = await json<{ instances: { myTask: { taskId: string } | null }[] }>(
      await admin.get(`/api/v1/workflow/subjects/${subject}/${id}`),
    );
    const task = panel.instances.map((x) => x.myTask).find((t) => t !== null);
    if (!task) throw new Error(`No approval task on ${subject} ${id}`);
    await json(
      await apiPost(admin, `/api/v1/workflow/tasks/${task.taskId}/act`, { action: 'approve', comment: null }),
    );
  }
}

/** An initiative in planning with Ahmed as PM (no charter: planning directly, Q-P2). */
async function initiative(ahmed: APIRequestContext, admin: APIRequestContext, name: string) {
  const values = {
    nameAr: `مبادرة ${name}`,
    nameEn: name,
    description: 'Vendor-delivered online services.',
    justifications: 'Applicants wait too long today.',
    strategicObjectiveCode: 'digital-government',
    suggestionSourceCode: 'idea',
    programTypeCode: 'digital',
    requiresBudget: true,
    estimatedBudget: 40_000,
    year1Cost: 28_000,
    year2Cost: 4_000,
    year3Cost: 4_000,
    year4Cost: 4_000,
    costPath: 'cost-estimation',
    expectedStartDate: `${year + 1}-01-01`,
    expectedEndDate: `${year + 1}-12-31`,
    programNature: 'new',
    scopeOfWork: 'Online forms, payments and tracking delivered by a vendor.',
    screenCoreWork: true,
    screenOneTime: true,
    screenTimeBound: true,
    screenUniqueOutput: true,
    screenMinCost: false,
    screenOperationalCosts: true,
    priorityScores: { 'vision-alignment': 5, impact: 4, complexity: 3, 'radical-change': 4 },
    mainOptionCode: 'contracting',
    practiceService: 'Permits service',
  };
  const request = await json<{ id: string }>(await apiPost(ahmed, '/api/v1/portfolio/requests', { values }));
  const upload = await ahmed.post(`/api/v1/portfolio/requests/${request.id}/files/cost-estimation`, {
    multipart: { file: { name: 'cost.pdf', mimeType: 'application/pdf', buffer: pdf } },
    headers: await antiforgery(ahmed),
  });
  expect(upload.ok(), await upload.text()).toBe(true);
  const draft = await json<{ version: number }>(await ahmed.get(`/api/v1/portfolio/requests/${request.id}`));
  await json(
    await apiPost(ahmed, `/api/v1/portfolio/requests/${request.id}/submit`, { version: draft.version }),
  );
  await approve(admin, 'portfolio.request', request.id, 3);
  const approved = await json<{ project: { id: string } | null }>(
    await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
  );
  const id = approved.project?.id ?? '';
  expect(id).not.toBe('');
  const p = await json<{ version: number }>(await ahmed.get(base(id)));
  await json(await apiPost(ahmed, `${base(id)}/take-ownership`, { version: p.version }));
  await json(
    await apiPost(ahmed, `${base(id)}/planning-items`, {
      values: { category: 'in-scope', description: 'Online services' },
    }),
  );
  return id;
}

/** A vendor browser session: invitation link → password (UI) → sign-in (UI) → MFA enrolment (screenshot, then confirmed). */
async function vendorSignsIn(
  browser: Browser,
  baseURL: string,
  language: Language,
  email: string,
  link: URL,
) {
  const context = await browser.newContext({ baseURL, locale: language === 'ar' ? 'ar-AE' : 'en-GB' });
  await context.addInitScript((lang) => {
    if (!sessionStorage.getItem('e2e.initialised')) {
      localStorage.setItem('strategy.language', lang);
      localStorage.setItem('strategy.theme', 'light');
      sessionStorage.setItem('e2e.initialised', '1');
    }
  }, language);
  const page = await context.newPage();
  await page.setViewportSize({ width: 1366, height: 860 });
  await page.goto(`${link.pathname}${link.search}`);
  await page.getByTestId('wasel-new-password').fill(vendorPassword);
  await page.getByTestId('wasel-confirm-password').fill(vendorPassword);
  await page.getByTestId('wasel-set-password').click();
  // The page opens the sign-in page only once the password is saved: wait for that redirect instead of navigating at once.
  await page.waitForURL(/\/wasel\/login/);
  await page.getByTestId('wasel-login-email').fill(email);
  await page.getByTestId('wasel-login-password').fill(vendorPassword);
  await page.getByTestId('wasel-login-submit').click();
  await expect(page.getByTestId('wasel-mfa-enrol')).toBeVisible();
  await expectAccessible(page, 'wasel-login-mfa');
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: screenshotPath(language, 'wasel-login-mfa') });

  // The enrolment is confirmed through the same session (the authenticator key is read from the API, as an app would scan it).
  const setup = await json<{ sharedKey: string }>(
    await waselPost(page.request, `${wasel}/auth/mfa/enrol/totp`),
  );
  await json(
    await waselPost(page.request, `${wasel}/auth/mfa/enrol/confirm`, {
      method: 'totp',
      code: await nextTotp(setup.sharedKey),
    }),
  );
  await page.goto('/wasel');
  await expect(page.getByTestId('wasel-projects')).toBeVisible();
  return page;
}

async function vendorShot(page: Page, language: Language, name: string, fullPage = false) {
  // The vendor area is checked against WCAG 2.1 AA on every screen it photographs (docs/accessibility.md).
  await expectAccessible(page, `wasel-${name}`);
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: screenshotPath(language, name), fullPage });
}

test.describe('wasel — vendor portal and portfolio completion', () => {
  test('invitation, MFA, plan proposal, monthly submission, PM review with diff, rating, capacity, review, Gantt export', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    browser,
    baseURL,
  }) => {
    test.setTimeout(900_000);
    const tag = `${language}${Date.now().toString(36)}`;
    await useMailpit(adminApi, baseURL ?? '');
    const ahmed = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmed, 'ahmed');

    // 1. Vendor register (PM Office) and an initiative in planning.
    const vendor = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/portfolio/vendors', {
        values: {
          nameAr: `شركة النخبة ${tag}`,
          nameEn: `Elite Systems ${tag}`,
          tradeLicenceNo: `TL-${tag}`,
          email: 'info@elite.example',
          countryCode: 'AE',
        },
      }),
    );
    const id = await initiative(ahmed, adminApi, `Vendor portal ${tag}`);
    await json(
      await apiPost(ahmed, `${base(id)}/wasel/assignments`, {
        vendorId: vendor.id,
        accessUntil: `${year + 1}-12-31`,
        expiryRule: 'date',
      }),
    );

    // 2. The PM invites the Vendor Project Lead from the project's Vendors tab.
    const email = `lead.${tag}@elite.example`;
    await page.goto(`/portfolio/projects/${id}?tab=vendors`);
    await expect(page.getByTestId('wasel-panel')).toBeVisible();
    await page.getByTestId('wasel-invite').first().click();
    const dialog = page.getByTestId('wasel-invite-dialog');
    await expect(dialog).toBeVisible();
    await page.getByTestId('wasel-invite-email').fill(email);
    await shot('vendor-invitation');
    const since = new Date(Date.now() - 1000);
    await page.getByTestId('wasel-invite-send').click();
    await expect(page.getByTestId('wasel-member').first()).toBeVisible();

    // 3. The vendor accepts the invitation and signs in with mandatory MFA.
    const mail = await latestEmail(page, email, since);
    const link = new URL(/https?:\/\/\S*\/wasel\/set-password\?\S+/u.exec(mail.text)?.[0] ?? '');
    const vendorPage = await vendorSignsIn(browser, baseURL ?? '', language, email, link);
    await expect(vendorPage.getByTestId('wasel-project-card')).toHaveCount(1);
    await vendorPage.getByTestId('wasel-project-card').first().click();
    await expect(vendorPage.getByTestId('wasel-overview')).toBeVisible();
    await vendorShot(vendorPage, language, 'vendor-project-overview');

    // 4. Plan proposal: a phase and two milestones (60 / 40); the Lead submits.
    const v = vendorPage.request;
    const proposals = await json<{ proposals: { id: string; version: number }[] }>(
      await waselPost(v, `${wasel}/projects/${id}/proposals`, {
        note: 'Delivery plan',
        items: [
          {
            key: 'p1',
            type: 'phase',
            titleAr: 'التسليم',
            titleEn: 'Delivery',
            plannedStart: `${year + 1}-01-01`,
            plannedEnd: `${year + 1}-12-31`,
          },
          {
            key: 'm1',
            type: 'milestone',
            parentKey: 'p1',
            titleAr: 'التصميم',
            titleEn: 'Design',
            plannedStart: `${year + 1}-01-01`,
            plannedEnd: `${year + 1}-04-30`,
            weight: 0.6,
            tasks: ['Wireframes', 'Design review'],
          },
          {
            key: 'm2',
            type: 'milestone',
            parentKey: 'p1',
            titleAr: 'التنفيذ',
            titleEn: 'Build',
            plannedStart: `${year + 1}-05-01`,
            plannedEnd: `${year + 1}-12-31`,
            weight: 0.4,
          },
        ],
      }),
    );
    await vendorPage.getByTestId('wasel-tab-plan').click();
    await expect(vendorPage.getByTestId('wasel-plan')).toBeVisible();
    await vendorShot(vendorPage, language, 'plan-proposal', true);
    const proposal = proposals.proposals[0];
    await json(
      await waselPost(v, `${wasel}/projects/${id}/proposals/${proposal?.id}/submit`, {
        version: proposal?.version,
      }),
    );

    // 5. The PM accepts the plan with one edit; planning approval → execution, "Vendor then PM", window open.
    const panel = await json<{
      proposals: { id: string; version: number; items: { key: string; titleEn: string }[] }[];
    }>(await ahmed.get(`${base(id)}/wasel`));
    const pmProposal = panel.proposals[0];
    const items = (pmProposal?.items ?? []).map((i) =>
      i.key === 'm1' ? { ...i, titleEn: 'Design and sign-off' } : i,
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/proposals/${pmProposal?.id}/accept`, {
        items,
        version: pmProposal?.version,
      }),
    );
    let p = await json<{ version: number }>(await ahmed.get(base(id)));
    await json(await apiPost(ahmed, `${base(id)}/submit-planning`, { version: p.version }));
    await approve(adminApi, 'portfolio.project', id, 2);
    await expect
      .poll(async () => (await json<{ subState: string }>(await ahmed.get(base(id)))).subState, {
        timeout: 60_000,
      })
      .toBe('execution');
    p = await json<{ version: number }>(await ahmed.get(base(id)));
    await json(await apiPut(ahmed, `${base(id)}/wasel/mode`, { mode: 'vendor-then-pm', version: p.version }));
    await json(await apiPost(adminApi, `${base(id)}/monthly/open`));

    // 6. The vendor fills the monthly update (evidence for the complete milestone) and the Lead submits.
    const milestones = await json<{ id: string; titleEn: string }[]>(
      await v.get(`${wasel}/projects/${id}/milestones`),
    );
    const design = milestones.find((m) => m.titleEn === 'Design and sign-off')?.id ?? '';
    const build = milestones.find((m) => m.titleEn === 'Build')?.id ?? '';
    const today = new Date().toISOString().slice(0, 10);
    await json(
      await waselPut(v, `${wasel}/projects/${id}/submission/items/${design}`, {
        values: { achieve: 0.3, actualStart: today, progressNote: 'Half of the design screens are done' },
      }),
    );
    await json(
      await waselPut(v, `${wasel}/projects/${id}/submission/items/${build}`, {
        values: { achieve: 0.4, actualStart: today, actualEnd: today, progressNote: 'Build delivered' },
      }),
    );
    const evidence = await v.post(`${wasel}/projects/${id}/submission/items/${build}/evidence`, {
      multipart: { file: { name: 'handover.pdf', mimeType: 'application/pdf', buffer: pdf } },
      headers: await waselHeaders(v),
    });
    expect(evidence.ok(), await evidence.text()).toBe(true);
    await vendorPage.getByTestId('wasel-tab-monthly').click();
    await expect(vendorPage.getByTestId('wasel-monthly')).toBeVisible();
    await expect(vendorPage.getByTestId('wasel-item').first()).toBeVisible();
    await vendorShot(vendorPage, language, 'monthly-submission', true);
    const submission = await json<{ version: number }>(await v.get(`${wasel}/projects/${id}/submission`));
    await json(
      await waselPost(v, `${wasel}/projects/${id}/submission/submit`, { version: submission.version }),
    );

    // 7. PM review side by side; edit-then-accept Design (30 % → 25 %), return Build.
    await page.goto(`/portfolio/projects/${id}?tab=vendors`);
    await expect(page.getByTestId('wasel-submission').first()).toBeVisible();
    const pmPanel = await json<{
      submissions: { id: string; items: { id: string; milestoneId: string; version: number }[] }[];
    }>(await ahmed.get(`${base(id)}/wasel`));
    const sub = pmPanel.submissions[0];
    const designItem = sub?.items.find((i) => i.milestoneId === design);
    const buildItem = sub?.items.find((i) => i.milestoneId === build);
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/vendor-submissions/${sub?.id}/items/${designItem?.id}/accept`, {
        values: { achieve: 0.25, actualStart: today, progressNote: 'Half of the design screens are done' },
        version: designItem?.version,
      }),
    );
    await page.reload();
    await expect(page.getByTestId('wasel-review-item').first()).toBeVisible();
    await shot('pm-review-diff', { fullPage: true });
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/vendor-submissions/${sub?.id}/items/${buildItem?.id}/return`, {
        comment: 'Attach the signed acceptance certificate',
        version: buildItem?.version,
      }),
    );

    // 8. The vendor sees the PM's change and the returned item.
    await vendorPage.reload();
    await vendorPage.getByTestId('wasel-tab-monthly').click();
    await expect(vendorPage.getByTestId('wasel-value-diff').first()).toBeVisible();
    await vendorShot(vendorPage, language, 'vendor-sees-review', true);

    // 9. Vendor rating (per milestone at any time; overall at closure).
    await page.reload();
    await page.getByTestId('wasel-rate').first().click();
    await expect(page.getByTestId('wasel-rate-dialog')).toBeVisible();
    await shot('vendor-rating');
    await page.keyboard.press('Escape');

    // 10. Capacity view (PM Office) and a quarterly portfolio review whose hold decision starts a change request.
    const mariam = await browser.newContext({ baseURL, storageState: await adminApi.storageState() });
    await mariam.addInitScript((lang) => {
      localStorage.setItem('strategy.language', lang);
      localStorage.setItem('strategy.theme', 'light');
    }, language);
    const office = await mariam.newPage();
    await office.setViewportSize({ width: 1366, height: 860 });
    await office.goto('/portfolio/capacity');
    await expect(office.getByTestId('capacity-page')).toBeVisible();
    await office.evaluate(() => document.fonts.ready);
    await office.screenshot({ path: screenshotPath(language, 'capacity-view'), fullPage: true });

    const review = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/portfolio/reviews', {
        period: `${year}-Q4`,
        title: `Q4 review ${tag}`,
      }),
    );
    await json(
      await apiPut(adminApi, `/api/v1/portfolio/reviews/${review.id}/decisions/${id}`, {
        decision: 'hold',
        comment: 'Budget freeze',
      }),
    );
    await json(await apiPost(adminApi, `/api/v1/portfolio/reviews/${review.id}/complete`));
    await office.goto(`/portfolio/reviews/${review.id}`);
    await expect(office.getByTestId('review-page')).toHaveAttribute('data-status', 'completed');
    await expect(office.getByTestId('review-change-request').first()).toBeVisible();
    await office.evaluate(() => document.fonts.ready);
    await office.screenshot({ path: screenshotPath(language, 'portfolio-review'), fullPage: true });
    await mariam.close();

    // 11. Gantt PNG export with the bundled Arabic font: the downloaded image is kept as the screenshot.
    await page.goto(`/portfolio/projects/${id}?tab=gantt`);
    await expect(page.getByTestId('project-gantt').locator('svg[role="img"]').first()).toBeVisible();
    const download = page.waitForEvent('download');
    await page.getByTestId('gantt-export-png').first().click();
    const file = await (await download).path();
    expect(file).toBeTruthy();
    const target = screenshotPath(language, 'gantt-export');
    mkdirSync(dirname(target), { recursive: true });
    writeFileSync(target, readFileSync(file));

    await vendorPage.context().close();
    await ahmed.dispose();
  });
});
