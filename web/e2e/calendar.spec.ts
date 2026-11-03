import { expect, test, tr } from './fixtures';

test.describe('business hours and holidays', () => {
  test.use({ signedInAs: 'mariam' });

  test('Settings → Business hours: the default week, the editor and units with their own hours', async ({
    page,
    language,
    shot,
  }) => {
    const c = (key: string, params?: Record<string, string | number>) =>
      tr(language, `calendar:${key}`, params);
    await page.goto('/settings/business-hours');
    await expect(page.getByRole('heading', { level: 1, name: c('businessHours.title') })).toBeVisible();
    const standard = page.getByRole('region', {
      name: language === 'ar' ? 'ساعات العمل الرسمية' : 'Standard business hours',
    });
    await expect(standard.getByText(c('businessHours.default'))).toBeVisible();
    await expect(standard.getByText('07:30–15:30').first()).toBeVisible();
    await expect(page.getByRole('region', { name: c('businessHours.units.title') })).toBeVisible();
    await shot('settings-business-hours', { fullPage: true });

    // The editor: an invalid day is explained in the user's language.
    await standard.getByRole('button', { name: tr(language, 'actions.edit') }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByRole('heading', { name: c('businessHours.edit') })).toBeVisible();
    const to = dialog.getByLabel(c('businessHours.to')).first();
    await to.fill('06:00');
    await dialog.getByRole('button', { name: tr(language, 'actions.save') }).click();
    await expect(dialog.getByText(c('errors.endBeforeStart'))).toBeVisible();
    await shot('business-hours-editor');
    await dialog.getByRole('button', { name: tr(language, 'actions.cancel') }).click();
    await expect(dialog).toBeHidden();
  });

  test('Settings → Holidays: add a partial-day holiday for one year, then remove it', async ({
    page,
    language,
    shot,
  }) => {
    const c = (key: string, params?: Record<string, string | number>) =>
      tr(language, `calendar:${key}`, params);
    const year = new Date().getFullYear();
    const date = `${year}-12-${language === 'ar' ? '30' : '31'}`;
    await page.goto('/settings/holidays');
    await expect(page.getByRole('heading', { level: 1, name: c('holidays.title') })).toBeVisible();

    await page.getByRole('button', { name: c('holidays.new') }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(c('holidays.nameAr'), { exact: true }).fill('نصف يوم نهاية السنة');
    await dialog.getByLabel(c('holidays.nameEn'), { exact: true }).fill('Year-end half day');
    await dialog.getByLabel(c('holidays.startDate'), { exact: true }).fill(date);
    await dialog.getByRole('switch', { name: c('holidays.allDaySwitch') }).click();
    await dialog.getByLabel(c('holidays.from'), { exact: true }).fill('12:00');
    await dialog.getByLabel(c('holidays.to'), { exact: true }).fill('15:30');
    await shot('holiday-editor');
    await dialog.getByRole('button', { name: tr(language, 'actions.save') }).click();
    await expect(dialog).toBeHidden();

    const row = page
      .getByTestId('holiday-row')
      .filter({ hasText: language === 'ar' ? 'نصف يوم نهاية السنة' : 'Year-end half day' });
    await expect(row).toContainText('12:00–15:30');
    await shot('settings-holidays');

    // Removed again so the demo calendar stays as seeded (and reruns do not overlap).
    await row.getByRole('button', { name: c('holidays.remove') }).click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: c('holidays.remove') })
      .click();
    await expect(row).toHaveCount(0);
  });
});
