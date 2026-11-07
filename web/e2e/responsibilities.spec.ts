import type { APIRequestContext } from '@playwright/test';
import { apiPost, expect, test, tr, unitIdByCode } from './fixtures';

/**
 * Phase 19, stream D2 — System → Settings → Responsibilities (SPEC-ADDENDUM §D2, ADR 0168): from KPI settings to the Responsibilities
 * page, filter by the current person, select two KPI cards, preview, reassign with a reason and read the result.
 * Screenshots (docs/screenshots/$E2E_SCREENSHOTS): <lang>-responsibilities-list, -preview, -result, -mobile.
 */

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

test.describe('responsibilities', () => {
  test.use({ signedInAs: 'mariam' });

  test('KPI cards move to a new responsible person with a preview, a reason and a result summary', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    test.setTimeout(180_000);
    const tag = `R${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const ahmed = await employeeIdByNumber(adminApi, 'E0002');
    const department = await unitIdByCode(adminApi, 'DEP-STR');
    const card = async (en: string, ar: string) => {
      const response = await apiPost(adminApi, '/api/v1/kpi/cards', {
        values: {
          nameAr: `${ar} ${tag}`,
          nameEn: `${en} ${tag}`,
          measuringEquation: 'count',
          nature: 'quantitative',
          measuringMethodCode: 'cumulative',
          unitCode: 'number',
          measuringPeriod: 'quarterly',
          direction: 'increase',
          targetType: 'single',
          typeCode: 'strategic',
          departmentUnitId: department,
          responsibleUnitId: department,
          responsibleEmployeeId: ahmed,
          level: 'department',
          sourceCodes: [],
          gemCriteriaCodes: [],
          dataElements: [],
          scale: [],
        },
      });
      expect(response.status(), await response.text()).toBe(201);
      return (await response.json()) as { id: string; code: string };
    };
    const first = await card('Permits issued', 'التصاريح الصادرة');
    const second = await card('Licences renewed', 'التراخيص المجددة');
    const third = await card('Inspections done', 'الزيارات التفتيشية');
    const t = (key: string, params?: Record<string, string | number>) =>
      tr(language, `responsibilities:${key}`, params);

    // KPI → Settings → Responsibilities.
    await page.goto('/settings/kpi');
    await page.getByTestId('responsibilities-link-kpi').click();
    await expect(page).toHaveURL(/\/settings\/responsibilities\?system=kpi/);
    await expect(page.getByRole('heading', { level: 1, name: t('title') })).toBeVisible();

    // Filter by the current person (Ahmed) and the tag of this run.
    const person = page.getByRole('combobox', { name: t('filters.person') });
    await person.fill('E0002');
    await page.getByRole('option').filter({ hasText: 'E0002' }).first().click();
    await page.getByLabel(t('filters.text')).fill(tag);
    const table = page.getByTestId('responsibilities-table');
    await expect(table.getByRole('row')).toHaveCount(4); // header + 3 cards
    await table.getByRole('checkbox', { name: t('table.select', { record: first.code }) }).check();
    await table.getByRole('checkbox', { name: t('table.select', { record: second.code }) }).check();
    await expect(page.getByText(t('table.selected_other', { count: 2 }))).toBeVisible();
    await shot('responsibilities-list');

    // New person (Fatima), reason, preview: both allowed.
    const target = page.getByRole('combobox', { name: t('change.person') });
    await target.fill('E0003');
    await page.getByRole('option').filter({ hasText: 'E0003' }).first().click();
    await page.getByLabel(t('change.reason')).fill(`Portfolio rebalanced ${tag}`);
    await page.getByTestId('responsibilities-preview').click();
    const outcomes = page.getByTestId('responsibilities-outcomes');
    await expect(outcomes.getByText(t('outcomes.allowed'))).toHaveCount(2);
    await shot('responsibilities-preview');

    await page.getByTestId('responsibilities-confirm').click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: t('change.confirm_other', { count: 2 }) })
      .click();
    const result = page.getByTestId('responsibilities-result');
    await expect(
      result.getByText(t('result.summary', { moved: 2, changeRequests: 0, skipped: 0 })),
    ).toBeVisible();
    await expect(result.getByText(t('outcomes.moved'))).toHaveCount(2);
    await shot('responsibilities-result');

    // Only the two selected cards moved.
    const fatima = await employeeIdByNumber(adminApi, 'E0003');
    const list = await json<{ items: { id: string; responsible: { id: string } | null }[] }>(
      await adminApi.get(`/api/v1/access/systems/kpi/responsibilities/card-person?text=${tag}`),
    );
    expect(list.items.find((r) => r.id === first.id)?.responsible?.id).toBe(fatima);
    expect(list.items.find((r) => r.id === second.id)?.responsible?.id).toBe(fatima);
    expect(list.items.find((r) => r.id === third.id)?.responsible?.id).toBe(ahmed);

    // Phones (360 px): the page stays usable without horizontal page scroll.
    await page.getByRole('button', { name: t('result.again') }).click();
    await page.setViewportSize({ width: 360, height: 740 });
    await expect(page.getByTestId('responsibilities-table')).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth + 1)).toBe(
      true,
    );
    await shot('responsibilities-mobile');
  });
});
