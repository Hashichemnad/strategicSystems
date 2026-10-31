import { apiPost, expect, latestEmail, nextTotp, seed, test, tr, unitIdByCode, useMailpit } from './fixtures';

test.use({ signedInAs: null });

test('wrong password is refused; the administrator signs in with an authenticator code and signs out', async ({
  page,
  language,
  shot,
}) => {
  await page.goto('/org/units');
  await expect(page).toHaveURL(/\/login\?redirect=/u);
  await expect(page.locator('html')).toHaveAttribute('dir', language === 'ar' ? 'rtl' : 'ltr');
  await expect(page.getByRole('heading', { name: tr(language, 'auth:login.title') })).toBeVisible();
  await shot('login');

  await page.getByLabel(tr(language, 'auth:login.userName')).fill(seed.users.mariam);
  await page.getByLabel(tr(language, 'auth:login.password')).fill('not-the-password');
  await page.getByRole('button', { name: tr(language, 'auth:login.submit') }).click();
  await expect(page.getByRole('alert')).toContainText(tr(language, 'auth:errors.invalidCredentials'));
  await shot('login-wrong-password');

  await page.getByLabel(tr(language, 'auth:login.password')).fill(seed.password);
  await page.getByRole('button', { name: tr(language, 'auth:login.submit') }).click();
  await expect(page).toHaveURL(/\/login\/mfa/u);
  await expect(page.getByText(tr(language, 'auth:mfa.instructions.totp'))).toBeVisible();
  await page.getByLabel(`${tr(language, 'auth:mfa.code')} 1`).click();
  await page.keyboard.type(await nextTotp(seed.adminTotpKey));
  await shot('login-mfa');
  await page.getByRole('button', { name: tr(language, 'auth:mfa.verify') }).click();

  // Back to the page that was asked for, as the administrator.
  await expect(page).toHaveURL(/\/org\/units/u);
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'org:units.title') })).toBeVisible();
  await expect(
    page.getByRole('navigation').getByRole('link', { name: tr(language, 'shell:nav.settings') }),
  ).toBeVisible();

  await page.getByRole('button', { name: tr(language, 'shell:profile.menu') }).click();
  await page.getByRole('menuitem', { name: tr(language, 'shell:profile.signOut') }).click();
  await expect(page).toHaveURL(/\/login/u);
  await page.goto('/org/units');
  await expect(page).toHaveURL(/\/login\?redirect=/u);
});

test('an invited employee sets a password from the email, then resets a forgotten one', async ({
  page,
  language,
  shot,
  baseURL,
  adminApi,
}) => {
  const tag = `${language}${Date.now().toString(36)}`;
  const email = `invitee.${tag}@strategy.local`;
  const firstPassword = `Welcome#${tag}A1`;
  const secondPassword = `Changed#${tag}B2`;

  // The administrator adds the employee and an account; the invitation goes out by email.
  await useMailpit(adminApi, baseURL ?? 'http://localhost:5080');
  const employee = await apiPost(adminApi, '/api/v1/org/employees', {
    employeeNo: `E2E-${tag}`,
    nameAr: `موظف تجريبي ${tag}`,
    nameEn: `Test Employee ${tag}`,
    email,
    unitId: await unitIdByCode(adminApi, 'DEP-STR'),
    joinDate: '2024-01-01',
  });
  expect(employee.status()).toBe(201);
  const { id: employeeId } = (await employee.json()) as { id: string };
  const invitedAt = new Date(Date.now() - 1000);
  const account = await apiPost(adminApi, '/api/v1/identity/admin/users', {
    employeeId,
    sendInvitation: true,
  });
  expect(account.status()).toBe(200);

  const invitation = await latestEmail(page, email, invitedAt);
  const inviteLink = /https?:\/\/\S*\/reset-password\?\S+/u.exec(invitation.text)?.[0];
  expect(inviteLink).toBeTruthy();
  await page.goto(inviteLink ?? '');
  await expect(page.getByRole('heading', { name: tr(language, 'auth:reset.inviteTitle') })).toBeVisible();
  await page.getByLabel(tr(language, 'auth:reset.newPassword'), { exact: true }).fill(firstPassword);
  await page.getByLabel(tr(language, 'auth:reset.confirmPassword')).fill(firstPassword);
  await shot('invitation-set-password');
  await page.getByRole('button', { name: tr(language, 'auth:reset.submit') }).click();
  await expect(page.getByText(tr(language, 'auth:reset.done'))).toBeVisible();

  // Forgotten password: the link arrives by email and the new password works.
  await page.goto('/forgot-password');
  await page.getByLabel(tr(language, 'auth:login.userName')).fill(email);
  const requestedAt = new Date(Date.now() - 1000);
  await page.getByRole('button', { name: tr(language, 'auth:forgot.submit') }).click();
  await expect(page.getByText(tr(language, 'auth:forgot.sent'))).toBeVisible();
  await shot('forgot-password-sent');
  const reset = await latestEmail(page, email, requestedAt);
  expect(reset.subject).not.toBe(invitation.subject);
  await page.goto(/https?:\/\/\S*\/reset-password\?\S+/u.exec(reset.text)?.[0] ?? '');
  await expect(page.getByRole('heading', { name: tr(language, 'auth:reset.title') })).toBeVisible();
  await page.getByLabel(tr(language, 'auth:reset.newPassword'), { exact: true }).fill(secondPassword);
  await page.getByLabel(tr(language, 'auth:reset.confirmPassword')).fill(secondPassword);
  await page.getByRole('button', { name: tr(language, 'auth:reset.submit') }).click();
  await expect(page.getByText(tr(language, 'auth:reset.done'))).toBeVisible();

  await page.goto('/login');
  await page.getByLabel(tr(language, 'auth:login.userName')).fill(email);
  await page.getByLabel(tr(language, 'auth:login.password')).fill(secondPassword);
  await page.getByRole('button', { name: tr(language, 'auth:login.submit') }).click();
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'shell:home.title') })).toBeVisible();
});
