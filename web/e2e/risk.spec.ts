import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync, renameSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { APIRequestContext } from '@playwright/test';
import {
  apiPost,
  expect,
  screenshotPath,
  signInViaApi,
  test,
  tr,
  unitIdByCode,
  type Language,
} from './fixtures';

/** Phase 13 — Risk Management (docs/design/risk.md): screenshots go to docs/screenshots/phase-13 (E2E_SCREENSHOTS=phase-13). */

const r = (language: Language, key: string) => tr(language, `risk:${key}`);
const year = new Date().getFullYear();
const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

async function antiforgery(api: APIRequestContext): Promise<Record<string, string>> {
  const { token, headerName } = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [headerName]: token, 'X-Client': 'spa' };
}

async function upload(api: APIRequestContext, url: string, name: string, mimeType = 'application/pdf') {
  const response = await api.post(url, {
    headers: await antiforgery(api),
    multipart: { file: { name, mimeType, buffer: name.endsWith('.txt') ? Buffer.from('Report: 120') : pdf } },
  });
  expect(response.ok(), await response.text()).toBe(true);
}

async function employeeIdByNumber(api: APIRequestContext, number: string): Promise<string> {
  const page = await json<{ items: { id: string; employeeNo: string }[] }>(
    await api.get('/api/v1/org/employees', { params: { search: number } }),
  );
  const employee = page.items.find((e) => e.employeeNo === number);
  if (!employee) throw new Error(`Employee ${number} not found`);
  return employee.id;
}

/** The administrator (Mariam: Risk Office, PM Office, KPI Strategy Office in the demo) approves her open task on the subject. */
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

/** The demo department's employees become department risk coordinators (Settings → Roles in the UI; API here). */
async function makeCoordinators(admin: APIRequestContext, unitId: string) {
  const roles = await json<{ id: string; systemCode: string; code: string }[]>(
    await admin.get('/api/v1/access/roles', { params: { system: 'risk' } }),
  );
  const role = roles.find((x) => x.code === 'coordinator');
  if (!role) throw new Error('risk.coordinator role not found');
  const existing = await json<{ principalId: string | null }[]>(
    await admin.get('/api/v1/access/assignments', { params: { roleId: role.id } }),
  );
  if (existing.some((a) => a.principalId === unitId)) return;
  const response = await apiPost(admin, '/api/v1/access/assignments', {
    roleId: role.id,
    principalKind: 'unit',
    principalId: unitId,
    validFrom: null,
    validTo: null,
    note: 'E2E Phase 13',
  });
  // A second run (or the other language project) finds the assignment already there.
  expect(response.ok() || response.status() === 409, await response.text()).toBe(true);
}

interface RiskDto {
  id: string;
  number: string;
  version: number;
  status: string;
}

