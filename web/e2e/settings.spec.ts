import { apiPost, apiPut, expect, test, tr, type Language } from './fixtures';

/**
 * Phase 2: access control and the Settings centre, in Arabic (RTL) and English (LTR).
 * Every test leaves the installation as it found it (systems on, default numbering, no overrides).
 */
test.describe('as an administrator', () => {
  test.use({ signedInAs: 'mariam' });

  test('settings centre and the permission matrix of a system', async ({ page, language, shot }) => {
    await page.goto('/settings');
    await expect(page.getByRole('heading', { level: 1, name: tr(language, 'settings:title') })).toBeVisible();
    for (const group of ['general', 'data', 'access', 'identity', 'organisation'])
      await expect(
        page.getByRole('heading', { level: 2, name: tr(language, `settings:groups.${group}`) }),
      ).toBeVisible();
    await shot('settings-hub', { fullPage: true });

    await page
      .getByRole('link', { name: tr(language, 'settings:pages.roles.title') })
      .first()
      .click();
    await expect(
      page.getByRole('heading', { level: 1, name: tr(language, 'access:roles.title') }),
    ).toBeVisible();
    await page.goto('/settings/access/roles?system=sample');
    const matrix = page.getByRole('table', { name: tr(language, 'access:roles.matrixCaption') });
    await expect(matrix.getByRole('rowheader').filter({ hasText: 'sample.item.update' })).toBeVisible();
    // Editor: update own records only (seeded default, ADR 0015).
    const cell = page.getByRole('combobox', {
      name: tr(language, 'access:roles.cellLabel', {
        permission: language === 'ar' ? 'تعديل البنود' : 'Edit items',
        role: language === 'ar' ? 'محرر' : 'Editor',
      }),
    });
    await expect(cell).toHaveText(tr(language, 'access:scopes.own'));
    await shot('access-matrix');

    await page.getByRole('tab', { name: tr(language, 'access:roles.recordRoles') }).click();
    await expect(page.getByText(tr(language, 'access:roles.recordRolesHint'))).toBeVisible();
    await shot('access-record-roles');
  });

  test('a role is assigned to a user in the UI and appears in the access review', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    await page.goto('/settings/access/assignments');
    await expect(
      page.getByRole('heading', { level: 1, name: tr(language, 'access:assignments.title') }),
    ).toBeVisible();
    await page.getByRole('button', { name: tr(language, 'access:assignments.new') }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByRole('combobox', { name: tr(language, 'access:assignments.role') }).click();
    await page
      .getByRole('option', {
        name: `${language === 'ar' ? 'إدارة الصلاحيات' : 'Access control'} — ${language === 'ar' ? 'مراجع الصلاحيات' : 'Access reviewer'}`,
      })
      .click();
    await dialog.getByRole('combobox', { name: tr(language, 'access:principals.user') }).fill('ahmed');
    await page.getByRole('option', { name: /ahmed@strategy\.local/u }).click();
    await shot('access-assign-dialog');
    await dialog.getByRole('button', { name: tr(language, 'access:assignments.assign') }).click();
    await expect(dialog).toBeHidden();
    const row = page
      .getByRole('row')
      .filter({ hasText: language === 'ar' ? 'مراجع الصلاحيات' : 'Access reviewer' });
    await expect(row.first()).toBeVisible();
    await shot('access-assignments');

    await page.goto('/settings/access/review');
    await page.getByRole('searchbox', { name: tr(language, 'access:review.search') }).fill('ahmed');
    await expect(
      page.getByRole('row').filter({ hasText: language === 'ar' ? 'مراجع الصلاحيات' : 'Access reviewer' }),
    ).toBeVisible();
    await shot('access-review');
    const download = page.waitForEvent('download');
    await page
      .getByRole('link', {
        name: tr(language, language === 'ar' ? 'access:review.exportAr' : 'access:review.exportEn'),
      })
      .click();
    expect((await download).suggestedFilename()).toMatch(/^access-review-\d{8}\.xlsx$/u);

    // Clean up through the API: end the assignment again.
    const assignments = (await (await adminApi.get('/api/v1/access/assignments')).json()) as {
      id: string;
      systemCode: string;
      roleCode: string;
      version: number;
      principal: { detail: string | null };
    }[];
    for (const a of assignments.filter(
      (x) => x.roleCode === 'reviewer' && x.principal.detail === 'ahmed@strategy.local',
    ))
      expect(
        (await apiPost(adminApi, `/api/v1/access/assignments/${a.id}/end`, { version: a.version })).ok(),
      ).toBe(true);
  });

  test('switching a system off hides it from the sidebar and blocks its API', async ({
    page,
    language,
    shot,
  }) => {
    await page.goto('/settings/systems');
    const sidebar = page.getByRole('navigation', { name: tr(language, 'shell:navigation') });
    const sampleLink = sidebar.getByRole('link', { name: tr(language, 'shell:nav.sample') });
    await expect(sampleLink).toBeVisible();
    const toggle = page.getByRole('switch', {
      name: tr(language, 'settings:systems.enabledFor', {
        name: language === 'ar' ? 'النظام التجريبي' : 'Sample system',
      }),
    });
    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'false');
    await expect(sampleLink).toBeHidden();
    expect((await page.request.get('/api/v1/sample/items')).status()).toBe(404);
    await shot('settings-systems');

    await toggle.click();
    await expect(toggle).toHaveAttribute('aria-checked', 'true');
    await expect(sampleLink).toBeVisible();
    expect((await page.request.get('/api/v1/sample/items')).status()).toBe(200);
  });

  test('a list value and a numbering format edited in the UI take effect at once', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    const code = `E2E-${language}-${Date.now().toString(36)}`.toUpperCase();
    await page.goto('/settings/lookups');
    await page.getByRole('button', { name: language === 'ar' ? /الدرجة الوظيفية/u : /Grade/u }).click();
    await page.getByRole('button', { name: tr(language, 'settings:lookups.newValue') }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(tr(language, 'settings:lookups.code')).fill(code);
    await dialog.getByLabel(tr(language, 'settings:lookups.labelAr')).fill('درجة اختبار');
    await dialog.getByLabel(tr(language, 'settings:lookups.labelEn')).fill('Test grade');
    await dialog.getByRole('button', { name: tr(language, 'settings:save') }).click();
    await expect(dialog).toBeHidden();
    await expect(page.getByRole('row').filter({ hasText: code })).toBeVisible();
    await shot('settings-lookups');
    const grades = (await (
      await page.request.get('/api/v1/settings/lookups/employee.grade/values')
    ).json()) as { code: string }[];
    expect(grades.map((g) => g.code)).toContain(code);

    await page.goto('/settings/numbering');
    const pattern = page.getByLabel(tr(language, 'settings:numbering.pattern'));
    await pattern.fill('ITEM/{YY}/{SEQ:3}');
    const year = String(new Date().getFullYear() % 100).padStart(2, '0');
    await expect(page.getByRole('status').filter({ hasText: `ITEM/${year}/001` })).toBeVisible();
    await page.getByRole('button', { name: tr(language, 'settings:save') }).click();
    await expect(page.getByText(tr(language, 'settings:saved'), { exact: true }).first()).toBeVisible();
    await shot('settings-numbering');
    const created = await apiPost(adminApi, '/api/v1/sample/items', { nameEn: `Numbered ${code}` });
    expect(created.status()).toBe(201);
    expect(((await created.json()) as { number: string }).number).toMatch(
      new RegExp(`^ITEM/${year}/\\d{3}$`, 'u'),
    );

    // Back to the default pattern.
    await restoreNumbering(adminApi);
  });

  test('a translation override changes a label live in both languages', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    const label = { ar: 'لوحة أعمالي', en: 'My desk' } satisfies Record<Language, string>;
    await page.goto('/settings/translations');
    await page
      .getByRole('searchbox', { name: tr(language, 'settings:translations.search') })
      .fill('nav.home');
    await page
      .getByRole('button', { name: tr(language, 'settings:translations.editKey', { key: 'nav.home' }) })
      .click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(tr(language, 'settings:translations.overrideAr')).fill(label.ar);
    await dialog.getByLabel(tr(language, 'settings:translations.overrideEn')).fill(label.en);
    await dialog.getByRole('button', { name: tr(language, 'settings:save') }).click();
    await expect(dialog).toBeHidden();

    const sidebar = page.getByRole('navigation', { name: tr(language, 'shell:navigation') });
    await expect(sidebar.getByRole('link', { name: label[language] })).toBeVisible();
    await shot('settings-translations');
    // The other language changed too: switch and look.
    const other: Language = language === 'ar' ? 'en' : 'ar';
    await page.getByRole('button', { name: tr(language, 'shell:language.switchTo') }).click();
    await expect(
      page
        .getByRole('navigation', { name: tr(other, 'shell:navigation') })
        .getByRole('link', { name: label[other] }),
    ).toBeVisible();

    for (const lang of ['ar', 'en'] as const)
      expect(
        (
          await apiPut(adminApi, '/api/v1/settings/translations', {
            language: lang,
            namespace: 'shell',
            key: 'nav.home',
            value: null,
          })
        ).ok(),
      ).toBe(true);
  });

  test('regional settings and branding', async ({ page, language, shot }) => {
    await page.goto('/settings/regional');
    await expect(
      page.getByRole('switch', { name: tr(language, 'settings:regional.hijriDisplay') }),
    ).toHaveAttribute('aria-checked', 'false');
    await shot('settings-regional');
    await page.goto('/settings/branding');
    await expect(page.getByLabel(tr(language, 'settings:branding.organisationNameAr'))).toBeVisible();
    await shot('settings-branding', { fullPage: true });
  });
});

