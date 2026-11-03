import type { Browser, APIRequestContext, Page } from '@playwright/test';
import { apiPost, expect, savePreferences, test, tr, type Language } from './fixtures';

/** A second browser session signed in as the seeded administrator (Mariam, head of the Strategy & Performance Department). */
async function mariamPage(browser: Browser, adminApi: APIRequestContext, language: Language): Promise<Page> {
  const context = await browser.newContext({
    locale: language === 'ar' ? 'ar-AE' : 'en-GB',
    viewport: { width: 1366, height: 860 },
  });
  await context.addInitScript((lang) => {
    localStorage.setItem('strategy.language', lang);
    localStorage.setItem('strategy.theme', 'light');
  }, language);
  await context.addCookies((await adminApi.storageState()).cookies);
  const page = await context.newPage();
  await savePreferences(page, language);
  return page;
}

test.describe('approval settings', () => {
  test.use({ signedInAs: 'mariam' });

  test('Approval Settings centre: a conditional step, live condition check and "who would approve" preview', async ({
    page,
    language,
    shot,
  }) => {
    const wf = (key: string, params?: Record<string, string | number>) =>
      tr(language, `workflow:${key}`, params);
    const tag = `${language}${Date.now().toString(36)}`;

    await page.goto('/settings/approvals');
    await expect(page.getByRole('heading', { level: 1, name: wf('settings.title') })).toBeVisible();
    const processName = language === 'ar' ? 'اعتماد البند التجريبي' : 'Sample item approval';
    const row = page.getByRole('row').filter({ hasText: processName });
    await expect(row).toBeVisible();
    await shot('approval-settings');

    await row.getByRole('link', { name: wf('settings.processes.edit') }).click();
    await expect(page.getByRole('heading', { level: 1, name: processName })).toBeVisible();
    const steps = page
      .getByRole('region', { name: /./u })
      .filter({ has: page.getByLabel(wf('editor.code')) });
    const before = await steps.count();

    // Add a conditional step: an invalid condition is explained while typing, then fixed.
    await page.getByRole('button', { name: wf('editor.addStep') }).click();
    const added = page.getByRole('region', { name: wf('editor.step', { number: before + 1 }) });
    await added.getByLabel(wf('editor.code')).fill(`review-${tag}`);
    await added.getByLabel(wf('editor.nameAr')).fill('مراجعة تنفيذية');
    await added.getByLabel(wf('editor.nameEn')).fill('Executive review');
    const condition = added.getByLabel(wf('editor.condition'));
    await condition.fill('amount >');
    await expect(added.getByText(tr(language, 'expressions:errors.unexpectedEnd'))).toBeVisible();
    await condition.fill('amount > 500000');
    await expect(added.getByText(wf('editor.conditionValid'))).toBeVisible();

    // Preview for Ahmed with a small amount: Q-P4 skips the section manager, the director (Mariam) approves.
    const preview = page.getByRole('region', { name: wf('preview.title') });
    await preview
      .getByRole('combobox')
      .first()
      .fill(language === 'ar' ? 'أحمد' : 'Ahmed');
    await page
      .getByRole('option', { name: language === 'ar' ? /أحمد السويدي/u : /Ahmed Al Suwaidi/u })
      .click();
    await preview.getByLabel(language === 'ar' ? 'المبلغ' : 'Amount').fill('25000');
    await preview.getByRole('button', { name: wf('preview.run') }).click();
    await expect(preview.getByText(wf('preview.result.included'))).toBeVisible();
    await expect(
      preview
        .getByRole('paragraph')
        .filter({ hasText: language === 'ar' ? 'مريم الحمادي' : 'Mariam Al Hammadi' }),
    ).toBeVisible();
    await expect(preview.getByText(wf('preview.outcomes.skipped')).first()).toBeVisible();
    await shot('approval-step-editor-preview', { fullPage: true });

    // Save as a new version, then take the extra step out again (so the demo chain stays as seeded).
    await page.getByLabel(wf('editor.note')).fill(`E2E ${tag}`);
    await page.getByRole('button', { name: wf('editor.save') }).click();
    await expect(
      page.getByText(new RegExp(wf('editor.saved', { version: 'XX' }).replace('XX', '\\d+'), 'u')).first(),
    ).toBeVisible();
    await added.getByRole('button', { name: wf('editor.removeStep') }).click();
    await page.getByRole('button', { name: wf('editor.save') }).click();
    await page.getByRole('tab', { name: wf('editor.tabs.versions') }).click();
    await expect(page.getByRole('cell', { name: `E2E ${tag}` })).toBeVisible();
    await shot('approval-versions');
  });
});

