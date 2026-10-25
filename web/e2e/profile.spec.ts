import { createUserWithPassword, expect, nextTotp, test, tr } from './fixtures';

test.use({ signedInAs: null });

test('a user turns on an authenticator app, signs in with it and manages access tokens', async ({
  page,
  language,
  shot,
  baseURL,
  adminApi,
  playwright,
}) => {
  const tag = `p${language}${Date.now().toString(36)}`;
  const { email, password } = await createUserWithPassword(
    page,
    adminApi,
    baseURL ?? 'http://localhost:5080',
    tag,
  );
  const signIn = async () => {
    await page.goto('/login?redirect=/profile');
    await page.getByLabel(tr(language, 'auth:login.userName')).fill(email);
    await page.getByLabel(tr(language, 'auth:login.password')).fill(password);
    await page.getByRole('button', { name: tr(language, 'auth:login.submit') }).click();
  };

  // MFA is optional for regular users: straight in.
  await signIn();
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'profile:title') })).toBeVisible();
  await page.getByRole('tab', { name: tr(language, 'profile:tabs.security') }).click();
  await page.getByRole('button', { name: tr(language, 'profile:mfa.add') }).click();
  const setup = page.getByRole('dialog');
  await setup.getByRole('button', { name: tr(language, 'auth:setup.totpStart') }).click();
  await expect(setup.getByRole('img', { name: tr(language, 'auth:setup.qrAlt') })).toBeVisible();
  const key = (await setup.getByLabel(tr(language, 'auth:setup.manualKey')).textContent()) ?? '';
  expect(key).toMatch(/^[a-z2-7 ]{16,}$/iu);
  await setup.getByLabel(`${tr(language, 'auth:mfa.code')} 1`).click();
  await page.keyboard.type(await nextTotp(key));
  await shot('profile-totp-setup');
  await setup.getByRole('button', { name: tr(language, 'auth:setup.confirm') }).click();
  await expect(setup).toBeHidden();
  await expect(page.getByLabel(tr(language, 'auth:recovery.title'))).toContainText(
    /[A-Z0-9]{5}-[A-Z0-9]{5}/u,
  );
  await shot('profile-recovery-codes');
  await expect(
    page.getByRole('listitem').filter({ hasText: tr(language, 'profile:mfa.methods.totp') }),
  ).toContainText(tr(language, 'profile:mfa.on'));

  // Personal access token: shown once, works as a bearer token, stops working when revoked.
  await page.getByRole('tab', { name: tr(language, 'profile:tabs.tokens') }).click();
  await page.getByRole('button', { name: tr(language, 'profile:tokens.create') }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(tr(language, 'profile:tokens.name')).fill(`Reporting script ${tag}`);
  await dialog
    .getByRole('checkbox', { name: tr(language, 'profile:tokens.scopeLabels.apiRead'), exact: true })
    .check();
  await dialog.getByRole('button', { name: tr(language, 'common:actions.create') }).click();
  const token = (await dialog.getByLabel(tr(language, 'profile:tokens.token')).textContent()) ?? '';
  expect(token).toMatch(/^sst_/u);
  await shot('profile-token-created');
  await page.keyboard.press('Escape');

  const apiClient = await playwright.request.newContext({ baseURL });
  const call = () =>
    apiClient.get('/api/v1/org/units/tree', { headers: { Authorization: `Bearer ${token}` } });
  expect((await call()).status()).toBe(200);
  const write = await apiClient.post('/api/v1/org/levels', {
    headers: { Authorization: `Bearer ${token}` },
    data: { code: 'x', nameEn: 'x', sort: 1 },
  });
  expect(write.status()).toBe(403); // read-only scope

  const row = page.getByRole('row').filter({ hasText: `Reporting script ${tag}` });
  await expect(row).toContainText(tr(language, 'profile:tokens.states.active'));
  await shot('profile-tokens');
  await row.getByRole('button', { name: tr(language, 'profile:tokens.revoke') }).click();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: tr(language, 'profile:tokens.revoke') })
    .click();
  await expect(row).toContainText(tr(language, 'profile:tokens.states.revoked'));
  expect((await call()).status()).toBe(401);
  await apiClient.dispose();

  // Next sign-in asks for the authenticator code.
  await page.getByRole('button', { name: tr(language, 'shell:profile.menu') }).click();
  await page.getByRole('menuitem', { name: tr(language, 'shell:profile.signOut') }).click();
  await expect(page).toHaveURL(/\/login/u);
  await signIn();
  await expect(page).toHaveURL(/\/login\/mfa/u);
  await page.getByLabel(`${tr(language, 'auth:mfa.code')} 1`).click();
  await page.keyboard.type(await nextTotp(key));
  await page.getByRole('button', { name: tr(language, 'auth:mfa.verify') }).click();
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'profile:title') })).toBeVisible();
});
