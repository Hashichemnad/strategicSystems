import type { APIRequestContext, Page } from '@playwright/test';
import {
  apiPost,
  apiPut,
  expect,
  recordSection,
  signInViaApi,
  test,
  tr,
  unitIdByCode,
  type Language,
} from './fixtures';

/**
 * Phase 9 — Project lifecycle (docs/design/portfolio.md §15–§28): Take Ownership → charter (gate) → approval → LPO → team formation
 * through Teams → planning → monthly cycle (evidence, delay) → change request apply → closure → impact → Completed.
 * Screenshots go to docs/screenshots/phase-9 (E2E_SCREENSHOTS=phase-9).
 */

const k = (language: Language, key: string, params?: Record<string, string | number>) =>
  tr(language, `portfolio:${key}`, params);

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

interface ProjectDto {
  id: string;
  number: string;
  version: number;
  stage: string;
  subState: string;
  status: string;
  performance: number;
  approval: { process: string | null; stepCode: string | null; result: string | null };
}

const year = new Date().getFullYear();
const iso = (d: Date) => d.toISOString().slice(0, 10);
const daysFromToday = (n: number) => iso(new Date(Date.now() + n * 86_400_000));
const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

async function antiforgery(api: APIRequestContext) {
  const token = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [token.headerName]: token.token, 'X-Client': 'spa' };
}

async function upload(api: APIRequestContext, url: string, name: string) {
  const response = await api.post(url, {
    multipart: { file: { name, mimeType: 'application/pdf', buffer: pdf } },
    headers: await antiforgery(api),
  });
  expect(response.ok(), await response.text()).toBe(true);
}

async function project(api: APIRequestContext, id: string): Promise<ProjectDto> {
  return json<ProjectDto>(await api.get(`/api/v1/portfolio/projects/${id}`));
}

/** Mariam (director, PM Office, Finance, GM in the demo) approves the caller's open task on the subject. */
async function approve(admin: APIRequestContext, subject: string, id: string, times = 1) {
  for (let i = 0; i < times; i++) {
    const panel = await json<{ instances: { myTask: { taskId: string } | null }[] }>(
      await admin.get(`/api/v1/workflow/subjects/${subject}/${id}`),
    );
    const task = panel.instances.map((x) => x.myTask).find((t) => t !== null);
    if (!task) throw new Error(`No approval task on ${subject} ${id}`);
    const response = await apiPost(admin, `/api/v1/workflow/tasks/${task.taskId}/act`, {
      action: 'approve',
      comment: null,
    });
    expect(response.status(), await response.text()).toBe(200);
  }
}

async function employeeId(api: APIRequestContext, number: string): Promise<string> {
  const page = (await (await api.get('/api/v1/org/employees', { params: { search: number } })).json()) as {
    items: { id: string; employeeNo: string }[];
  };
  const found = page.items.find((e) => e.employeeNo === number);
  if (!found) throw new Error(`Employee ${number} not found`);
  return found.id;
}

async function waitFor(api: APIRequestContext, id: string, check: (p: ProjectDto) => boolean, what: string) {
  await expect
    .poll(async () => check(await project(api, id)), {
      message: what,
      timeout: 60_000,
      intervals: [500, 1000, 2000],
    })
    .toBe(true);
}

async function openTab(page: Page, language: Language, id: string, tab: string) {
  await page.goto(`/portfolio/projects/${id}?tab=${tab}`);
  await expect(recordSection(page, language, k(language, `project.tabs.${tab}`))).toHaveAttribute(
    'aria-current',
    'page',
  );
}

