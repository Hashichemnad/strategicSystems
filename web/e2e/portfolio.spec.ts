import type { APIRequestContext, Page } from '@playwright/test';
import { apiPost, apiPut, expect, signInViaApi, test, tr, type Language } from './fixtures';

/** Phase 8 — Program Requests (docs/design/portfolio.md): screenshots go to docs/screenshots/phase-8 (E2E_SCREENSHOTS=phase-8). */

const k = (language: Language, key: string, params?: Record<string, string | number>) =>
  tr(language, `portfolio:${key}`, params);

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

interface RequestDto {
  id: string;
  number: string;
  version: number;
  status: string;
  approvalStepCode: string | null;
  approvalStatus: string | null;
  project: { id: string; number: string } | null;
}

const year = new Date().getFullYear();

/** A complete request (every SPEC §1.4 A rule satisfied); with a budget Year 1–4 add up to it. */
function values(name: string, budget: number | null, extra: Record<string, unknown> = {}) {
  return {
    nameAr: `برنامج ${name}`,
    nameEn: name,
    description: 'Digitise the licensing service end to end, from application to payment and tracking.',
    justifications: 'Applicants wait ten days today; the target is two days.',
    strategicObjectiveCode: 'digital-government',
    suggestionSourceCode: 'idea',
    programTypeCode: 'digital',
    requiresBudget: budget !== null,
    estimatedBudget: budget,
    year1Cost: budget === null ? null : budget * 0.7,
    year2Cost: budget === null ? null : budget * 0.1,
    year3Cost: budget === null ? null : budget * 0.1,
    year4Cost: budget === null ? null : budget * 0.1,
    costPath: budget === null ? null : 'cost-estimation',
    expectedStartDate: `${year + 1}-01-01`,
    expectedEndDate: `${year + 2}-06-30`,
    programNature: 'new',
    scopeOfWork: 'Online forms, payments and tracking.',
    screenCoreWork: true,
    screenOneTime: true,
    screenTimeBound: true,
    screenUniqueOutput: true,
    screenMinCost: true,
    screenOperationalCosts: true,
    priorityScores: { 'vision-alignment': 5, impact: 4, complexity: 3, 'radical-change': 4 },
    mainOptionCode: 'contracting',
    practiceService: 'Licensing service',
    ...extra,
  };
}

async function create(api: APIRequestContext, body: Record<string, unknown>): Promise<RequestDto> {
  return json<RequestDto>(await apiPost(api, '/api/v1/portfolio/requests', { values: body }));
}

async function get(api: APIRequestContext, id: string): Promise<RequestDto> {
  return json<RequestDto>(await api.get(`/api/v1/portfolio/requests/${id}`));
}

async function costFile(api: APIRequestContext, id: string) {
  const response = await api.post(`/api/v1/portfolio/requests/${id}/files/cost-estimation`, {
    multipart: {
      file: {
        name: 'cost-estimation.pdf',
        mimeType: 'application/pdf',
        buffer: Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n'),
      },
    },
    headers: await (async () => {
      const token = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
        token: string;
        headerName: string;
      };
      return { [token.headerName]: token.token, 'X-Client': 'spa' };
    })(),
  });
  expect(response.ok(), await response.text()).toBe(true);
}

async function submit(api: APIRequestContext, id: string) {
  const request = await get(api, id);
  await json(await apiPost(api, `/api/v1/portfolio/requests/${id}/submit`, { version: request.version }));
}

/** The administrator (PM Office, DG Office, Budget, department director in the demo) acts on the step in progress. */
async function act(
  admin: APIRequestContext,
  id: string,
  expectedStep: string,
  action = 'approve',
  comment: string | null = null,
) {
  const request = await get(admin, id);
  expect(request.approvalStepCode).toBe(expectedStep);
  const panel = await json<{ instances: { myTask: { taskId: string } | null }[] }>(
    await admin.get(`/api/v1/workflow/subjects/portfolio.request/${id}`),
  );
  const task = panel.instances[0]?.myTask;
  if (!task) throw new Error(`No task on ${expectedStep}`);
  const response = await apiPost(admin, `/api/v1/workflow/tasks/${task.taskId}/act`, { action, comment });
  expect(response.status(), await response.text()).toBe(200);
}

async function openTab(page: Page, language: Language, id: string, tab: string) {
  await page.goto(`/portfolio/requests/${id}?tab=${tab}`);
  await expect(page.getByRole('tab', { name: k(language, `tabs.${tab}`) })).toHaveAttribute(
    'aria-selected',
    'true',
  );
}

