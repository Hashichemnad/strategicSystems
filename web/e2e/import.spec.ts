import { expect, test, tr } from './fixtures';

test.use({ signedInAs: 'mariam' });

const header = 'employee_no,name_ar,name_en,email,unit_code,manager_employee_no,status,join_date,create_user';

test('employees are imported from CSV only after the validation report is clean', async ({
  page,
  language,
  shot,
}) => {
  const tag = `${language}${Date.now().toString(36)}`.toUpperCase();
  const rows = (unitOfSecond: string) =>
    [
      header,
      `I1-${tag},ليلى حسن ${tag},Layla Hassan ${tag},layla.${tag}@strategy.local,DEP-STR,,active,2024-02-01,N`,
      `I2-${tag},عمر سالم ${tag},Omar Salem ${tag},omar.${tag}@strategy.local,${unitOfSecond},I1-${tag},active,2024-03-01,N`,
    ].join('\n');
  const csv = (content: string) => ({
    name: `employees-${tag}.csv`,
    mimeType: 'text/csv',
    buffer: Buffer.from(content),
  });
  const next = () => page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();

  await page.goto('/org/import');
  await page.getByRole('combobox').click();
  await page.getByRole('option', { name: tr(language, 'import:kinds.employees') }).click();
  const download = page.waitForEvent('download');
  await page.getByRole('link', { name: tr(language, 'import:downloadTemplate') }).click();
  expect((await download).suggestedFilename()).toMatch(/\.xlsx$/u);
  await next();

  // A row pointing at a unit that does not exist: nothing is saved, the report says where.
  await page.getByLabel(tr(language, 'import:drop')).setInputFiles(csv(rows('NO-SUCH-UNIT')));
  await next();
  await expect(page.getByRole('alert')).toContainText(tr(language, 'import:invalid', { count: 1 }));
  const problem = page.getByRole('row').filter({ hasText: 'unit_code' });
  await expect(problem).toContainText('3');
  await shot('import-validation-report');

  await page.getByRole('button', { name: tr(language, 'common:actions.back') }).click();
  await page.getByLabel(tr(language, 'import:drop')).setInputFiles(csv(rows('DEP-FIN')));
  await next();
  await expect(page.getByText(tr(language, 'import:valid', { count: 2 }))).toBeVisible();
  await page.getByRole('button', { name: tr(language, 'import:commit') }).click();
  await expect(page.getByText(tr(language, 'import:done', { created: 2, updated: 0 }))).toBeVisible();
  await shot('import-done');

  await page.goto(`/org/employees?search=${encodeURIComponent(tag.toLowerCase())}`);
  await expect(
    page.getByRole('link', { name: language === 'ar' ? `عمر سالم ${tag}` : `Omar Salem ${tag}` }),
  ).toBeVisible();
});
