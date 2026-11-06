import { expect, test, tr } from './fixtures';

/** Phase 18 (ADR 0151): the administrator Health page, in Arabic (RTL) and English (LTR). */
test.describe('as an administrator', () => {
  test.use({ signedInAs: 'mariam' });

  test('the health page lists every check by group', async ({ page, language, shot }) => {
    await page.goto('/settings');
    await page
      .getByRole('link', { name: tr(language, 'settings:pages.health.title') })
      .first()
      .click();
    await expect(page.getByRole('heading', { level: 1, name: tr(language, 'health:title') })).toBeVisible();
    for (const group of ['platform', 'jobs', 'integrations', 'storage', 'security'])
      await expect(
        page.getByRole('heading', { level: 2, name: tr(language, `health:groups.${group}`) }),
      ).toBeVisible();
    const database = page.getByTestId('health-check-database');
    await expect(database.getByText(tr(language, 'health:statuses.ok'))).toBeVisible();
    await expect(page.getByTestId('health-job-row').first()).toBeVisible();
    await shot('health-page', { fullPage: true });
  });
});

test.describe('as a regular user', () => {
  test('the health API is refused', async ({ page }) => {
    const response = await page.request.get('/api/v1/settings/health');
    expect(response.status()).toBe(403);
  });
});
