import { apiPost, expect, test, tr, unitIdByCode } from './fixtures';

test.use({ signedInAs: 'mariam' });

/**
 * System → Settings → Members (SPEC-ADDENDUM §D1, ADRs 0166 / 0167): from Portfolio's settings the administrator opens Members, adds a
 * person (search) to Users and a unit (with its sub-units) to an extra list, then removes them with a reason.
 * Screenshots: <lang>-members-page, <lang>-members-add, <lang>-members-remove, <lang>-members-mobile.
 */
test('members: open Portfolio → Settings → Members, add a person and a unit, remove them', async ({
  page,
  language,
  shot,
  adminApi,
}) => {
  const tag = `m${Date.now().toString(36)}`;
  const parentId = await unitIdByCode(adminApi, 'DEP-STR');
  const unit = await apiPost(adminApi, '/api/v1/org/units', {
    code: `SEC-${tag}`.toUpperCase(),
    nameAr: `قسم الأعضاء ${tag}`,
    nameEn: `Members section ${tag}`,
    levelCode: 'section',
    parentId,
    effectiveFrom: '2024-01-01',
  });
  expect(unit.status()).toBe(201);
  const employee = await apiPost(adminApi, '/api/v1/org/employees', {
    employeeNo: `E2E-${tag}`,
    nameAr: `عضو تجريبي ${tag}`,
    nameEn: `Member ${tag}`,
    email: `member.${tag}@strategy.local`,
    unitId: parentId,
    joinDate: '2024-01-01',
  });
  expect(employee.status()).toBe(201);
  const account = await apiPost(adminApi, '/api/v1/identity/admin/users', {
    employeeId: ((await employee.json()) as { id: string }).id,
    sendInvitation: false,
  });
  expect(account.ok()).toBe(true);
  const personName = language === 'ar' ? `عضو تجريبي ${tag}` : `Member ${tag}`;
  const unitName = language === 'ar' ? `قسم الأعضاء ${tag}` : `Members section ${tag}`;

  // From the system's settings page: the link bar opens Members.
  await page.goto('/settings/portfolio');
  await page
    .getByTestId('system-settings-links')
    .getByRole('link', { name: tr(language, 'access:members.title') })
    .click();
  await expect(page).toHaveURL(/\/settings\/members\/portfolio$/u);
  await expect(page.getByRole('heading', { level: 1 })).toContainText(tr(language, 'access:members.title'));
  const users = page.getByRole('region', { name: tr(language, 'access:members.lists.users') });
  await expect(users).toBeVisible();
  await expect(page.getByRole('link', { name: tr(language, 'access:members.advanced') })).toBeVisible();
  await shot('members-page', { fullPage: true });

  // A person, found by search.
  await users
    .getByRole('button', {
      name: tr(language, 'access:members.addTo', { list: tr(language, 'access:members.lists.users') }),
    })
    .click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('combobox', { name: tr(language, 'access:members.person') }).fill(tag);
  await page.getByRole('option', { name: new RegExp(tag, 'u') }).click();
  await dialog.getByLabel(tr(language, 'access:members.note')).fill('E2E');
  await shot('members-add');
  await dialog.getByRole('button', { name: tr(language, 'access:members.save') }).click();
  await expect(dialog).toBeHidden();
  await expect(users.getByRole('row').filter({ hasText: personName })).toBeVisible();

  // A unit (and its sub-units) on the first extra list.
  const extra = page.locator('[data-testid^="members-list-extra:"]').first();
  await extra.getByRole('button').first().click();
  await dialog.getByRole('combobox', { name: tr(language, 'access:members.kind') }).click();
  await page.getByRole('option', { name: tr(language, 'access:members.kinds.unit') }).click();
  await dialog.getByRole('combobox', { name: tr(language, 'access:members.unit') }).click();
  await page.getByRole('option', { name: new RegExp(`SEC-${tag}`, 'iu') }).click();
  await dialog.getByRole('button', { name: tr(language, 'access:members.save') }).click();
  await expect(dialog).toBeHidden();
  await expect(extra.getByRole('row').filter({ hasText: unitName })).toBeVisible();

  // Remove the person with a reason (confirm dialog), then the unit.
  await users
    .getByRole('row')
    .filter({ hasText: personName })
    .getByRole('button', { name: tr(language, 'access:members.remove') })
    .click();
  await dialog.getByLabel(tr(language, 'access:members.reason')).fill('E2E clean-up');
  await shot('members-remove');
  await dialog.getByRole('button', { name: tr(language, 'access:members.remove') }).click();
  await expect(dialog).toBeHidden();
  await expect(users.getByRole('row').filter({ hasText: personName })).toHaveCount(0);
  await extra
    .getByRole('row')
    .filter({ hasText: unitName })
    .getByRole('button', { name: tr(language, 'access:members.remove') })
    .click();
  await dialog.getByRole('button', { name: tr(language, 'access:members.remove') }).click();
  await expect(extra.getByRole('row').filter({ hasText: unitName })).toHaveCount(0);

  // Phones (360 px).
  await page.setViewportSize({ width: 360, height: 740 });
  await expect(users).toBeVisible();
  await shot('members-mobile');
});