test.describe('approval flow', () => {
  test('submit, return for correction, resubmit and approve; My Work and the approval panel', async ({
    page,
    language,
    shot,
    browser,
    adminApi,
  }) => {
    const wf = (key: string, params?: Record<string, string | number>) =>
      tr(language, `workflow:${key}`, params);
    const sm = (key: string) => tr(language, `sample:${key}`);
    const tag = `${language}-${Date.now().toString(36)}`;
    const title = language === 'ar' ? `شراء معدات ${tag}` : `Equipment purchase ${tag}`;
    const created = await apiPost(page, '/api/v1/sample/items', {
      nameAr: `شراء معدات ${tag}`,
      nameEn: `Equipment purchase ${tag}`,
      amount: 25000,
    });
    expect(created.status()).toBe(201);
    const { id } = (await created.json()) as { id: string };

    // Ahmed: the banner says what is next and offers "Submit for approval".
    await page.goto(`/sample/items/${id}`);
    await expect(page.getByRole('heading', { level: 1, name: title })).toBeVisible();
    await expect(page.getByRole('status').filter({ hasText: sm('currentStatus.draft') })).toBeVisible();
    await shot('record-status-draft');
    await page.getByRole('button', { name: sm('actions.submit') }).click();
    const submit = page.getByRole('dialog');
    await submit
      .getByLabel(sm('submitDialog.comment'))
      .fill(language === 'ar' ? 'مطلوب للربع القادم' : 'Needed next quarter');
    await submit.getByRole('button', { name: sm('submitDialog.confirm') }).click();
    await expect(submit).toBeHidden();
    const step = language === 'ar' ? 'مدير الإدارة' : 'Department director';
    await expect(
      page.getByRole('status').filter({ hasText: wf('currentStatus.waitingStep', { step }) }),
    ).toBeVisible();

    // Mariam: My Work lists the approval with a deep link.
    const mariam = await mariamPage(browser, adminApi, language);
    await mariam.goto('/');
    const approvals = mariam.getByRole('region', { name: wf('myWork.approvals.title') });
    await expect(approvals.getByRole('link', { name: title })).toBeVisible();
    await mariam.evaluate(() => document.fonts.ready);
    await mariam.screenshot({ path: shotPath(language, 'my-work') });
    await approvals.getByRole('link', { name: title }).click();
    await expect(mariam.getByRole('heading', { level: 1, name: title })).toBeVisible();

    // Return for correction: a comment is required.
    await mariam
      .getByRole('status')
      .getByRole('button', { name: wf('actions.return') })
      .click();
    let dialog = mariam.getByRole('dialog');
    await dialog.getByRole('button', { name: wf('panel.confirm') }).click();
    await expect(dialog.getByText(wf('errors.commentRequired'))).toBeVisible();
    await dialog
      .getByRole('textbox')
      .fill(language === 'ar' ? 'يرجى إرفاق عرض السعر' : 'Please attach the quotation');
    await dialog.getByRole('button', { name: wf('panel.confirm') }).click();
    await expect(dialog).toBeHidden();

    // Ahmed: returned → resubmit.
    await page.reload();
    await expect(page.getByRole('status').filter({ hasText: sm('currentStatus.returned') })).toBeVisible();
    await page.getByRole('button', { name: sm('actions.resubmit') }).click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: sm('submitDialog.confirm') })
      .click();
    await expect(page.getByRole('dialog')).toBeHidden();

    // Mariam approves from the approval panel; the panel shows both submissions and the history.
    await mariam.goto(`/sample/items/${id}?tab=approvals`);
    const panel = mariam.getByRole('tabpanel');
    await panel.getByRole('button', { name: wf('actions.approve') }).click();
    dialog = mariam.getByRole('dialog');
    await dialog.getByRole('button', { name: wf('panel.confirm') }).click();
    await expect(dialog).toBeHidden();
    await expect(panel.getByText(wf('status.approved')).first()).toBeVisible();
    await expect(panel.getByText(wf('panel.events.resubmitted'))).toBeVisible();
    await panel.getByText(wf('panel.previous')).click();
    await mariam.evaluate(() => document.fonts.ready);
    await mariam.screenshot({ path: shotPath(language, 'approval-panel'), fullPage: true });
    await mariam.context().close();

    // Ahmed sees the outcome on the record: chip, stepper and banner.
    await page.reload();
    await expect(page.getByRole('status').filter({ hasText: sm('currentStatus.approved') })).toBeVisible();
    await shot('record-approved');
  });
});

function shotPath(language: Language, name: string) {
  return `../docs/screenshots/${process.env.E2E_SCREENSHOTS ?? 'phase-4'}/${language}-${name}.png`;
}
