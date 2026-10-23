import { apiPost, expect, test, tr, unitIdByCode, type Language } from './fixtures';
import type { Page } from '@playwright/test';

test.use({ signedInAs: 'mariam' });

async function createUnit(
  page: Page,
  unit: { code: string; nameAr: string; nameEn: string; levelCode: string; parentId: string },
) {
  const response = await apiPost(page, '/api/v1/org/units', { ...unit, effectiveFrom: '2025-01-01' });
  expect(response.status()).toBe(201);
  return ((await response.json()) as { id: string }).id;
}

const chartBox = (page: Page, name: string) => page.locator('.org-chart-box').filter({ hasText: name });

test('a four-level structure is re-parented and the chart still shows the old structure for past dates', async ({
  page,
  language,
  shot,
}) => {
  const tag = `${language}${Date.now().toString(36)}`.toUpperCase();
  const name = (en: string, ar: string) => (language === 'ar' ? `${ar} ${tag}` : `${en} ${tag}`);
  const names = (en: string, ar: string) => ({ nameEn: `${en} ${tag}`, nameAr: `${ar} ${tag}` });

  // Organisation › sector › two departments › a section (created with history from 2025).
  const sector = await createUnit(page, {
    code: `S-${tag}`,
    ...names('Planning Sector', 'قطاع التخطيط'),
    levelCode: 'sector',
    parentId: await unitIdByCode(page, 'ORG'),
  });
  const first = await createUnit(page, {
    code: `D1-${tag}`,
    ...names('Studies Department', 'إدارة الدراسات'),
    levelCode: 'department',
    parentId: sector,
  });
  await createUnit(page, {
    code: `D2-${tag}`,
    ...names('Excellence Department', 'إدارة التميز'),
    levelCode: 'department',
    parentId: sector,
  });
  const section = await createUnit(page, {
    code: `X-${tag}`,
    ...names('Research Section', 'قسم البحوث'),
    levelCode: 'section',
    parentId: first,
  });

  // Move the section to the other department from today.
  await page.goto(`/org/units?unit=${section}`);
  const details = page.locator('main');
  await expect(
    details.getByRole('heading', { level: 2, name: name('Research Section', 'قسم البحوث') }),
  ).toBeVisible();
  await expect(details.getByRole('navigation', { name: tr(language, 'org:units.path') })).toContainText(
    name('Studies Department', 'إدارة الدراسات'),
  );
  await details.getByRole('button', { name: tr(language, 'org:units.move'), exact: true }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(tr(language, 'org:units.newParent')).click();
  await page.getByRole('option', { name: name('Excellence Department', 'إدارة التميز') }).click();
  await shot('org-move-unit');
  await dialog.getByRole('button', { name: tr(language, 'org:units.move'), exact: true }).click();
  await expect(dialog).toBeHidden();
  await expect(details.getByRole('navigation', { name: tr(language, 'org:units.path') })).toContainText(
    name('Excellence Department', 'إدارة التميز'),
  );

  // History keeps both placements.
  await details.getByRole('tab', { name: tr(language, 'org:units.tabs.history') }).click();
  const versions = details
    .getByRole('list', { name: tr(language, 'org:units.tabs.history') })
    .getByRole('listitem');
  await expect(versions).toHaveCount(2);
  await expect(versions.last()).toContainText(name('Studies Department', 'إدارة الدراسات'));
  await expect(versions.first()).toContainText(name('Excellence Department', 'إدارة التميز'));
  await shot('org-unit-history');

  await expectChart(
    page,
    language,
    undefined,
    name('Excellence Department', 'إدارة التميز'),
    name('Studies Department', 'إدارة الدراسات'),
  );
  await shot('org-chart-today');
  await expectChart(
    page,
    language,
    '2025-06-01',
    name('Studies Department', 'إدارة الدراسات'),
    name('Excellence Department', 'إدارة التميز'),
  );
  await shot('org-chart-as-of');
});

/** The section shows under `parent` (its box offers to expand one unit) and not under `other`. */
async function expectChart(
  page: Page,
  language: Language,
  asOf: string | undefined,
  parent: string,
  other: string,
) {
  await page.goto(asOf ? `/org/chart?asOf=${asOf}` : '/org/chart');
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'org:chart.title') })).toBeVisible();
  await expect(chartBox(page, parent)).toContainText(tr(language, 'common:tree.expand', { count: 1 }));
  await expect(chartBox(page, other)).toBeVisible();
  await expect(chartBox(page, other).getByRole('button')).toHaveCount(0);
}
