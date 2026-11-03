import type { APIRequestContext, Browser, Page } from '@playwright/test';
import {
  apiPost,
  apiPut,
  expect,
  savePreferences,
  screenshotPath,
  signInViaApi,
  test,
  tr,
  unitIdByCode,
  type Language,
} from './fixtures';

/** Phase 12 — Performance Management (docs/design/kpi.md): screenshots go to docs/screenshots/phase-12 (E2E_SCREENSHOTS=phase-12). */

const k = (language: Language, key: string, params?: Record<string, string | number>) =>
  tr(language, `kpi:${key}`, params);

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

async function employeeIdByNumber(api: APIRequestContext, number: string): Promise<string> {
  const page = await json<{ items: { id: string; employeeNo: string }[] }>(
    await api.get('/api/v1/org/employees', { params: { search: number } }),
  );
  const employee = page.items.find((e) => e.employeeNo === number);
  if (!employee) throw new Error(`Employee ${number} not found`);
  return employee.id;
}

async function antiforgery(api: APIRequestContext): Promise<Record<string, string>> {
  const { token, headerName } = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [headerName]: token, 'X-Client': 'spa' };
}

/** Evidence: a small text export uploaded by the owner to the result. */
async function uploadEvidence(api: APIRequestContext, resultId: string, text: string) {
  const response = await api.post(`/api/v1/files/owners/kpi.result/${resultId}`, {
    headers: await antiforgery(api),
    multipart: { file: { name: 'evidence.txt', mimeType: 'text/plain', buffer: Buffer.from(text) } },
  });
  expect(response.ok(), await response.text()).toBe(true);
}

async function pageAs(browser: Browser, baseURL: string, language: Language): Promise<Page> {
  const context = await browser.newContext({ baseURL, viewport: { width: 1366, height: 900 } });
  const page = await context.newPage();
  await page.addInitScript((lang) => {
    localStorage.setItem('strategy.language', lang);
    localStorage.setItem('strategy.theme', 'light');
  }, language);
  await signInViaApi(page, 'ahmed');
  await savePreferences(page, language);
  return page;
}

interface Plan {
  id: string;
  cardId: string;
  status: string;
}

interface PlanDetail {
  periods: { resultId: string; periodCode: string; status: string; target: number | null }[];
}

