import { defineConfig, devices } from '@playwright/test';

/**
 * First-run setup wizard E2E (ADR 0150), separate from the main suite because it needs an EMPTY database: each project starts its own
 * host (built with `dotnet build src/Host/Strategy.Host`) on a fresh database without the demo seed, from e2e/setup.spec.ts itself.
 * The SPA must be built first (`npm run build`).
 *
 *   npx playwright test -c playwright.setup.config.ts
 *
 * ConnectionStrings__Strategy (as for the main suite) names the server and an account allowed to create databases; the database name
 * in it is replaced by strategy_e2e_setup_<language>, dropped and recreated on every run (psql must be on the PATH).
 */
export default defineConfig({
  testDir: './e2e',
  testMatch: 'setup.spec.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: 0,
  timeout: 300_000,
  reporter: process.env.CI
    ? [['github'], ['html', { open: 'never', outputFolder: 'playwright-report-setup' }]]
    : [['list']],
  use: {
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...(process.env.E2E_CHROMIUM_PATH
      ? { launchOptions: { executablePath: process.env.E2E_CHROMIUM_PATH } }
      : {}),
  },
  projects: [
    {
      name: 'ar',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 860 },
        locale: 'ar-AE',
        baseURL: 'http://localhost:5181',
      },
      metadata: { language: 'ar', port: 5181 },
    },
    {
      name: 'en',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 860 },
        locale: 'en-GB',
        baseURL: 'http://localhost:5182',
      },
      metadata: { language: 'en', port: 5182 },
    },
  ],
});