test.describe('as a regular user', () => {
  test('the sidebar shows only what the user may open and settings stay closed', async ({
    page,
    language,
    shot,
  }) => {
    await page.goto('/');
    const sidebar = page.getByRole('navigation', { name: tr(language, 'shell:navigation') });
    await expect(sidebar.getByRole('link', { name: tr(language, 'shell:nav.units') })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: tr(language, 'shell:nav.sample') })).toBeVisible();
    await expect(sidebar.getByRole('link', { name: tr(language, 'shell:nav.settings') })).toHaveCount(0);
    await expect(sidebar.getByRole('link', { name: tr(language, 'shell:nav.users') })).toHaveCount(0);
    await shot('user-sidebar');

    await page.goto('/settings');
    await expect(page.getByText(tr(language, 'settings:nothing'))).toBeVisible();
    expect((await page.request.get('/api/v1/access/roles')).status()).toBe(403);
  });
});

async function restoreNumbering(admin: Parameters<typeof apiPut>[0]) {
  const formats = (await (
    await ('goto' in admin ? admin.request : admin).get('/api/v1/settings/numbering')
  ).json()) as {
    key: string;
    defaultPattern: string;
    version: number;
  }[];
  const format = formats.find((f) => f.key === 'sample.item');
  if (format)
    expect(
      (
        await apiPut(admin, '/api/v1/settings/numbering/sample.item', {
          pattern: format.defaultPattern,
          version: format.version,
        })
      ).ok(),
    ).toBe(true);
}