test.describe('portfolio — program requests', () => {
  test('the wizard, the Year 1–4 rule, the three approval routes, return for correction and the created project', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    baseURL,
  }) => {
    test.setTimeout(300_000);
    const tag = `${language}${Date.now().toString(36)}`;
    const ahmed = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmed, 'ahmed');

    // 1. The requester starts a request in the wizard (fill manually) and saves a draft.
    await page.goto('/portfolio/requests/new');
    await expect(page.getByText(k(language, 'start.title'))).toBeVisible();
    await shot('request-start');
    await page.getByRole('button', { name: k(language, 'start.manual') }).click();
    await page.getByLabel(k(language, 'fields.nameAr')).fill(`منصة الترخيص الذكي ${tag}`);
    await page.getByLabel(k(language, 'fields.nameEn')).fill(`Smart licensing platform ${tag}`);
    await page
      .getByLabel(k(language, 'fields.description'))
      .fill('Digitise the licensing service end to end.');
    await page
      .getByLabel(k(language, 'fields.justifications'))
      .fill('Applicants wait ten days today; the target is two days.');
    await shot('request-wizard', { fullPage: true });
    await page.getByRole('button', { name: tr(language, 'actions.saveDraft') }).click();
    await expect(page).toHaveURL(/\/portfolio\/requests\/[0-9a-f-]+\/edit$/u);
    const id = /requests\/([0-9a-f-]+)\/edit/u.exec(page.url())?.[1] ?? '';

    // 2. The rest of the answers (with Year 4 one dirham off) through the API; the wizard shows the live checks.
    let request = await get(ahmed, id);
    const large = values(`Smart licensing platform ${tag}`, 250_000, {
      nameAr: `منصة الترخيص الذكي ${tag}`,
      year4Cost: 25_001,
    });
    await json(
      await apiPut(ahmed, `/api/v1/portfolio/requests/${id}`, { values: large, version: request.version }),
    );
    await page.reload();
    await page.getByRole('button', { name: tr(language, 'actions.next') }).click();
    await expect(page.getByTestId('year-total')).toContainText(k(language, 'live.totalMismatch'));
    await shot('request-wizard-budget', { fullPage: true });
    await page.getByRole('button', { name: tr(language, 'actions.next') }).click();
    await expect(page.getByTestId('live-evaluation')).toBeVisible();
    await shot('request-wizard-screening', { fullPage: true });

    // 3. Submission is blocked by the Year 1–4 rule with a clear message.
    await costFile(ahmed, id);
    await openTab(page, language, id, 'overview');
    await page
      .getByRole('button', { name: k(language, 'actions.submit') })
      .first()
      .click();
    const dialog = page.getByRole('dialog', { name: k(language, 'dialogs.submitTitle') });
    await dialog.getByRole('button', { name: k(language, 'actions.submit') }).click();
    await expect(dialog.getByTestId('submit-errors')).toContainText(
      k(language, 'errors.yearCostsMismatch', { total: '250001.00', budget: '250000.00' }),
    );
    await shot('request-submit-blocked');
    await dialog.getByRole('button', { name: tr(language, 'actions.cancel') }).click();

    // 4. Corrected and submitted from the record page: route > threshold (Director → PM Office → DG Office → Budget).
    request = await get(ahmed, id);
    await json(
      await apiPut(ahmed, `/api/v1/portfolio/requests/${id}`, {
        values: { ...large, year4Cost: 25_000 },
        version: request.version,
      }),
    );
    await page.reload();
    await page
      .getByRole('button', { name: k(language, 'actions.submit') })
      .first()
      .click();
    await page
      .getByRole('dialog', { name: k(language, 'dialogs.submitTitle') })
      .getByRole('button', { name: k(language, 'actions.submit') })
      .click();
    await expect(page.getByText(k(language, 'toast.submitted')).first()).toBeVisible();
    await act(adminApi, id, 'department-director');
    await act(adminApi, id, 'pm-office');
    await act(adminApi, id, 'dg-office');
    await json(
      await apiPost(adminApi, `/api/v1/portfolio/requests/${id}/approved-budget`, {
        amount: 240_000,
        version: (await get(adminApi, id)).version,
      }),
    );
    await openTab(page, language, id, 'approvals');
    await shot('approval-route-large-at-budget', { fullPage: true });
    await act(adminApi, id, 'budget-section');

    // 5. Route ≤ threshold (… → PM Office → Budget) and route without budget (… → PM Office).
    const small = await create(ahmed, values(`Archive scanning ${tag}`, 30_000));
    await costFile(ahmed, small.id);
    await submit(ahmed, small.id);
    await act(adminApi, small.id, 'department-director');
    await act(adminApi, small.id, 'pm-office');
    await act(adminApi, small.id, 'budget-section');
    await openTab(page, language, small.id, 'approvals');
    await shot('approval-route-small', { fullPage: true });

    const noBudget = await create(ahmed, values(`Open data portal ${tag}`, null));
    await submit(ahmed, noBudget.id);
    await act(adminApi, noBudget.id, 'department-director');
    await act(adminApi, noBudget.id, 'pm-office');
    await openTab(page, language, noBudget.id, 'approvals');
    await shot('approval-route-no-budget', { fullPage: true });
    expect((await get(ahmed, noBudget.id)).status).toBe('approved');

    // 6. Return for correction: the director returns, the requester sees it, corrects and resubmits.
    const returned = await create(ahmed, values(`Customer survey ${tag}`, null));
    await submit(ahmed, returned.id);
    await act(
      adminApi,
      returned.id,
      'department-director',
      'return',
      'Add measurable benefits and a target date.',
    );
    await openTab(page, language, returned.id, 'overview');
    await expect(page.getByText(k(language, 'currentStatus.returned'))).toBeVisible();
    await shot('request-returned');
    await page.getByRole('button', { name: k(language, 'actions.resubmit') }).click();
    await page
      .getByRole('dialog', { name: k(language, 'dialogs.submitTitle') })
      .getByRole('button', { name: k(language, 'actions.submit') })
      .click();
    await expect(page.getByText(k(language, 'toast.submitted')).first()).toBeVisible();
    await act(adminApi, returned.id, 'department-director');
    await act(adminApi, returned.id, 'pm-office');
    await openTab(page, language, returned.id, 'approvals');
    await shot('request-resubmitted-approved', { fullPage: true });

    // 7. The large request created a Project numbered PROJ-…; the banner and the project page say so.
    const done = await get(ahmed, id);
    expect(done.status).toBe('approved');
    expect(done.project?.number).toMatch(new RegExp(`^PROJ-[A-Z0-9-]+-${year}-\\d{3}$`, 'u'));
    await openTab(page, language, id, 'overview');
    await expect(
      page.getByText(k(language, 'currentStatus.approved', { project: done.project?.number ?? '' })),
    ).toBeVisible();
    await shot('request-approved');
    await page.goto(`/portfolio/projects/${done.project?.id ?? ''}`);
    await expect(page.getByTestId('project-page')).toContainText(done.project?.number ?? '');
    await expect(page.getByText(k(language, 'currentStatus.project.takeOwnership'))).toBeVisible();
    await shot('project-created', { fullPage: true });
    await page.goto('/portfolio/requests?view=mine');
    await expect(page.getByTestId('requests-table')).toContainText(done.number);
    await shot('requests-list');
    await ahmed.dispose();
  });

  test('start from a document: AI fills the wizard for review, nothing is saved before confirmation, the Proposal Assessment is written', async ({
    page,
    language,
    shot,
  }) => {
    test.setTimeout(180_000);
    const tag = `${language}${Date.now().toString(36)}`;
    const document = [
      'Business case — Smart permits',
      `Program name (English): Smart permits ${tag}`,
      `اسم البرنامج بالعربية: التصاريح الذكية ${tag}`,
      'Program description: Issue building permits online with automatic checks of the drawings.',
      'Program justifications: Permits take 21 days and three visits today.',
      'Scope of work: Online application, automatic checks, payment and tracking.',
      'Strategic objective: digital-government',
      'Program type: digital',
      'Source of suggestion: benchmarking',
      'Program nature: new',
      `Expected start date: ${year + 1}-02-01`,
      `Expected end date: ${year + 2}-01-31`,
      'Requires budget: yes',
      'Estimated budget: 400,000',
      'Year 1 cost: 250,000',
      'Year 2 cost: 150,000',
      'Work is core to the unit: yes',
      'Done once without repetition: yes',
    ].join('\n');

    await page.goto('/portfolio/requests/new?mode=document');
    const start = page.getByTestId('start-from-document');
    await start
      .locator('input[type="file"]')
      .setInputFiles({ name: 'business-case.txt', mimeType: 'text/plain', buffer: Buffer.from(document) });
    await start.getByRole('button', { name: k(language, 'start.add') }).click();
    await expect(page.getByTestId('documents-to-read')).toContainText('business-case.txt');
    await shot('start-from-document');
    await start.getByRole('button', { name: k(language, 'start.read') }).click();

    // The wizard opens with highlighted drafts to review; missing fields are listed; creating waits for the review.
    await expect(page.getByLabel(k(language, 'fields.nameEn'))).toHaveValue(`Smart permits ${tag}`);
    await expect(page.getByText(k(language, 'start.missingTitle'))).toBeVisible();
    await expect(page.getByRole('button', { name: tr(language, 'actions.saveDraft') })).toBeDisabled();
    await page.getByTestId('ai-field-review').first().getByText(tr(language, 'ai:source')).click();
    await shot('start-from-document-review', { fullPage: true });
    const before = (await (
      await page.request.get('/api/v1/portfolio/requests?view=mine&search=' + tag)
    ).json()) as { totalCount: number };
    expect(before.totalCount).toBe(0); // nothing saved before confirmation

    await page.getByRole('button', { name: tr(language, 'ai:acceptAll') }).click();
    for (let i = 0; i < 5; i++)
      await page.getByRole('button', { name: tr(language, 'actions.next') }).click();
    await page.getByRole('button', { name: k(language, 'actions.createDraft') }).click();

    // Created with the document attached and the first Proposal Assessment (demo AI provider).
    await expect(page).toHaveURL(/tab=assessment/u, { timeout: 60_000 });
    await expect(page.getByTestId('proposal-assessment')).toBeVisible({ timeout: 60_000 });
    await expect(page.getByTestId('assessment-gaps')).toBeVisible();
    await shot('proposal-assessment', { fullPage: true });
    await page.getByRole('button', { name: k(language, 'actions.regenerate') }).click();
    await expect(page.getByText(k(language, 'toast.assessed', { version: 2 })).first()).toBeVisible({
      timeout: 60_000,
    });
    await shot('proposal-assessment-v2');
  });
});