/** A complete program request with a budget above the threshold: it becomes a Project (Q-P5). */
function requestValues(name: string) {
  const budget = 240_000;
  return {
    nameAr: `مشروع ${name}`,
    nameEn: name,
    description: 'Replace the paper permits with an online service.',
    justifications: 'Applicants wait ten days today.',
    strategicObjectiveCode: 'digital-government',
    suggestionSourceCode: 'idea',
    programTypeCode: 'digital',
    requiresBudget: true,
    estimatedBudget: budget,
    year1Cost: budget * 0.7,
    year2Cost: budget * 0.1,
    year3Cost: budget * 0.1,
    year4Cost: budget * 0.1,
    costPath: 'cost-estimation',
    expectedStartDate: `${year + 1}-01-01`,
    expectedEndDate: `${year + 2}-06-30`,
    programNature: 'new',
    scopeOfWork: 'Online forms, payments and tracking of permits.',
    screenCoreWork: true,
    screenOneTime: true,
    screenTimeBound: true,
    screenUniqueOutput: true,
    screenMinCost: true,
    screenOperationalCosts: true,
    priorityScores: { 'vision-alignment': 5, impact: 4, complexity: 3, 'radical-change': 4 },
    mainOptionCode: 'contracting',
    practiceService: 'Permits service',
  };
}

/** Picks today in the shared date picker (the calendar opens on the current month). */
async function pickToday(page: Page, trigger: ReturnType<Page['locator']>) {
  await trigger.click();
  await page.locator('[data-today] button').first().click();
}

