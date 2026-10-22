import { createItemViaApi, expect, test, text } from './fixtures';

test('shell renders in the user language and direction', async ({ page, language, shot }) => {
  await page.goto('/');
  await expect(page.locator('html')).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr');
  await expect(page.locator('html')).toHaveAttribute('lang', language);
  await expect(page.getByRole('heading', { level: 1, name: text[language].home })).toBeVisible();
  await expect(page.getByRole('navigation').getByRole('link', { name: text[language].sample })).toBeVisible();

  // The sidebar sits on the start side: right in Arabic, left in English.
  const sidebarX = (await page.locator('aside').boundingBox())?.x ?? Number.NaN;
  const mainX = (await page.locator('main').boundingBox())?.x ?? Number.NaN;
  if (language === 'ar') expect(sidebarX).toBeGreaterThan(mainX);
  else expect(sidebarX).toBeLessThan(mainX);

  await shot('home');
});

test('language toggle flips the whole layout instantly without refetching data', async ({
  page,
  language,
}) => {
  await createItemViaApi(page, { nameAr: 'بند للتبديل', nameEn: 'Toggle item' });
  await page.goto('/sample');
  await expect(page.getByTestId('sample-row').first()).toBeVisible();

  const listCalls: string[] = [];
  page.on('request', (request) => {
    if (request.url().includes('/api/v1/sample/items')) listCalls.push(request.url());
  });

  await page.getByTestId('language-toggle').click();
  const other = language === 'ar' ? 'en' : 'ar';
  await expect(page.locator('html')).toHaveAttribute('dir', other === 'ar' ? 'rtl' : 'ltr');
  await expect(page.getByRole('heading', { level: 1, name: text[other].sample })).toBeVisible();
  await page.waitForTimeout(500);
  expect(listCalls).toEqual([]);

  // The choice survives a reload.
  await page.reload();
  await expect(page.locator('html')).toHaveAttribute('lang', other);
});

test('small screens get an off-canvas menu', async ({ page, language, shot }) => {
  await page.setViewportSize({ width: 360, height: 740 });
  await page.goto('/');
  const nav = page.getByRole('navigation', {
    name: language === 'ar' ? 'القائمة الرئيسية' : 'Main navigation',
  });
  await expect(nav).toBeHidden();
  await page.getByRole('button', { name: text[language].openMenu }).click();
  await expect(nav).toBeVisible();
  await page.locator('aside').evaluate((aside) => Promise.all(aside.getAnimations().map((a) => a.finished)));
  await shot('mobile-menu');
  await nav.getByRole('link', { name: text[language].sample }).click();
  await expect(page).toHaveURL(/\/sample/);
  await expect(nav).toBeHidden();
});

test('UI kit renders every shared component', async ({ page, language, shot }) => {
  await page.goto('/ui-kit');
  await expect(page.getByRole('heading', { level: 1, name: text[language].uiKit })).toBeVisible();
  await expect(
    page
      .getByRole('navigation')
      .filter({ has: page.locator('[aria-current="step"]') })
      .first(),
  ).toBeVisible();
  await shot('ui-kit', { fullPage: true });

  await page.evaluate(() => localStorage.setItem('strategy.theme', 'dark'));
  await page.reload();
  await expect(page.locator('html')).toHaveClass(/dark/);
  await shot('ui-kit-dark', { fullPage: true });
});
