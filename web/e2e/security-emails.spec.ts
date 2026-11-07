import { createUserWithPassword, expect, mailpitUrl, savePreferences, test, tr } from './fixtures';

/**
 * Phase 19 — security e-mails (ADR 0162): a user changes their password in the profile; the bilingual security e-mail arrives in Mailpit in
 * the user's language, without the password. Screenshots: E2E_SCREENSHOTS=phase-19.
 */
test.use({ signedInAs: null });

test('changing the password sends a security e-mail in the user’s language without the password', async ({
  page,
  language,
  shot,
  baseURL,
  adminApi,
}) => {
  const tag = `se${language}${Date.now().toString(36)}`;
  const { email, password } = await createUserWithPassword(
    page,
    adminApi,
    baseURL ?? 'http://localhost:5080',
    tag,
  );
  await page.goto('/login?redirect=/profile');
  await page.getByLabel(tr(language, 'auth:login.userName')).fill(email);
  await page.getByLabel(tr(language, 'auth:login.password')).fill(password);
  await page.getByRole('button', { name: tr(language, 'auth:login.submit') }).click();
  await expect(page.getByRole('heading', { level: 1, name: tr(language, 'profile:title') })).toBeVisible();
  await savePreferences(page, language);

  const changed = `Changed#${tag}Z9`;
  const since = new Date(Date.now() - 1000);
  await page.goto('/profile?tab=security');
  await page.getByLabel(tr(language, 'profile:password.current')).fill(password);
  await page.getByLabel(tr(language, 'profile:password.new')).fill(changed);
  await page.getByRole('button', { name: tr(language, 'profile:password.submit') }).click();
  await expect(page.getByText(tr(language, 'profile:password.changed')).first()).toBeVisible();

  // The security e-mail: subject in the user's language first, both columns, no password.
  let id: string | undefined;
  await expect
    .poll(
      async () => {
        const response = await page.request.get(`${mailpitUrl}/api/v1/search`, {
          params: { query: `to:"${email}"`, limit: '10' },
        });
        const { messages } = (await response.json()) as {
          messages: { ID: string; Subject: string; Created: string }[];
        };
        id = messages.find(
          (m) =>
            new Date(m.Created) >= since &&
            m.Subject.includes(language === 'ar' ? 'تم تغيير كلمة المرور' : 'Your password was changed'),
        )?.ID;
        return Boolean(id);
      },
      { timeout: 20_000 },
    )
    .toBe(true);
  const message = (await (await page.request.get(`${mailpitUrl}/api/v1/message/${id}`)).json()) as {
    Subject: string;
    HTML: string;
    Text: string;
  };
  expect(
    message.Subject.startsWith(language === 'ar' ? 'تم تغيير كلمة المرور' : 'Your password was changed'),
  ).toBe(true);
  expect(message.HTML).not.toContain(changed);
  expect(message.Text).not.toContain(changed);
  expect(message.Text).not.toContain(password);
  expect(message.HTML).toContain('The password of your account was changed');
  expect(message.HTML).toContain('تم تغيير كلمة مرور حسابك');

  await page.setContent(message.HTML);
  await shot('security-email-password-changed', { fullPage: true });
});

test.describe('security e-mail settings', () => {
  test.use({ signedInAs: 'mariam' });

  test('the administrator sees the security e-mail settings', async ({ page, language, shot }) => {
    await page.goto('/admin/security');
    const section = page.getByTestId('security-emails');
    await expect(section).toBeVisible();
    await expect(section.getByText(tr(language, 'admin:security.fields.notifyNewDevice'))).toBeVisible();
    await section.scrollIntoViewIfNeeded();
    await shot('security-settings-emails');
  });
});