test.describe('portfolio — project lifecycle', () => {
  test('take ownership to completed with the charter gate, team formation, monthly cycle, change request, closure and impact', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    baseURL,
  }) => {
    test.setTimeout(600_000);
    const tag = `${language}${Date.now().toString(36)}`;
    const ahmed = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmed, 'ahmed');
    const ahmedId = await employeeId(adminApi, 'E0002');
    const mariamId = await employeeId(adminApi, 'E0001');
    const fatimaId = await employeeId(adminApi, 'E0003');

    // 0. An approved program request (> threshold) creates the Project in Initiation (Phase 8).
    const request = await json<{ id: string; version: number }>(
      await apiPost(ahmed, '/api/v1/portfolio/requests', { values: requestValues(`Smart permits ${tag}`) }),
    );
    await upload(ahmed, `/api/v1/portfolio/requests/${request.id}/files/cost-estimation`, 'cost.pdf');
    const draft = await json<{ version: number }>(
      await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/requests/${request.id}/submit`, { version: draft.version }),
    );
    await approve(adminApi, 'portfolio.request', request.id, 4); // director → PM Office → DG Office → Budget
    const approved = await json<{ project: { id: string } | null }>(
      await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
    );
    const id = approved.project?.id ?? '';
    expect(id).not.toBe('');

    // 1. Take Ownership: Ahmed works in the project's unit, so the banner offers it.
    await page.goto(`/portfolio/projects/${id}`);
    await expect(page.getByTestId('project-stepper')).toBeVisible();
    const takeButton = page.getByRole('button', { name: k(language, 'actions.takeOwnership') });
    await expect(takeButton).toBeVisible();
    await shot('take-ownership');
    await takeButton.click();
    await expect(page.getByText(k(language, 'currentStatus.project.fillMinimums'))).toBeVisible();

    // 2. Charter: KPI, risk, stakeholder, two proposed members and the high-level plan (API); the gate still blocks (2 of 3 members).
    const base = `/api/v1/portfolio/projects/${id}`;
    await json(
      await apiPost(ahmed, `${base}/kpis`, {
        values: {
          nature: 'new',
          kpiCardId: null,
          projectGoal: 'Faster permits',
          nameAr: 'زمن إصدار التصريح',
          nameEn: 'Permit issuing time',
          equation: 'Average days from application to permit',
          direction: 'decrease',
          indexClassCode: 'outcome',
          target: 2,
          targetYear: year + 1,
          whenMeasured: 'after',
          currentResult: 4,
        },
      }),
    );
    await json(
      await apiPost(ahmed, `${base}/risks`, {
        values: {
          title: language === 'ar' ? 'تأخر المورّد في التوريد' : 'Vendor delivers late',
          categoryCode: 'vendor',
          probability: 4,
          impact: 4,
          riskFactor: null,
          controlEvaluation: 6,
          response: 'mitigate',
          mitigationActions: 'Weekly vendor review',
          implementationPct: 0.2,
          implementationStart: null,
          implementationEnd: null,
          ownerEmployeeId: ahmedId,
          rmsStatus: null,
        },
      }),
    );
    await json(
      await apiPost(ahmed, `${base}/stakeholders`, {
        values: {
          name: language === 'ar' ? 'المتعاملون' : 'Applicants',
          classificationCode: 'customer',
          interestLevel: 'high',
          impactLevel: 'medium',
          communicationMethod: 'Email',
          communicationFrequency: 'Monthly',
          email: null,
          phone: null,
        },
      }),
    );
    for (const employee of [ahmedId, mariamId]) {
      await json(
        await apiPost(ahmed, `${base}/members`, {
          values: { employeeId: employee, roleAr: 'عضو', roleEn: 'Member', responsibilities: null },
        }),
      );
    }
    const milestone = (values: Record<string, unknown>) =>
      apiPost(ahmed, `${base}/milestones`, {
        values: {
          titleAr: null,
          titleEn: null,
          parentId: null,
          isHeader: false,
          highLevel: false,
          plannedStart: daysFromToday(-60),
          plannedEnd: daysFromToday(60),
          weight: 0,
          cost: null,
          isDeliverable: false,
          mainDeliverable: null,
          acceptanceCriteria: null,
          qualityMet: null,
          isInnovative: false,
          innovativeReason: null,
          sort: 0,
          ...values,
        },
      });
    await json(
      await milestone({
        titleAr: 'التصميم والبناء',
        titleEn: 'Design and build',
        highLevel: true,
        weight: 0.7,
      }),
    );
    await json(await milestone({ titleAr: 'الإطلاق', titleEn: 'Launch', highLevel: true, weight: 0.3 }));

    await openTab(page, language, id, 'overview');
    await expect(page.getByTestId('project-gate')).toContainText(
      k(language, 'errors.gateMembers', { min: 3, count: 2 }),
    );
    await recordSection(page, language, k(language, 'project.tabs.risks')).click();
    await expect(page.getByTestId('project-risks')).toContainText(k(language, 'project.risk.bands.high'));
    await shot('charter-risks', { fullPage: true });
    await openTab(page, language, id, 'overview');
    const blocked = await apiPost(ahmed, `${base}/submit-charter`, {
      version: (await project(ahmed, id)).version,
    });
    expect(blocked.status()).toBe(400);
    await shot('charter-gate-blocked', { fullPage: true });

    // The third member through the Team tab; the gate passes and the charter is submitted from the banner.
    await json(
      await apiPost(ahmed, `${base}/members`, {
        values: {
          employeeId: fatimaId,
          roleAr: 'محاسبة',
          roleEn: 'Accountant',
          responsibilities: 'Cost tracking',
        },
      }),
    );
    await openTab(page, language, id, 'team');
    await expect(page.getByTestId('project-members').getByRole('row')).toHaveCount(4);
    await page.getByRole('button', { name: k(language, 'actions.submitCharter') }).click();
    const submitCharter = page.getByTestId('project-submit-dialog');
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'actions.submit') })
      .click();
    await expect(submitCharter).toBeHidden();
    await approve(adminApi, 'portfolio.project', id, 2); // department director → PM Office
    await waitFor(ahmed, id, (p) => p.subState === 'lpo', 'charter approved → LPO');

    // 3. LPO by the Administration Dept (Mariam holds the procurement record role now).
    await upload(adminApi, `${base}/files/lpo`, 'lpo.pdf');
    await json(
      await apiPost(adminApi, `${base}/contracts`, {
        values: {
          name: 'Permits platform',
          number: 'PO-2026-117',
          supplier: 'Gulf Digital LLC',
          issueDate: daysFromToday(-1),
          amount: 230000,
          contractStatus: 'active',
        },
      }),
    );
    await json(
      await apiPost(adminApi, `${base}/lpo-uploaded`, { version: (await project(adminApi, id)).version }),
    );
    await waitFor(ahmed, id, (p) => p.subState === 'team-formation', 'LPO → team formation');

    // 4. Team formation through Teams Management: requested with the project linked, approved director → GM.
    const team = await json<{ id: string; version: number }>(
      await apiPost(ahmed, '/api/v1/teams', {
        typeCode: 'project-team',
        nameAr: `فريق مشروع التصاريح ${tag}`,
        nameEn: `Smart permits project team ${tag}`,
        permanent: false,
        subject: 'Smart permits',
        objectives: 'Deliver the smart permits project.',
        mandate: 'Run the project',
        owningUnitId: await unitIdByCode(adminApi, 'DEP-STR'),
        sponsorEmployeeId: null,
        approvedByCode: 'general-manager',
        memoMode: 'generate',
        memoDraft: null,
        startDate: daysFromToday(0),
        endDate: `${year + 2}-06-30`,
        cadence: 'monthly',
        reportTo: null,
        workload: 'medium',
        replacesTeamId: null,
        evaluatorRule: null,
        evaluatorEmployeeId: null,
        linkedType: 'portfolio.project',
        linkedId: id,
        members: [
          { id: null, employeeId: ahmedId, roleCode: 'project-manager', responsibilities: 'Project manager' },
          { id: null, employeeId: fatimaId, roleCode: 'member', responsibilities: 'Costs' },
        ],
      }),
    );
    await json(
      await apiPost(ahmed, `/api/v1/teams/${team.id}/submit`, {
        version: team.version,
        comment: null,
        reason: null,
      }),
    );
    await approve(adminApi, 'teams.team', team.id, 2);
    await waitFor(ahmed, id, (p) => p.subState === 'planning-draft', 'team active → Planning');
    await openTab(page, language, id, 'team');
    await expect(page.getByTestId('linked-teams')).toContainText(tag);
    await shot('team-formation', { fullPage: true });

    // 5. Planning items (in scope through the UI dialog, out of scope through the API).
    await openTab(page, language, id, 'scope');
    await page
      .getByTestId('project-planning-scope')
      .getByRole('button', { name: k(language, 'project.add') })
      .click();
    const itemDialog = page.getByTestId('item-dialog-planning-items');
    await itemDialog
      .getByLabel(k(language, 'fields.description'))
      .fill(language === 'ar' ? 'نماذج إلكترونية ودفع وتتبع' : 'Online forms, payment and tracking');
    await page
      .getByRole('dialog')
      .getByRole('button', { name: tr(language, 'actions.save') })
      .click();
    await expect(itemDialog).toBeHidden();
    await json(
      await apiPost(ahmed, `${base}/planning-items`, {
        values: {
          category: 'out-of-scope',
          description: language === 'ar' ? 'الربط مع الأنظمة المالية' : 'Integration with the finance ERP',
        },
      }),
    );
    await page.reload();
    await expect(page.getByTestId('project-planning-scope').getByRole('row')).toHaveCount(3);
    await shot('planning-items', { fullPage: true });

    // 6. Milestones: a phase with two milestones (40 % delayed, 60 %) — 100 %; the totals are shown live.
    const phase = await json<{ id: string }>(
      await milestone({
        titleAr: 'التنفيذ',
        titleEn: 'Delivery',
        isHeader: true,
        plannedStart: null,
        plannedEnd: null,
      }),
    );
    const late = await json<{ id: string }>(
      await milestone({
        titleAr: 'تحليل المتطلبات',
        titleEn: 'Requirements analysis',
        parentId: phase.id,
        weight: 0.4,
        plannedStart: daysFromToday(-40),
        plannedEnd: daysFromToday(-5),
      }),
    );
    await openTab(page, language, id, 'milestones');
    await expect(page.getByTestId('weight-detailed')).toContainText('40');
    const build = await json<{ id: string }>(
      await milestone({
        titleAr: 'بناء المنصة',
        titleEn: 'Build the platform',
        parentId: phase.id,
        weight: 0.6,
        plannedStart: daysFromToday(-10),
        plannedEnd: daysFromToday(45),
        isDeliverable: true,
        mainDeliverable: 'The live permits portal',
        acceptanceCriteria: 'Applicants receive a permit online',
      }),
    );
    await page.reload();
    await expect(page.getByTestId('weight-detailed')).toContainText('100');
    await shot('milestones-weights', { fullPage: true });
    await page.getByRole('button', { name: k(language, 'actions.submitPlanning') }).click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'actions.submit') })
      .click();
    await expect(page.getByTestId('project-submit-dialog')).toBeHidden();

    // 6b. The PM recalls the pending planning approval with a reason (Approvals tab), then submits again.
    await openTab(page, language, id, 'approvals');
    await page.getByTestId('recall-approval').click();
    await page
      .getByRole('dialog')
      .getByRole('textbox')
      .fill(language === 'ar' ? 'إضافة مرحلة اختبار' : 'Add a test phase');
    await shot('approval-recall');
    await page.getByTestId('recall-confirm').click();
    await waitFor(
      ahmed,
      id,
      (p) => p.subState === 'planning-draft' && p.approval.result === 'recalled',
      'planning recalled',
    );
    await expect(page.getByTestId('recall-approval')).toBeHidden();
    const recalled = await project(ahmed, id);
    expect(
      (
        await apiPost(ahmed, `/api/v1/portfolio/projects/${id}/submit-planning`, {
          version: recalled.version,
        })
      ).ok(),
    ).toBeTruthy();
    await approve(adminApi, 'portfolio.project', id, 2);
    await waitFor(ahmed, id, (p) => p.subState === 'execution', 'planning approved → Execution');

    // 7. Change request (details: budget 240 000 → 260 000, cost affected → Finance): approved, then applied by the PM.
    const change = await json<{ id: string; version: number }>(
      await apiPost(ahmed, `${base}/change-requests`, {
        values: {
          type: 'details',
          areas: ['budget'],
          reason: language === 'ar' ? 'إضافة الدفع عبر الهاتف' : 'Add mobile payments',
          impactOfNonResponse: 'Applicants keep paying at the counter',
          proposedNameAr: null,
          proposedNameEn: null,
          proposedDescription: null,
          proposedScope: null,
          proposedStart: null,
          proposedEnd: null,
          proposedBudget: 260000,
          costAffected: true,
          newPmEmployeeId: null,
          newOwnerEmployeeId: null,
          mergeIntoProjectId: null,
          requestedDate: null,
        },
      }),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/change-requests/${change.id}/submit`, {
        version: change.version,
        comment: null,
      }),
    );
    await approve(adminApi, 'portfolio.project', id, 3); // director → PM Office → Finance Service Section
    await openTab(page, language, id, 'changes');
    await page.getByTestId('change-apply').click();
    const applyDialog = page.getByTestId('apply-dialog');
    await expect(applyDialog).toBeVisible();
    await shot('change-request-apply');
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'project.change.confirmApply') })
      .click();
    await expect(applyDialog).toBeHidden();
    await expect(page.getByTestId('change-log')).toContainText('260000');
    await shot('change-request-applied', { fullPage: true });

    // 8. Monthly cycle: the PM Office opens the window now; the PM updates both milestones (delay rules, evidence at 100 %).
    await json(await apiPost(adminApi, `${base}/monthly/open`));
    await openTab(page, language, id, 'milestones');
    const rows = page.getByTestId('milestone-row');
    await rows
      .filter({ hasText: language === 'ar' ? 'تحليل المتطلبات' : 'Requirements analysis' })
      .getByTestId('milestone-update')
      .click();
    const progress = page.getByTestId('progress-dialog');
    await progress.getByTestId('progress-achieve').fill('20');
    await pickToday(page, progress.getByRole('button', { name: tr(language, 'datePicker.open') }).first());
    await page
      .getByRole('dialog')
      .getByRole('button', { name: tr(language, 'actions.save') })
      .click();
    await expect(progress.getByTestId('field-errors')).toContainText(k(language, 'errors.causalityRequired'));
    await shot('monthly-update-delay');
    await progress
      .getByTestId('progress-causality')
      .fill(language === 'ar' ? 'تأخر اعتماد المتطلبات' : 'Requirements sign-off was late');
    await progress
      .getByTestId('progress-corrective')
      .fill(language === 'ar' ? 'ورش عمل يومية' : 'Daily workshops with the owners');
    await page
      .getByRole('dialog')
      .getByRole('button', { name: tr(language, 'actions.save') })
      .click();
    await expect(progress).toBeHidden();

    await rows
      .filter({ hasText: language === 'ar' ? 'بناء المنصة' : 'Build the platform' })
      .getByTestId('milestone-update')
      .click();
    await progress.getByTestId('progress-achieve').fill('60');
    await pickToday(page, progress.getByRole('button', { name: tr(language, 'datePicker.open') }).first());
    await pickToday(page, progress.getByRole('button', { name: tr(language, 'datePicker.open') }).first());
    await progress
      .locator('input[type=file]')
      .setInputFiles({ name: 'acceptance.pdf', mimeType: 'application/pdf', buffer: pdf });
    await progress.getByRole('button', { name: k(language, 'project.milestones.uploadEvidence') }).click();
    await expect(progress.getByTestId('progress-evidence')).toContainText('acceptance.pdf');
    await shot('monthly-update-evidence');
    await page
      .getByRole('dialog')
      .getByRole('button', { name: tr(language, 'actions.save') })
      .click();
    await expect(progress).toBeHidden();

    await openTab(page, language, id, 'monthly');
    await page.getByRole('button', { name: k(language, 'project.monthly.submit') }).click();
    await expect(page.getByTestId('project-monthly')).toContainText(
      k(language, 'project.monthly.statuses.submitted'),
    );
    await approve(adminApi, 'portfolio.project', id, 2); // Q-P1: director → PM Office
    await waitFor(
      ahmed,
      id,
      (p) => p.approval.result === 'approved' && Math.abs(p.performance - 0.8) < 0.0001,
      'monthly approved (80 %)',
    );
    await openTab(page, language, id, 'overview');
    await shot('project-record-execution', { fullPage: true });

    // A second window in the same month: the delayed milestone reaches 100 % with evidence → Closure.
    await json(await apiPost(adminApi, `${base}/monthly/open`));
    await upload(ahmed, `/api/v1/portfolio/milestones/${late.id}/evidence`, 'requirements-signed.pdf');
    const plan = await json<{ milestones: { id: string; version: number; actualStart: string | null }[] }>(
      await ahmed.get(`${base}/milestones`),
    );
    const lateRow = plan.milestones.find((m) => m.id === late.id);
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/milestones/${late.id}/progress`, {
        version: lateRow?.version,
        values: {
          achieve: 0.4,
          actualStart: lateRow?.actualStart,
          actualEnd: daysFromToday(0),
          causality: 'Late sign-off',
          correctiveActions: 'Workshops',
          evidenceNote: null,
        },
      }),
    );
    await json(
      await apiPost(ahmed, `${base}/monthly/submit`, {
        version: (await project(ahmed, id)).version,
        comment: null,
      }),
    );
    await approve(adminApi, 'portfolio.project', id, 2);
    await waitFor(ahmed, id, (p) => p.subState === 'closure-draft', '100 % → Closure');
    expect(build.id).not.toBe('');

    // Phase 10 quality (ADR 0095): the PM Office signs off every deliverable before the closure.
    const finalPlan = await json<{ milestones: { id: string; isDeliverable: boolean; version: number }[] }>(
      await ahmed.get(`${base}/milestones`),
    );
    for (const deliverable of finalPlan.milestones.filter((m) => m.isDeliverable)) {
      await json(
        await apiPost(adminApi, `/api/v1/portfolio/milestones/${deliverable.id}/sign-off`, {
          decision: 'accepted',
          comment: 'Meets the acceptance criteria',
          version: deliverable.version,
        }),
      );
    }

    // 9. Closure form with a lesson learned; submitted and approved → Impact (the project team's evaluation opens in Teams).
    await json(
      await apiPost(ahmed, `${base}/lessons`, {
        values: {
          stage: 'execution',
          lesson: 'Sign requirements off before the build starts',
          typeCode: 'improvement',
          recommendation: 'Add a sign-off gate',
        },
      }),
    );
    await openTab(page, language, id, 'closure');
    const closure = page.getByTestId('project-closure');
    await closure
      .getByLabel(k(language, 'project.closure.summary'))
      .fill(
        language === 'ar'
          ? 'أُطلقت المنصة وتصدر التصاريح خلال يومين.'
          : 'The portal is live; permits are issued in two days.',
      );
    await closure
      .getByLabel(k(language, 'project.closure.deliverables'))
      .fill(language === 'ar' ? 'بوابة التصاريح الذكية' : 'The smart permits portal');
    await closure.getByRole('button', { name: k(language, 'actions.saveDraft') }).click();
    await expect(page.getByText(k(language, 'project.saved')).first()).toBeVisible();
    await shot('closure-form', { fullPage: true });
    await closure.getByRole('button', { name: k(language, 'project.closure.submit') }).click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'actions.submit') })
      .click();
    await expect(page.getByTestId('project-submit-dialog')).toBeHidden();
    await approve(adminApi, 'portfolio.project', id, 2);
    await waitFor(ahmed, id, (p) => p.subState === 'impact-draft', 'closure approved → Impact');

    // 10. Impact, approved by the PM Office → Completed.
    await openTab(page, language, id, 'impact');
    const impact = page.getByTestId('project-impact');
    await impact.getByTestId('impact-cost').fill('1.05');
    for (const key of ['community', 'employee', 'vendor']) {
      await impact.getByLabel(k(language, `project.impact.${key}`)).click();
      await page.getByRole('option', { name: k(language, 'project.impact.scale.4') }).click();
    }
    await impact.getByRole('button', { name: k(language, 'actions.saveDraft') }).click();
    await expect(page.getByText(k(language, 'project.saved')).first()).toBeVisible();
    await shot('impact', { fullPage: true });
    await impact.getByRole('button', { name: k(language, 'project.impact.submit') }).click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'actions.submit') })
      .click();
    await expect(page.getByTestId('project-submit-dialog')).toBeHidden();
    await approve(adminApi, 'portfolio.project', id);
    await waitFor(
      ahmed,
      id,
      (p) => p.status === 'completed' && p.stage === 'completed',
      'impact approved → Completed',
    );
    await openTab(page, language, id, 'overview');
    await expect(page.getByText(k(language, 'currentStatus.project.completed')).first()).toBeVisible();
    await shot('project-record-completed', { fullPage: true });

    // The project team's evaluation was opened by the closure (ADR 0072).
    await expect
      .poll(
        async () =>
          ((await (await adminApi.get(`/api/v1/teams/${team.id}`)).json()) as { status: string }).status,
        { timeout: 60_000 },
      )
      .not.toBe('active');
    await apiPut(ahmed, '/api/v1/identity/me/preferences', { language, theme: 'light' });
    await ahmed.dispose();
  });
});
