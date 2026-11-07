import { createItemViaApi, expect, test, tr } from './fixtures';

/**
 * Global search (SPEC-ADDENDUM A1, ADR 0160): Ctrl+K focuses the top-bar search; an Arabic term typed without hamza / ta marbuta finds
 * the record; results are grouped by system; ↓ / Enter open the record, which then appears under "Recent".
 * Screenshots: <lang>-search-results, <lang>-search-recent, <lang>-search-mobile.
 */
test('global search: keyboard, Arabic normalisation, grouped results and recent items', async ({
  page,
  language,
  shot,
}) => {
  const tag = `s${Date.now().toString(36)}`;
  const first = await createItemViaApi(page, { nameAr: `مدرسة أحمد ${tag}`, nameEn: `Ahmed school ${tag}` });
  const second = await createItemViaApi(page, {
    nameAr: `مكتبة أحمد ${tag}`,
    nameEn: `Ahmed library ${tag}`,
  });

  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();

  const box = page.getByRole('combobox', { name: tr(language, 'search:label') });
  await page.keyboard.press('Control+K');
  await expect(box).toBeFocused();

  // Typed without hamza and with ha for ta marbuta: still found (normalised on both sides).
  await box.fill(`مدرسه احمد ${tag}`);
  const list = page.getByTestId('global-search-list');
  const options = list.getByRole('option');
  await expect(options).toHaveCount(1);
  await expect(list.getByRole('group').first()).toBeVisible();
  await expect(options.first()).toHaveAttribute('aria-selected', 'true');

  // Both items with a word common to them; ↓ moves to the second, Enter opens it.
  await box.fill(`${tag} احمد`);
  await expect(options).toHaveCount(2);
  await shot('search-results');
  await page.keyboard.press('ArrowDown');
  await expect(options.nth(1)).toHaveAttribute('aria-selected', 'true');
  await page.keyboard.press('Enter');
  await expect(page).toHaveURL(new RegExp(`/sample/items/(${first.id}|${second.id})`));
  await expect(box).toHaveValue('');

  // Esc closes; the visit is listed under "Recent" when the box is empty.
  await page.keyboard.press('Control+K');
  await expect(list.getByRole('group', { name: tr(language, 'search:recent') })).toBeVisible();
  await expect(list.getByRole('option').first()).toContainText(tag);
  await shot('search-recent');
  await page.keyboard.press('Escape');
  await expect(box).toHaveAttribute('aria-expanded', 'false');

  // Too short: a hint, no request.
  await box.fill('a');
  await expect(list).toContainText(tr(language, 'search:tooShort'));

  // Phones (360 px): an icon opens the search over the top bar.
  await page.keyboard.press('Escape');
  await page.keyboard.press('Escape');
  await page.setViewportSize({ width: 360, height: 740 });
  await page.getByTestId('global-search-open').click();
  await expect(box).toBeFocused();
  await box.fill(`${tag} احمد`);
  await expect(options).toHaveCount(2);
  await shot('search-mobile');
});