test.describe('performance management', () => {
  test.use({ signedInAs: 'mariam' });

  test('cards, a new measuring year, pending updates, submit with evidence, approval with the checklist, bulk approval, scorecard, strategy map and dashboard', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    browser,
    baseURL,
  }) => {
    test.setTimeout(300_000);
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const year = new Date().getFullYear() + 1; // the wizard proposes next year
    const ahmed = await employeeIdByNumber(adminApi, 'E0002');
    const department = await unitIdByCode(adminApi, 'DEP-STR');
    const ahmedApi = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmedApi, 'ahmed');

    // Strategy hierarchy: vision → two perspectives → objectives (Settings → KPI in the UI; API here).
    const element = async (kind: string, code: string, ar: string, en: string, parentId: string | null) =>
      (
        await json<{ id: string }>(
          await apiPost(adminApi, '/api/v1/kpi/strategy/elements', {
            kind,
            code: `${code}-${tag}`,
            nameAr: ar,
            nameEn: en,
            parentId,
            sort: 1,
            active: true,
            weight: null,
          }),
        )
      ).id;
    const vision = await element('vision', 'V', 'حكومة رائدة 2030', 'A leading government 2030', null);
    const customers = await element('perspective', 'P1', 'المتعاملون', 'Customers', vision);
    const processes = await element('perspective', 'P2', 'العمليات الداخلية', 'Internal processes', vision);
    const happiness = await element('objective', 'O1', 'إسعاد المتعاملين', 'Delight customers', customers);
    const digital = await element(
      'objective',
      'O2',
      'تحول رقمي شامل',
      'Full digital transformation',
      processes,
    );
    const speed = await element('objective', 'O3', 'تسريع الإجراءات', 'Faster procedures', processes);

    // KPI cards (Strategy Office); Ahmed is responsible for them.
    const card = async (
      ar: string,
      en: string,
      objectiveId: string,
      extra: Record<string, unknown>,
    ): Promise<{ id: string; code: string }> => {
      const response = await apiPost(adminApi, '/api/v1/kpi/cards', {
        values: {
          nameAr: ar,
          nameEn: en,
          measuringEquation: extra.equation,
          nature: 'quantitative',
          measuringMethodCode: extra.method ?? 'average',
          unitCode: extra.unit ?? 'percentage',
          measuringPeriod: 'quarterly',
          direction: extra.direction ?? 'increase',
          targetType: 'single',
          typeCode: 'strategic',
          objectiveId,
          departmentUnitId: department,
          responsibleUnitId: department,
          responsibleEmployeeId: ahmed,
          level: 'department',
          isGlobalStar: Boolean(extra.star),
          sourceCodes: [],
          gemCriteriaCodes: [],
          dataElements: [],
          scale: [],
        },
      });
      expect(response.status(), await response.text()).toBe(201);
      return (await response.json()) as { id: string; code: string };
    };
    const satisfaction = await card(`رضا المتعاملين ${tag}`, `Customer satisfaction ${tag}`, happiness, {
      equation: 'satisfied ÷ surveyed × 100',
      star: true,
    });
    const transactions = await card(`المعاملات الرقمية ${tag}`, `Digital transactions ${tag}`, digital, {
      equation: 'digital ÷ all transactions × 100',
    });
    const days = await card(`زمن إنجاز المعاملة ${tag}`, `Processing time ${tag}`, speed, {
      equation: 'average working days per transaction',
      unit: 'days',
      direction: 'decrease',
    });
    const targets = new Map([
      [satisfaction.code, '90'],
      [transactions.code, '80'],
      [days.code, '5'],
    ]);

    await page.goto('/kpi/cards');
    await expect(page.getByTestId('kpi-cards-table')).toBeVisible();
    await page.getByTestId('kpi-cards-search').fill(tag);
    await expect(page.getByTestId('kpi-card-row')).toHaveCount(3);
    await shot('kpi-cards');

    // New measuring year wizard: untick nothing of ours, give the targets, create.
    await page.goto('/kpi/new-year');
    await expect(page.getByTestId('kpi-new-year-step-year')).toBeVisible();
    await page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await expect(page.getByTestId('kpi-new-year-step-cards')).toBeVisible();
    const rows = page.getByTestId('kpi-new-year-row');
    for (let i = 0; i < (await rows.count()); i++) {
      const row = rows.nth(i);
      const text = await row.innerText();
      const code = [...targets.keys()].find((c) => text.includes(c));
      const checkbox = row.getByRole('checkbox');
      if (code) await row.getByTestId('kpi-new-year-target').fill(targets.get(code) ?? '');
      else if ((await checkbox.isEnabled()) && (await checkbox.isChecked())) await checkbox.uncheck();
    }
    await shot('kpi-new-year');
    await page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await expect(page.getByTestId('kpi-new-year-step-review')).toBeVisible();
    await page.getByRole('button', { name: k(language, 'newYear.submit') }).click();
    await expect(page.getByTestId('kpi-year-plans')).toBeVisible();

    // Windows of the year (Q-K2 defaults are editable): open since yesterday, due in two weeks, so the demo can submit today.
    const today = new Date();
    const iso = (d: Date) => d.toISOString().slice(0, 10);
    const opens = iso(new Date(today.getTime() - 86_400_000));
    const due = iso(new Date(today.getTime() + 14 * 86_400_000));
    const windows = await apiPut(adminApi, `/api/v1/kpi/years/${year}/windows`, {
      windows: ['q1', 'q2', 'q3', 'q4', 'h1', 'h2', 'fy', 'ty'].map((periodCode) => ({
        periodCode,
        opensOn: opens,
        dueOn: due,
      })),
    });
    expect(windows.ok(), await windows.text()).toBe(true);

    // Confirm our three plans (bulk confirm in the year's plan list).
    const planRows = page.getByTestId('kpi-plan-row').filter({ hasText: tag });
    await expect(planRows).toHaveCount(3);
    for (let i = 0; i < 3; i++) await planRows.nth(i).getByRole('checkbox').check();
    await page.getByTestId('kpi-confirm-plans').click();
    const plans = await json<Plan[]>(await adminApi.get(`/api/v1/kpi/plans?year=${year}`));
    await expect
      .poll(async () =>
        (await json<Plan[]>(await adminApi.get(`/api/v1/kpi/plans?year=${year}`)))
          .filter((p) => [satisfaction.id, transactions.id, days.id].includes(p.cardId))
          .every((p) => p.status === 'confirmed'),
      )
      .toBe(true);
    const detail = async (cardId: string) =>
      json<PlanDetail>(
        await adminApi.get(`/api/v1/kpi/plans/${plans.find((p) => p.cardId === cardId)?.id ?? 'missing'}`),
      );

    // Ahmed: My Pending Updates, then the submit wizard with evidence.
    const ahmedPage = await pageAs(browser, baseURL ?? '', language);
    await ahmedPage.goto('/kpi/pending');
    await expect(ahmedPage.getByTestId('kpi-pending-table')).toContainText(satisfaction.code);
    await ahmedPage.screenshot({ path: screenshotPath(language, 'kpi-pending-updates'), fullPage: true });

    const satisfactionQ1 =
      (await detail(satisfaction.id)).periods.find((p) => p.periodCode === 'q1')?.resultId ?? 'missing';
    await ahmedPage.goto(`/kpi/results/${satisfactionQ1}`);
    await expect(ahmedPage.getByTestId('kpi-submit-wizard')).toBeVisible();
    await ahmedPage.getByTestId('kpi-achieve').fill('84');
    await expect(ahmedPage.getByTestId('kpi-preview')).toBeVisible();
    await ahmedPage.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await expect(ahmedPage.getByTestId('kpi-step-narrative')).toBeVisible();
    await ahmedPage
      .getByTestId('kpi-causality')
      .fill(
        language === 'ar'
          ? 'انخفاض عدد الاستبيانات في الربع الأول.'
          : 'Fewer surveys returned in the first quarter.',
      );
    await ahmedPage
      .getByTestId('kpi-corrective-actions')
      .fill(language === 'ar' ? 'إرسال الاستبيان عبر الرسائل النصية.' : 'Send the survey by SMS as well.');
    await ahmedPage.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await expect(ahmedPage.getByTestId('kpi-step-evidence')).toBeVisible();
    await ahmedPage
      .getByTestId('kpi-step-evidence')
      .getByRole('button', { name: tr(language, 'files:upload') })
      .click();
    const upload = ahmedPage.getByRole('dialog');
    await upload.locator('input[type="file"]').setInputFiles({
      name: 'survey-q1.txt',
      mimeType: 'text/plain',
      buffer: Buffer.from('Customer survey Q1\nSatisfied customers: 84 %'),
    });
    await upload.getByRole('button', { name: tr(language, 'files:upload') }).click();
    await expect(upload).toBeHidden();
    await expect(ahmedPage.getByTestId('kpi-step-evidence')).toContainText('survey-q1.txt');
    await ahmedPage.screenshot({ path: screenshotPath(language, 'kpi-submit-result'), fullPage: true });
    await ahmedPage.getByRole('button', { name: k(language, 'submit.submit') }).click();
    await expect(ahmedPage.getByTestId('kpi-result-page')).toContainText(
      k(language, 'resultStatus.pending_approval'),
    );

    // Mariam (Strategy Office): review with the verification checklist, approve.
    await page.goto(`/kpi/results/${satisfactionQ1}?tab=review`);
    await expect(page.getByTestId('kpi-review-panel')).toBeVisible();
    const items = page.getByTestId('kpi-checklist-item');
    await expect(items).toHaveCount(5);
    for (let i = 0; i < (await items.count()); i++) await items.nth(i).check();
    await page
      .getByTestId('kpi-review-comment')
      .fill(language === 'ar' ? 'الدليل مطابق.' : 'Evidence matches.');
    await shot('kpi-approval-checklist');
    await page.getByTestId('kpi-decide-approve').click();
    await expect(page.getByTestId('kpi-result-page')).toContainText(k(language, 'resultStatus.approved'));

    // Ahmed submits the other Q1 results (API); Mariam bulk-approves them.
    const submit = async (cardId: string, period: string, value: number, causality?: string) => {
      const resultId =
        (await detail(cardId)).periods.find((p) => p.periodCode === period)?.resultId ?? 'missing';
      await uploadEvidence(ahmedApi, resultId, `Report ${period}: ${value}`);
      const current = await json<{ version: number }>(await ahmedApi.get(`/api/v1/kpi/results/${resultId}`));
      const response = await ahmedApi.post(`/api/v1/kpi/results/${resultId}/submit`, {
        headers: await antiforgery(ahmedApi),
        data: {
          entry: {
            achieve: value,
            causality: causality ?? null,
            correctiveActions: causality ? 'Action plan agreed with the department' : null,
            notes: null,
            notApplicable: false,
          },
          version: current.version,
        },
      });
      expect(response.ok(), await response.text()).toBe(true);
      return resultId;
    };
    await submit(transactions.id, 'q1', 92);
    await submit(days.id, 'q1', 6.5, 'Peak season');
    await submit(transactions.id, 'q2', 88);

    await page.goto('/kpi/approvals?tab=bulk');
    const bulk = page.getByTestId('kpi-bulk');
    await bulk.getByLabel(k(language, 'common.year')).click();
    await page.getByRole('option', { name: String(year), exact: true }).click();
    await bulk.getByLabel(k(language, 'approvals.department')).click();
    await page
      .getByRole('option', {
        name: language === 'ar' ? 'إدارة الاستراتيجية والأداء' : 'Strategy & Performance Department',
      })
      .click();
    await expect(page.getByTestId('kpi-bulk-table')).toBeVisible();
    const bulkRows = page.getByTestId('kpi-bulk-table').getByTestId('kpi-result-row');
    for (let i = 0; i < (await bulkRows.count()); i++) {
      const checkbox = bulkRows.nth(i).getByRole('checkbox');
      const label = (await checkbox.getAttribute('aria-label')) ?? '';
      // Only this run's results; leave the Q2 transactions result for later: untick it.
      if (!label.endsWith(' q1') || ![transactions.code, days.code].some((c) => label.startsWith(c)))
        await checkbox.uncheck();
    }
    await page.getByTestId('kpi-bulk-confirm').check();
    await shot('kpi-bulk-approval');
    await page.getByTestId('kpi-bulk-update').click();
    await expect(page.getByTestId('kpi-bulk')).toContainText(
      k(language, 'bulk.outcome', { approved: 2, failed: 0 }),
    );

    // Scorecard, strategy map and dashboard use capped performance.
    const pickYear = async () => {
      await page.getByLabel(k(language, 'common.year')).first().click();
      await page.getByRole('option', { name: String(year), exact: true }).click();
    };
    await page.goto('/kpi/scorecard');
    await pickYear();
    await expect(page.getByTestId('kpi-scorecard-tree')).toContainText(
      language === 'ar' ? 'تحول رقمي شامل' : 'Full digital transformation',
    );
    await shot('kpi-scorecard');
    await page.goto('/kpi/strategy-map');
    await pickYear();
    await expect(page.getByTestId('kpi-map')).toContainText(
      language === 'ar' ? 'تحول رقمي شامل' : 'Full digital transformation',
    );
    await shot('kpi-strategy-map');
    await page.goto('/kpi');
    await pickYear();
    await expect(page.getByTestId('kpi-tiles')).toBeVisible();
    await shot('kpi-dashboard');

    // The card page shows the yearly performance (actual and capped).
    await page.goto(`/kpi/cards/${transactions.id}?tab=performance`);
    await expect(page.getByTestId('kpi-performance-chart')).toBeVisible();
    await shot('kpi-card-performance');

    await ahmedPage.context().close();
    await ahmedApi.dispose();
  });
});
