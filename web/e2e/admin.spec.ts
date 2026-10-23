import { expect, test, tr, useMailpit } from './fixtures';

test.use({ signedInAs: 'mariam' });

test('an identity provider is configured from the UI and stays disabled until enabled', async ({
  page,
  language,
  shot,
}) => {
  const tag = `${language}${Date.now().toString(36)}`;
  const name = `Entra ID ${tag}`;
  await page.goto('/admin/providers');
  await expect(
    page.getByRole('heading', { level: 1, name: tr(language, 'admin:providers.title') }),
  ).toBeVisible();

  await page.getByRole('button', { name: tr(language, 'admin:providers.create') }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(tr(language, 'admin:providers.code')).fill(`entra-${tag}`);
  await dialog.getByLabel(tr(language, 'admin:providers.nameAr')).fill(`الدخول الموحد ${tag}`);
  await dialog.getByLabel(tr(language, 'admin:providers.nameEn')).fill(name);
  await dialog.getByLabel(tr(language, 'admin:providers.clientSecret')).fill('a-client-secret');
  await dialog
    .getByLabel(tr(language, 'admin:providers.fields.authority'))
    .fill('https://login.microsoftonline.com/00000000-0000-0000-0000-000000000000/v2.0');
  await dialog
    .getByLabel(tr(language, 'admin:providers.fields.clientId'))
    .fill('11111111-2222-3333-4444-555555555555');
  await shot('admin-provider-editor');
  await dialog.getByRole('button', { name: tr(language, 'common:actions.save') }).click();

  // Saved: the editor now shows the addresses to register at the identity provider.
  await expect(page.getByRole('dialog', { name: tr(language, 'admin:providers.edit') })).toBeVisible();
  await expect(page.getByRole('dialog')).toContainText(`/signin-ext/entra-${tag}`);
  await shot('admin-provider-saved');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: tr(language, 'common:actions.close'), exact: true })
    .last() // the footer button (the other is the corner ×)
    .click();
  await expect(page.getByRole('dialog')).toBeHidden();
  const card = page
    .getByRole('listitem')
    .filter({ hasText: language === 'ar' ? `الدخول الموحد ${tag}` : name });
  await expect(card).toContainText(tr(language, 'admin:providers.disabled'));
  await shot('admin-providers');

  // Disabled providers are not offered on the sign-in page; enabling one adds its button.
  const offered = async () => {
    const response = await page.request.get('/api/v1/identity/auth/providers');
    return ((await response.json()) as { code: string }[]).some((p) => p.code === `entra-${tag}`);
  };
  expect(await offered()).toBe(false);
  await card.getByRole('button', { name: tr(language, 'admin:providers.enable') }).click();
  await expect(card).toContainText(tr(language, 'admin:providers.enabled'));
  expect(await offered()).toBe(true);
  await card.getByRole('button', { name: tr(language, 'admin:providers.disable') }).click();
  await expect(card).toContainText(tr(language, 'admin:providers.disabled'));
  expect(await offered()).toBe(false);
});

test('administration settings screens', async ({ page, language, shot, adminApi, baseURL }) => {
  await page.goto('/admin/security');
  await expect(
    page.getByRole('heading', { level: 1, name: tr(language, 'admin:security.title') }),
  ).toBeVisible();
  await shot('admin-security', { fullPage: true });

  // A configured mail server, whichever specs ran before (CI starts from an empty database).
  await useMailpit(adminApi, baseURL ?? 'http://localhost:5080');
  await page.goto('/admin/email');
  await expect(
    page.getByRole('heading', { level: 1, name: tr(language, 'admin:email.title') }),
  ).toBeVisible();
  await expect(page.getByLabel(tr(language, 'admin:email.fields.host'))).not.toHaveValue('');
  await shot('admin-email', { fullPage: true });

  await page.goto('/admin/users');
  await expect(
    page.getByRole('heading', { level: 1, name: tr(language, 'admin:users.title') }),
  ).toBeVisible();
  await expect(page.getByText('mariam@strategy.local').first()).toBeVisible();
  await shot('admin-users');

  await page.goto('/org/employees');
  await expect(
    page.getByRole('heading', { level: 1, name: tr(language, 'org:employees.title') }),
  ).toBeVisible();
  await page.getByRole('link', { name: language === 'ar' ? 'أحمد السويدي' : 'Ahmed Al Suwaidi' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toContainText(
    language === 'ar' ? 'أحمد السويدي' : 'Ahmed Al Suwaidi',
  );
  await shot('org-employee');
});

test('a regular user does not see administration and is refused by the server', async ({
  browser,
  language,
  baseURL,
}) => {
  const context = await browser.newContext({ baseURL, locale: language === 'ar' ? 'ar-AE' : 'en-GB' });
  const page = await context.newPage();
  const { signInViaApi, savePreferences } = await import('./fixtures');
  await signInViaApi(page, 'ahmed');
  await savePreferences(page, language);
  await page.goto('/org/units');
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'org:units.title') })).toBeVisible();
  await expect(page.getByRole('link', { name: tr(language, 'shell:nav.providers') })).toHaveCount(0);
  await expect(page.getByRole('button', { name: tr(language, 'org:units.createRoot') })).toHaveCount(0);
  expect((await page.request.get('/api/v1/identity/admin/providers')).status()).toBe(403);
  await context.close();
});
