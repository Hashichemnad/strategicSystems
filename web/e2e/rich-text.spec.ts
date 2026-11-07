import { apiPost, expect, test, tr, type Language } from './fixtures';

/**
 * Phase 19 stream R — rich text (SPEC-ADDENDUM A2, ADR 0161): a programme request description is formatted in the Tiptap editor
 * (bold, list, paragraph direction), saved, and rendered on the request page; a script sent through the API is removed by the
 * server. Screenshots: docs/screenshots/$E2E_SCREENSHOTS/<lang>-rich-text-editor.png and <lang>-rich-text-view.png.
 */

const k = (language: Language, key: string) => tr(language, `portfolio:${key}`);
const rt = (language: Language, key: string) => tr(language, `richText.${key}`);

test.describe('rich text', () => {
  test('formats a request description, saves it and sees it rendered; unsafe markup is removed', async ({
    page,
    language,
    shot,
  }) => {
    test.setTimeout(120_000);
    const tag = `${language}${Date.now().toString(36)}`;
    await page.goto('/portfolio/requests/new');
    await page.getByRole('button', { name: k(language, 'start.manual') }).click();
    await page.getByLabel(k(language, 'fields.nameAr')).fill(`منصة المحاضر ${tag}`);
    await page.getByLabel(k(language, 'fields.nameEn')).fill(`Minutes platform ${tag}`);

    const field = page.locator('[data-field="description"]');
    const editor = field.getByRole('textbox', { name: k(language, 'fields.description') });
    await expect(editor).toBeVisible();
    await editor.click();
    await page.keyboard.type(language === 'ar' ? 'الأهداف الرئيسية' : 'Main goals');
    await page.keyboard.press('ControlOrMeta+A');
    await field.getByRole('button', { name: rt(language, 'bold') }).click();
    await expect(field.getByRole('button', { name: rt(language, 'bold') })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    await page.keyboard.press('End');
    await page.keyboard.press('Enter');
    await field.getByRole('button', { name: rt(language, 'bold') }).click();
    await field.getByRole('button', { name: rt(language, 'bulletList') }).click();
    await page.keyboard.type(language === 'ar' ? 'نموذج إلكتروني' : 'Online form');
    await page.keyboard.press('Enter');
    await page.keyboard.type('Payment gateway (English item)');
    // The English item of an Arabic text (and the other way round) gets its own direction.
    await field.getByRole('button', { name: rt(language, language === 'ar' ? 'ltr' : 'rtl') }).click();
    await expect(editor.locator('strong')).toHaveCount(1);
    await expect(editor.locator('li')).toHaveCount(2);
    await expect(editor.locator('li').nth(1)).toHaveAttribute('dir', language === 'ar' ? 'ltr' : 'rtl');

    await page
      .locator('[data-field="justifications"]')
      .getByRole('textbox', { name: k(language, 'fields.justifications') })
      .fill('Applicants wait ten days today; the target is two days.');
    await shot('rich-text-editor', { fullPage: true });

    await page.getByRole('button', { name: tr(language, 'actions.saveDraft') }).click();
    await expect(page).toHaveURL(/\/portfolio\/requests\/[0-9a-f-]+\/edit$/u);
    const id = /requests\/([0-9a-f-]+)\/edit/u.exec(page.url())?.[1] ?? '';

    await page.goto(`/portfolio/requests/${id}`);
    const view = page.locator('.rich-text').first();
    await expect(view.locator('strong')).toHaveText(language === 'ar' ? 'الأهداف الرئيسية' : 'Main goals');
    await expect(view.locator('li')).toHaveCount(2);
    await shot('rich-text-view', { fullPage: true });

    // The server keeps only the allow-list: a script and an event handler sent through the API never come back.
    const created = await apiPost(page, '/api/v1/portfolio/requests', {
      values: {
        nameEn: `Unsafe ${tag}`,
        description: '<p onclick="alert(1)"><em>kept</em></p><script>alert(1)</script>',
      },
    });
    expect(created.ok(), await created.text()).toBe(true);
    const body = (await created.json()) as { id: string; values: { description: string | null } };
    expect(body.values.description).toBe('<p><em>kept</em></p>');
    await page.goto(`/portfolio/requests/${body.id}`);
    await expect(page.locator('.rich-text em').first()).toHaveText('kept');
    expect(await page.locator('.rich-text script').count()).toBe(0);
  });
});
