import { expect, test, tr } from './fixtures';

test.use({ signedInAs: 'mariam' });

const header = 'employee_no,name_ar,name_en,email,unit_code,status,source_system,external_id';

/**
 * External references (SPEC-ADDENDUM §B, ADR 0164 / 0165): a source system created in Settings, an import with external IDs that
 * updates instead of duplicating on a re-run, and the record page with the External references section and the origin badge.
 */
test('source system, import with external IDs (re-run updates) and the record page references', async ({
  page,
  language,
  shot,
}) => {
  const i = (key: string, params?: Record<string, string | number>) =>
    tr(language, `integration:${key}`, params);
  const tag = `${language}${Date.now().toString(36)}`.toLowerCase();
  const source = `crm-${tag}`;
  const sourceName = language === 'ar' ? `نظام العملاء ${tag}` : `CRM ${tag}`;

  // 1. Settings → Integrations → Source systems.
  await page.goto('/settings/integrations/source-systems');
  await expect(page.getByRole('heading', { level: 1, name: i('sources.title') })).toBeVisible();
  await page.getByRole('button', { name: i('sources.create') }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(i('fields.code')).fill(source);
  await dialog.getByRole('combobox', { name: i('fields.type') }).click();
  await page.getByRole('option', { name: language === 'ar' ? 'نظام آخر' : 'Custom' }).click();
  await dialog.getByLabel(i('fields.nameAr')).fill(`نظام العملاء ${tag}`);
  await dialog.getByLabel(i('fields.nameEn')).fill(`CRM ${tag}`);
  await shot('external-refs-source-editor');
  await dialog.getByRole('button', { name: tr(language, 'common:actions.save') }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByTestId(`source-${source}`)).toBeVisible();
  await shot('external-refs-source-systems', { fullPage: true });

  // 2. Import employees with Source system + External ID, then re-import: updated, not duplicated.
  const csv = (name: string) => ({
    name: `employees-${tag}.csv`,
    mimeType: 'text/csv',
    buffer: Buffer.from(
      [
        header,
        `X-${tag},${language === 'ar' ? 'سلمى' : 'Salma'} ${name},Salma ${name},salma.${tag}@strategy.local,DEP-FIN,active,${source},C-${tag}`,
      ].join('\n'),
    ),
  });
  const next = () => page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
  const importFile = async (name: string) => {
    await page.goto('/org/import');
    await page.getByRole('main').getByRole('combobox').click(); // the top bar has the global search combobox
    await page.getByRole('option', { name: tr(language, 'import:kinds.employees') }).click();
    await next();
    await page.getByLabel(tr(language, 'import:drop')).setInputFiles(csv(name));
    await next();
    await expect(page.getByText(tr(language, 'import:valid', { count: 1 }))).toBeVisible();
    await page.getByRole('button', { name: tr(language, 'import:commit') }).click();
  };
  await importFile(tag);
  await expect(page.getByText(tr(language, 'import:done', { created: 1, updated: 0 }))).toBeVisible();
  await importFile(`${tag} B`);
  await expect(page.getByText(tr(language, 'import:done', { created: 0, updated: 1 }))).toBeVisible();
  await shot('external-refs-import-updated');

  await page.goto(`/org/employees?search=${encodeURIComponent(`x-${tag}`)}`);
  const link = page.getByRole('link', { name: new RegExp(`${tag} B`, 'u') });
  await expect(link).toHaveCount(1);

  // 3. The record page: origin badge in the header and the External references section.
  await link.click();
  await expect(page.getByTestId('origin-badge')).toContainText(i('origin', { source: sourceName }));
  const section = page.getByTestId('external-references');
  await expect(section.getByRole('heading', { name: i('section.title') })).toBeVisible();
  await expect(section.getByTestId('external-id')).toHaveText(`C-${tag}`);
  await shot('external-refs-record-page', { fullPage: true });
});
