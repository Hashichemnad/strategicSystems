import type { APIRequestContext } from '@playwright/test';
import {
  apiPost,
  apiPut,
  createItemViaApi,
  expect,
  latestEmail,
  seed,
  test,
  tr,
  unitIdByCode,
  useMailpit,
} from './fixtures';

/** Phase 3: audit trail (History tab, Audit Log, verification), documents, notifications, templates and the job dashboard. */

async function employeeIdOf(admin: APIRequestContext, email: string): Promise<string> {
  const response = await admin.get('/api/v1/org/employees', { params: { search: email, pageSize: '5' } });
  const body = (await response.json()) as { items: { id: string; email: string | null }[] };
  const found = body.items.find((e) => e.email === email);
  if (!found) throw new Error(`Employee ${email} not found`);
  return found.id;
}

test.describe('as a sample editor', () => {
  test('a record shows its changes old → new in the History tab and keeps versioned documents with a preview', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    const tag = `${language}-${Date.now().toString(36)}`;
    const item = await createItemViaApi(page, {
      nameAr: `خطة التحول ${tag}`,
      nameEn: `Transformation plan ${tag}`,
    });
    const current = (await (await page.request.get(`/api/v1/sample/items/${item.id}`)).json()) as {
      version: number;
    };
    const updated = await apiPut(page, `/api/v1/sample/items/${item.id}`, {
      nameAr: `خطة التحول ${tag}`,
      nameEn: `Digital transformation plan ${tag}`,
      descriptionAr: 'تمت إضافة وصف عربي',
      descriptionEn: null,
      version: current.version,
    });
    expect(updated.status()).toBe(200);

    await page.goto(`/sample/items/${item.id}?tab=history`);
    await expect(page.getByRole('tab', { name: tr(language, 'audit:history.tab') })).toHaveAttribute(
      'aria-selected',
      'true',
    );
    const timeline = page.getByRole('list', { name: tr(language, 'audit:history.timeline') });
    await expect(timeline.getByText(`Digital transformation plan ${tag}`)).toBeVisible();
    await expect(timeline.locator('del', { hasText: `Transformation plan ${tag}` })).toBeVisible();
    await expect(timeline.getByText(tr(language, 'sample:fields.nameEn')).first()).toBeVisible();
    await expect(
      timeline
        .getByText(
          new RegExp(`^(${tr(language, 'audit:channels.ui')}|${tr(language, 'audit:channels.api')})$`),
        )
        .first(),
    ).toBeVisible();
    await shot('record-history', { fullPage: true });

    // A bilingual PDF produced by the server (Audit Log export through Gotenberg) becomes a document of the record.
    const pdf = await adminApi.get('/api/v1/audit/entries/export', {
      params: { format: 'pdf', lang: language, kind: 'change', pageSize: '20' },
    });
    expect(pdf.status()).toBe(200);
    expect(pdf.headers()['content-type']).toContain('application/pdf');

    await page.getByRole('tab', { name: tr(language, 'files:tab') }).click();
    await page.getByRole('button', { name: tr(language, 'files:upload') }).click();
    const dialog = page.getByRole('dialog');
    await dialog.locator('input[type="file"]').setInputFiles({
      name: `audit-${language}.pdf`,
      mimeType: 'application/pdf',
      buffer: await pdf.body(),
    });
    await dialog
      .getByLabel(tr(language, 'files:comment'))
      .fill(language === 'ar' ? 'نسخة للمراجعة' : 'Copy for review');
    await dialog.getByRole('button', { name: tr(language, 'files:upload') }).click();
    await expect(dialog).toBeHidden();
    const row = page.getByRole('row', { name: new RegExp(`audit-${language}\\.pdf`) });
    await expect(row).toBeVisible();
    await shot('record-documents');

    await row
      .getByRole('button', { name: tr(language, 'files:preview', { name: `audit-${language}.pdf` }) })
      .click();
    const preview = page.getByRole('dialog');
    await expect(preview.locator('canvas')).toBeVisible({ timeout: 20_000 });
    await page.waitForTimeout(500); // pdf.js paints the page after the canvas appears
    await shot('document-preview');
    await page.keyboard.press('Escape');

    // A disguised file is refused by its content, not its name.
    await page.getByRole('button', { name: tr(language, 'files:upload'), exact: true }).click();
    await page
      .getByRole('dialog')
      .locator('input[type="file"]')
      .setInputFiles({
        name: 'innocent.pdf',
        mimeType: 'application/pdf',
        buffer: Buffer.from('<html><script>alert(1)</script></html>'),
      });
    await page
      .getByRole('dialog')
      .getByRole('button', { name: tr(language, 'files:upload') })
      .click();
    const refusal = tr(language, 'files:errors.typeNotAllowed', { allowed: '' }).split('(')[0] ?? '';
    await expect(page.getByText(refusal.trim()).first()).toBeVisible();
  });
});