test.describe('risk management', () => {
  test.use({ signedInAs: 'mariam' });

  test('framework formula preview, wizard matrix, risk page tabs, heat-maps, Top-10, KRI breach, Project Risks, mechanism and report PDF', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    baseURL,
  }) => {
    test.setTimeout(600_000);
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const arabic = language === 'ar';
    const department = await unitIdByCode(adminApi, 'DEP-STR');
    const ahmedId = await employeeIdByNumber(adminApi, 'E0002');
    await makeCoordinators(adminApi, department);
    const ahmed = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmed, 'ahmed');

    // ---- Data: Ahmed (coordinator) registers and submits; the Risk Office (Mariam) approves --------------------------------------
    const values = (
      ar: string,
      en: string,
      l: number,
      i: number,
      extra: Record<string, unknown> = {},
    ): Record<string, unknown> => ({
      titleAr: `${ar} ${tag}`,
      titleEn: `${en} ${tag}`,
      cause: arabic ? 'نظام دفع قديم لا يحظى بدعم المورد' : 'Legacy payment platform without vendor support',
      eventText: arabic
        ? 'تعطل منصة الدفع أثناء صرف الرواتب'
        : 'The payment platform fails during the salary run',
      consequence: arabic
        ? 'تأخر صرف الرواتب وشكاوى الموظفين وتغطية إعلامية'
        : 'Salaries are paid late, staff complain and the media report it',
      determinedByCode: 'risk-workshop',
      identifiedOn: `${year}-02-01`,
      riskType: 'internal',
      categoryCodes: ['operational'],
      emerging: false,
      opportunity: false,
      unitId: department,
      ownerEmployeeId: ahmedId,
      inherentLikelihood: l,
      inherentImpact: i,
      inherentImpactArea: 'service',
      responseCode: 'reduce',
      needsFollowUp: true,
      ...extra,
    });
    const register = async (body: Record<string, unknown>, submit = true): Promise<RiskDto> => {
      const created = await json<RiskDto>(await apiPost(ahmed, '/api/v1/risk/risks', body));
      if (!submit) return created;
      await json(await apiPost(ahmed, `/api/v1/risk/risks/${created.id}/submit`, { comment: null }));
      await approve(adminApi, 'risk.risk', created.id);
      return json<RiskDto>(await adminApi.get(`/api/v1/risk/risks/${created.id}`));
    };
    const payment = await register(
      values('تعطل منصة الدفع', 'Payment platform outage', 5, 5, {
        residualLikelihood: 5,
        residualImpact: 4,
        targetLikelihood: 2,
        targetImpact: 3,
        targetDate: `${year + 1}-06-30`,
      }),
    );
    expect(payment.status).toBe('active');
    const fraud = await register(
      values('احتيال في المدفوعات', 'Payment fraud', 4, 3, { residualLikelihood: 3, residualImpact: 3 }),
    );
    const vendor = await register(values('تأخر المورد', 'Vendor delay', 2, 3));
    await json(
      await apiPost(adminApi, `/api/v1/risk/risks/${vendor.id}/office-update`, {
        controlEffectivenessOverride: null,
        residualStatusOverride: null,
        justification: null,
        top10: true,
        top10Rank: 1,
        version: vendor.version,
      }),
    );
    const draft = await register(values('فقدان بيانات', 'Data loss', 3, 3), false);

    // Controls and a treatment action on the payment risk.
    const base = `/api/v1/risk/risks/${payment.id}`;
    await json(
      await apiPost(ahmed, `${base}/controls`, {
        values: {
          nameAr: 'اعتماد مزدوج للدفعات',
          nameEn: 'Dual approval of payments',
          description: null,
          typeCode: 'preventive',
          natureCode: 'manual',
          ownerEmployeeId: ahmedId,
          frequencyCode: 'daily',
          effectivenessCode: 'partially-effective',
          lastAssessedOn: `${year}-03-01`,
          evidence: null,
        },
      }),
    );
    const action = await json<{ id: string; version: number }>(
      await apiPost(ahmed, `${base}/actions`, {
        values: {
          titleAr: 'ترحيل منصة الدفع إلى نظام مدعوم',
          titleEn: 'Migrate the payment platform to a supported system',
          ownerEmployeeId: ahmedId,
          responsibleUnitIds: [department],
          plannedStart: `${year}-03-01`,
          plannedEnd: `${year + 1}-03-31`,
          cost: 450000,
        },
      }),
    );
    await json(
      await apiPost(ahmed, `/api/v1/risk/actions/${action.id}/progress`, {
        completionPct: 0.4,
        note: arabic ? 'اكتمل تحليل المتطلبات' : 'Requirements analysis complete',
        actualStart: `${year}-03-05`,
        actualEnd: null,
        version: action.version,
      }),
    );

    // ---- KRI: a KPI card linked to the payment risk; an approved result above the breach threshold flags it -----------------------
    const card = await json<{ id: string; code: string }>(
      await apiPost(adminApi, '/api/v1/kpi/cards', {
        values: {
          nameAr: `شكاوى الدفع ${tag}`,
          nameEn: `Payment complaints ${tag}`,
          measuringEquation: 'complaints per month',
          nature: 'quantitative',
          measuringMethodCode: 'average',
          unitCode: 'number',
          measuringPeriod: 'quarterly',
          direction: 'decrease',
          targetType: 'single',
          typeCode: 'operational',
          departmentUnitId: department,
          responsibleUnitId: department,
          responsibleEmployeeId: ahmedId,
          level: 'department',
          isGlobalStar: false,
          sourceCodes: [],
          gemCriteriaCodes: [],
          dataElements: [],
          scale: [],
        },
      }),
    );
    await json(
      await apiPost(ahmed, `${base}/kris`, {
        values: {
          kpiCardId: card.id,
          basis: 'value',
          direction: 'above',
          warningThreshold: 80,
          breachThreshold: 100,
        },
      }),
    );
    const kpiYear = 2040 + (arabic ? 0 : 1);
    await json(
      await apiPost(adminApi, `/api/v1/kpi/years/${kpiYear}/start`, {
        items: [
          {
            cardId: card.id,
            include: true,
            measuringPeriod: 'quarterly',
            unitCode: 'number',
            direction: 'decrease',
            targetType: 'single',
            target: 60,
            rangeMin: null,
            rangeMax: null,
            isAccumulated: false,
          },
        ],
      }),
    );
    const plans = await json<{ id: string; cardId: string }[]>(
      await adminApi.get('/api/v1/kpi/plans', { params: { year: kpiYear } }),
    );
    const plan = plans.find((p) => p.cardId === card.id);
    await json(await apiPost(adminApi, '/api/v1/kpi/plans/confirm', { planIds: [plan?.id] }));
    const periods = await json<{ periods: { resultId: string; periodCode: string }[] }>(
      await adminApi.get(`/api/v1/kpi/plans/${plan?.id}`),
    );
    const resultId = periods.periods.find((p) => p.periodCode === 'q1')?.resultId ?? 'missing';
    await json(await apiPost(adminApi, `/api/v1/kpi/results/${resultId}/open-now`));
    await upload(ahmed, `/api/v1/files/owners/kpi.result/${resultId}`, 'complaints.txt', 'text/plain');
    const result = await json<{ version: number }>(await ahmed.get(`/api/v1/kpi/results/${resultId}`));
    await json(
      await apiPost(ahmed, `/api/v1/kpi/results/${resultId}/submit`, {
        entry: {
          achieve: 120,
          causality: 'Outage in March',
          correctiveActions: 'Platform migration',
          notes: null,
          notApplicable: false,
        },
        comment: null,
        version: result.version,
      }),
    );
    await json(
      await apiPost(adminApi, `/api/v1/kpi/results/${resultId}/decide`, {
        action: 'approve',
        comment: null,
        checklist: {
          'evidence-readable': true,
          'evidence-supports': true,
          'equation-followed': true,
          'correct-period': true,
          'justification-adequate': true,
        },
      }),
    );
    await expect
      .poll(
        async () =>
          (
            await json<{ reassessmentRequired: boolean }>(
              await adminApi.get(`/api/v1/risk/risks/${payment.id}`),
            )
          ).reassessmentRequired,
        { timeout: 30_000 },
      )
      .toBe(true);

    // ---- A Portfolio project with a project risk (Project Risks tab, ADR 0103) ---------------------------------------------------
    const budget = 240_000;
    const request = await json<{ id: string }>(
      await apiPost(ahmed, '/api/v1/portfolio/requests', {
        values: {
          nameAr: `مشروع الدفع الرقمي ${tag}`,
          nameEn: `Digital payments ${tag}`,
          description: 'Replace the payment platform.',
          justifications: 'The platform is out of support.',
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
          scopeOfWork: 'New payment platform.',
          screenCoreWork: true,
          screenOneTime: true,
          screenTimeBound: true,
          screenUniqueOutput: true,
          screenMinCost: true,
          screenOperationalCosts: true,
          priorityScores: { 'vision-alignment': 5, impact: 4, complexity: 3, 'radical-change': 4 },
          mainOptionCode: 'contracting',
          practiceService: 'Payments',
        },
      }),
    );
    await upload(ahmed, `/api/v1/portfolio/requests/${request.id}/files/cost-estimation`, 'cost.pdf');
    const requestDraft = await json<{ version: number }>(
      await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/requests/${request.id}/submit`, {
        version: requestDraft.version,
      }),
    );
    await approve(adminApi, 'portfolio.request', request.id, 4);
    const approved = await json<{ project: { id: string } | null }>(
      await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
    );
    const projectId = approved.project?.id ?? 'missing';
    const project = await json<{ version: number }>(
      await ahmed.get(`/api/v1/portfolio/projects/${projectId}`),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/projects/${projectId}/take-ownership`, {
        version: project.version,
      }),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/projects/${projectId}/risks`, {
        values: {
          title: arabic ? `تأخر المورد في التوريد ${tag}` : `Vendor delivers late ${tag}`,
          categoryCode: 'vendor',
          probability: 4,
          impact: 5,
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

    // ---- 1. Settings → Risk framework: custom formula with the live matrix preview and the count of risks that would change ----------
    await page.goto('/settings/risk');
    await expect(page.getByTestId('risk-framework-settings')).toBeVisible();
    await page.getByTestId('risk-method-custom').click();
    await page.getByTestId('risk-formula').fill('(L * I) * (1 - C/10)');
    await expect(page.getByTestId('risk-preview-summary')).toBeVisible({ timeout: 15_000 });
    await expect(page.getByTestId('risk-matrix-preview-5-5')).toBeVisible();
    // The payment risk's residual (5 × 4 with a partially effective control, C = 5) drops from Critical to High.
    await expect(page.getByTestId('risk-preview-examples')).toContainText(payment.number, {
      timeout: 15_000,
    });
    await page.getByTestId('risk-framework-preview').scrollIntoViewIfNeeded();
    await shot('framework-formula-preview');
    await shot('framework-settings', { fullPage: true });

    // ---- 2. Risk wizard: the assessment step with the clickable matrix ----------------------------------------------------------
    await page.goto(`/risk/risks/${draft.id}/edit`);
    await expect(page.getByTestId('risk-wizard')).toBeVisible();
    await expect(page.getByTestId('risk-cause')).not.toBeEmpty();
    await shot('risk-wizard-statement');
    await page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await expect(page.getByTestId('risk-matrix-inherent-4-5')).toBeVisible();
    await page.getByTestId('risk-matrix-inherent-4-5').click();
    await expect(page.getByTestId('risk-matrix-inherent-4-5')).toHaveAttribute('aria-checked', 'true');
    await expect(page.getByTestId('risk-wizard-inherent-chip')).toContainText('20');
    await shot('risk-wizard-matrix');

    // ---- 3. Risk page: header chips inherent → residual → target and the tabs ------------------------------------------------------
    const tab = async (name: string) => {
      await page.goto(`/risk/risks/${payment.id}?tab=${name}`);
      await expect(page.getByTestId('risk-page')).toBeVisible();
      await expect(page.getByTestId(`risk-tab-${name}`)).toHaveAttribute('data-state', 'active');
    };
    await tab('overview');
    await expect(page.getByTestId('risk-number')).toHaveText(payment.number);
    await expect(page.getByTestId('risk-ratings')).toBeVisible();
    await shot('risk-page-overview');
    await tab('assessment');
    await expect(page.getByTestId('risk-assessment-chart')).toBeVisible();
    await shot('risk-page-assessment', { fullPage: true });
    await tab('controls');
    await expect(page.getByTestId('risk-control-row')).toHaveCount(1);
    await shot('risk-page-controls');
    await tab('actions');
    await expect(page.getByTestId('risk-action-row')).toHaveCount(1);
    await shot('risk-page-actions');
    await tab('kris');
    await expect(page.getByTestId('risk-kri-row')).toHaveCount(1);
    await expect(page.getByTestId('risk-kri-level')).toContainText(r(language, 'kriLevel.breach'));
    await shot('kri-breach');
    await tab('activity');
    await shot('risk-page-activity');

    // ---- 4. Dashboard: inherent vs residual heat-maps; clicking a cell lists its risks -------------------------------------------
    await page.goto('/risk');
    await expect(page.getByTestId('risk-heatmap-inherent')).toBeVisible();
    await expect(page.getByTestId('risk-heatmap-residual')).toBeVisible();
    await page.getByTestId('risk-heat-inherent-5-5').click();
    await expect(page.getByTestId('risk-heat-cell-list')).toContainText(payment.number);
    await shot('dashboard-heatmaps');
    await shot('dashboard', { fullPage: true });

    // ---- 5. Top-10 view of the register (the Risk Office ranked "Vendor delay" first) ---------------------------------------------
    await page.goto('/risk/risks?view=top10');
    await expect(page.getByTestId('risk-register-table')).toBeVisible();
    await expect(page.getByTestId('risk-register-table')).toContainText(vendor.number);
    await shot('top10');
    await page.goto(`/risk/risks?search=${tag}`);
    await expect(page.getByTestId('risk-row')).toHaveCount(4);
    await shot('register');

    // ---- 6. Project Risks: live from Portfolio, read-only; each row opens the risk inside its project ------------------------------
    await page.goto('/risk/project-risks');
    await expect(page.getByTestId('risk-project-risks-table')).toBeVisible();
    await page.getByTestId('risk-project-risks-search').fill(tag);
    await expect(page.getByTestId('risk-project-risk-row')).toHaveCount(1);
    // The Portfolio category is shown with its label in the user's language, not the raw code.
    await expect(page.getByTestId('risk-project-risk-row')).toContainText(arabic ? 'المورّد' : 'Vendor');
    await expect(page.getByTestId('risk-project-risk-row')).not.toContainText('vendor');
    await shot('project-risks');

    // ---- 7. The generated Risk Assessment Mechanism page --------------------------------------------------------------------------
    await page.goto('/risk/mechanism');
    await expect(page.getByTestId('risk-mechanism-matrix')).toBeVisible();
    await shot('mechanism', { fullPage: true });

    // ---- 8. Reports: the register as a bilingual PDF (rendered to PNG for review) --------------------------------------------------
    await page.goto('/risk/reports');
    await expect(page.getByTestId('risk-report-register-pdf')).toBeVisible();
    await shot('reports');
    const file = await adminApi.get('/api/v1/risk/reports/register', { params: { format: 'pdf', language } });
    expect(file.ok(), await file.text()).toBe(true);
    const dir = mkdtempSync(join(tmpdir(), 'risk-pdf-'));
    writeFileSync(join(dir, 'register.pdf'), await file.body());
    execFileSync('pdftoppm', [
      '-png',
      '-r',
      '80',
      '-f',
      '1',
      '-l',
      '1',
      join(dir, 'register.pdf'),
      join(dir, 'page'),
    ]);
    renameSync(join(dir, 'page-1.png'), screenshotPath(language, 'report-register-pdf'));

    await ahmed.dispose();
    expect(fraud.status).toBe('active');
  });
});
