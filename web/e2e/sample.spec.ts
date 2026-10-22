import { expect, test, text } from './fixtures';

test('sample module round trip: create, find, edit and deactivate', async ({ page, language, shot }) => {
  const t = text[language];
  const tag = `${language}-${Date.now().toString(36)}`;
  await page.goto('/sample');
  await expect(page.getByRole('heading', { level: 1, name: t.sample })).toBeVisible();
  await expect(page.locator('[aria-busy="true"]')).toHaveCount(0); // list loaded (rows or empty state)

  // Client-side validation: at least one name, message in the user's language.
  await page.getByRole('button', { name: t.newItem }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: t.create }).click();
  await expect(dialog.getByRole('alert')).toHaveText(t.required);

  await dialog
    .getByLabel(language === 'ar' ? 'الاسم (عربي)' : 'Name (Arabic)')
    .fill(`خطة التحول الرقمي ${tag}`);
  await dialog
    .getByLabel(language === 'ar' ? 'الاسم (إنجليزي)' : 'Name (English)')
    .fill(`Digital transformation plan ${tag}`);
  await dialog
    .getByLabel(language === 'ar' ? 'الوصف (عربي)' : 'Description (Arabic)')
    .fill('وصف تجريبي للبند');
  await shot('sample-create-dialog');
  await dialog.getByRole('button', { name: t.create }).click();
  await expect(dialog).toBeHidden();
  await expect(page.getByText(t.created, { exact: true })).toBeVisible();

  // Search goes through the URL (shareable view) and the server.
  await page.getByRole('searchbox', { name: t.search }).fill(tag);
  await expect(page).toHaveURL(new RegExp(`search=${tag}`));
  const row = page.getByTestId('sample-row');
  await expect(row).toHaveCount(1);
  await expect(row).toContainText(
    language === 'ar' ? `خطة التحول الرقمي ${tag}` : `Digital transformation plan ${tag}`,
  );

  await row.getByRole('button', { name: t.edit }).click();
  await page
    .getByRole('dialog')
    .getByLabel(language === 'ar' ? 'الاسم (إنجليزي)' : 'Name (English)')
    .fill(`Digital plan v2 ${tag}`);
  await page.getByRole('dialog').getByRole('button', { name: t.save }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(row).toContainText(`Digital plan v2 ${tag}`);

  await row.getByRole('button', { name: t.deactivate }).click();
  await page.getByRole('dialog').getByRole('button', { name: t.deactivate }).click();
  await expect(row.getByText(t.inactive)).toBeVisible();

  await page.getByRole('searchbox', { name: t.search }).fill('');
  await expect(page).not.toHaveURL(/search=/);
  await expect.poll(() => page.getByTestId('sample-row').count()).toBeGreaterThan(1);
  await shot('sample-list');
});