test.describe('as an administrator', () => {
  test.use({ signedInAs: 'mariam' });

  test('the Audit Log searches across systems and exports; the verification screen checks the chain', async ({
    page,
    language,
    shot,
  }) => {
    const tag = `audit-${language}-${Date.now().toString(36)}`;
    await createItemViaApi(page, { nameEn: tag });

    await page.goto('/audit');
    await expect(
      page.getByRole('heading', { level: 1, name: tr(language, 'audit:log.title') }),
    ).toBeVisible();
    await page.getByLabel(tr(language, 'audit:log.search')).fill(tag);
    await expect(page).toHaveURL(new RegExp(`text=${tag}`));
    await expect(page.getByRole('row', { name: new RegExp(tag) })).toHaveCount(1);
    await page.getByLabel(tr(language, 'audit:log.search')).fill('');
    await expect(page).not.toHaveURL(/text=/);
    await expect(page.getByRole('row').nth(5)).toBeVisible();
    await shot('audit-log');

    const download = page.waitForEvent('download');
    await page.getByRole('link', { name: tr(language, 'audit:log.exportExcel') }).click();
    expect((await download).suggestedFilename()).toBe('audit-log.xlsx');

    await page.goto('/audit/verification');
    await page.getByRole('button', { name: tr(language, 'audit:verification.verifyNow') }).click();
    await expect(page.getByText(tr(language, 'audit:verification.intact'), { exact: true })).toBeVisible();
    await expect(page.getByText(tr(language, 'audit:verification.ok')).first()).toBeVisible();
    await shot('audit-verification');
  });

  test('units and roles show their change log through the shared History component', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    const unitId = await unitIdByCode(adminApi, 'DEP-STR');
    await page.goto(`/org/units?unit=${unitId}`);
    await page.getByRole('tab', { name: tr(language, 'audit:history.changeLog') }).click();
    const unitTimeline = page.getByRole('list', { name: tr(language, 'audit:history.timeline') });
    await expect(unitTimeline.getByText(tr(language, 'audit:actions.create')).first()).toBeVisible();
    await expect(unitTimeline.getByText('DEP-STR').first()).toBeVisible();
    await shot('unit-change-log', { fullPage: true });

    await page.goto('/settings/access/roles?system=sample');
    await page.getByRole('tab', { name: tr(language, 'audit:history.tab') }).click();
    const roleTimeline = page.getByRole('list', { name: tr(language, 'audit:history.timeline') });
    await expect(
      roleTimeline.getByText(tr(language, 'audit:history.roleFields.PermissionCode')).first(),
    ).toBeVisible();
    await expect(roleTimeline.getByText('sample.item.read').first()).toBeVisible();
    await shot('role-history', { fullPage: true });
  });

  test('notification templates, file settings and the job dashboard', async ({ page, language, shot }) => {
    await page.goto('/settings/notifications');
    await expect(
      page.getByRole('heading', { level: 1, name: tr(language, 'notifications:templates.title') }),
    ).toBeVisible();
    const row = page.getByRole('row', { name: /identity\.passwordReset/ });
    await expect(row).toBeVisible();
    await shot('notification-templates');
    await row.getByRole('button', { name: tr(language, 'common:actions.edit') }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog.getByLabel(tr(language, 'notifications:templates.subjectAr'))).toHaveValue(
      'إعادة تعيين كلمة المرور',
    );
    await expect(dialog.locator('iframe')).toBeVisible();
    await page.waitForTimeout(800);
    await shot('notification-template-editor');
    await page.keyboard.press('Escape');

    await page.goto('/settings/files');
    await expect(
      page.getByRole('heading', { level: 1, name: tr(language, 'files:settings.title') }),
    ).toBeVisible();
    await shot('files-settings');

    await page.goto('/settings/jobs');
    await expect(page.getByRole('heading', { level: 1, name: tr(language, 'jobs:title') })).toBeVisible();
    const verify = page.getByTestId('job-row').filter({ hasText: 'audit.verify-chain' });
    await verify.getByRole('button', { name: tr(language, 'jobs:runNow') }).click();
    await expect(page.getByText(tr(language, 'jobs:toast.run'), { exact: true })).toBeVisible();
    await expect(verify.getByText(tr(language, 'jobs:statuses.succeeded'))).toBeVisible({ timeout: 20_000 });
    // Platform jobs, the three Task Management jobs, the two Teams jobs, the KPI windows, the Phase 14 jobs, the Portfolio monthly cycle and
    // the Risk monitoring job (Phase 13) and the Portfolio communication reminders (Phase 10).
    await expect(page.getByTestId('job-row')).toHaveCount(19);
    await shot('job-dashboard', { fullPage: true });
  });
});

