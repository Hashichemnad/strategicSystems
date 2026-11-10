import { apiPut, expect, test } from './fixtures';

/**
 * Visual-regression baseline per theme (SPEC-ADDENDUM §C5, ADR 0175): the UI kit (every shared component, static content) and the app
 * launcher in Classic / Enterprise / Modern × light / dark, per language project (`visual-ar`, `visual-en`). Opt-in (E2E_VISUAL=1):
 * pixel baselines depend on the machine's font rendering — re-baseline with `--update-snapshots` on the reference machine.
 */

test.use({ signedInAs: 'mariam' });
test.describe.configure({ mode: 'serial' });

let original: { value: Record<string, unknown> | null; version: number } | undefined;

test.beforeAll(async ({ adminApi }) => {
  original = (await (await adminApi.get('/api/v1/settings/appearance')).json()) as typeof original;
  const saved = await apiPut(adminApi, '/api/v1/settings/appearance', {
    value: { ...(original?.value ?? {}), allowUserTheme: true },
    version: original?.version ?? 0,
  });
  expect(saved.status()).toBe(200);
});

test.afterAll(async ({ adminApi }) => {
  await apiPut(adminApi, '/api/v1/identity/me/preferences', { uiTheme: '', theme: 'light' });
  const latest = (await (await adminApi.get('/api/v1/settings/appearance')).json()) as { version: number };
  await apiPut(adminApi, '/api/v1/settings/appearance', {
    value: original?.value ?? {},
    version: latest.version,
  });
});

for (const theme of ['classic', 'enterprise', 'modern'] as const) {
  for (const mode of ['light', 'dark'] as const) {
    test(`${theme} ${mode}`, async ({ page, language }) => {
      await apiPut(page, '/api/v1/identity/me/preferences', { uiTheme: theme, theme: mode, language });
      await page.goto('/ui-kit');
      await expect(page.locator('html')).toHaveAttribute('data-theme', theme);
      await page.waitForLoadState('networkidle');
      await page.evaluate(() => document.fonts.ready);
      // Only the page content: the shell shows live counters (notifications) that change between runs.
      await expect(page.locator('main')).toHaveScreenshot(`ui-kit-${theme}-${mode}.png`, {
        animations: 'disabled',
        maxDiffPixelRatio: 0.01,
      });
      if (theme !== 'classic') {
        await page.getByTestId('app-launcher').click();
        await expect(page.getByTestId('app-launcher-dialog')).toHaveScreenshot(
          `launcher-${theme}-${mode}.png`,
          {
            animations: 'disabled',
            maxDiffPixelRatio: 0.01,
          },
        );
      }
    });
  }
}