test.describe('notifications', () => {
  test('sharing an item notifies the new editor in the app and by e-mail in both languages', async ({
    page,
    language,
    shot,
    adminApi,
    baseURL,
  }) => {
    await useMailpit(adminApi, baseURL ?? '');
    const tag = `${language}-${Date.now().toString(36)}`;
    const item = (await (
      await apiPost(adminApi, '/api/v1/sample/items', {
        nameAr: `بند مشترك ${tag}`,
        nameEn: `Shared item ${tag}`,
      })
    ).json()) as { id: string };
    const since = new Date();
    const shared = await apiPost(adminApi, `/api/v1/sample/items/${item.id}/editors`, {
      employeeId: await employeeIdOf(adminApi, seed.users.ahmed),
    });
    expect(shared.status()).toBe(200);

    // Wait for this notification itself (the outbox delivers it): Ahmed may already hold unread notifications from earlier scenarios,
    // so the badge alone does not prove it has arrived.
    const sharedName = language === 'ar' ? `بند مشترك ${tag}` : `Shared item ${tag}`;
    await expect
      .poll(
        async () =>
          (await (await page.request.get('/api/v1/notifications/mine?page=1&pageSize=20')).text()).includes(
            sharedName,
          ),
        {
          timeout: 60_000,
          intervals: [500, 1000, 2000],
        },
      )
      .toBe(true);
    await page.goto('/');
    const bell = page.getByTestId('notification-bell');
    await expect(bell.locator('span').filter({ hasText: /\S/ })).toBeVisible({ timeout: 20_000 });
    await bell.click();
    const title =
      language === 'ar'
        ? `يمكنك الآن تعديل البند «بند مشترك ${tag}»`
        : `You can now edit “Shared item ${tag}”`;
    await expect(
      page.getByRole('menuitem', { name: new RegExp(title.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&')) }),
    ).toBeVisible();
    await shot('notification-bell');
    await page.getByRole('menuitem', { name: tr(language, 'notifications:viewAll') }).click();
    await expect(
      page.getByRole('heading', { level: 1, name: tr(language, 'notifications:title') }),
    ).toBeVisible();
    await shot('notification-centre');
    await page
      .getByRole('button', { name: tr(language, 'notifications:open') })
      .first()
      .click();
    await expect(page).toHaveURL(new RegExp(`/sample/items/${item.id}`));

    const email = await latestEmail(page, seed.users.ahmed, since);
    expect(email.subject).toBe(
      `يمكنك الآن تعديل البند «بند مشترك ${tag}» | You can now edit “Shared item ${tag}”`,
    );
    expect(email.text).toContain('مرحباً');
    expect(email.text).toContain('Hello');

    await page.goto('/profile?tab=notifications');
    await expect(page.getByText(tr(language, 'notifications:preferences.digest'))).toBeVisible();
    await shot('notification-preferences');
  });
});
