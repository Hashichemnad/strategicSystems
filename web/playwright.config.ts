import { defineConfig, devices } from '@playwright/test';
import type { E2eOptions } from './e2e/fixtures';

const port = Number(process.env.E2E_PORT ?? 5080);
const baseURL = process.env.E2E_BASE_URL ?? `http://localhost:${port}`;
const connectionString =
  process.env.ConnectionStrings__Strategy ??
  'Host=localhost;Port=5432;Database=strategy;Username=strategy;Password=strategy';

/**
 * E2E in Arabic (RTL) and English (LTR). The host serves the built SPA (`npm run build` first) and applies
 * migrations on start. Set E2E_BASE_URL to test an already running installation instead.
 */
export default defineConfig<E2eOptions>({
  testDir: './e2e',
  // The first-run setup wizard needs an empty database and its own hosts: playwright.setup.config.ts.
  testIgnore: 'setup.spec.ts',
  fullyParallel: false,
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  // Visual baselines (ADR 0175) are written only on request (UPDATE_SNAPSHOTS=1, tools/visual-regression.sh); a missing one fails.
  updateSnapshots: process.env.UPDATE_SNAPSHOTS ? 'all' : 'none',
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    // Sandboxes with a preinstalled Chromium of another revision: E2E_CHROMIUM_PATH=/opt/pw-browsers/chromium/... (optional).
    ...(process.env.E2E_CHROMIUM_PATH
      ? { launchOptions: { executablePath: process.env.E2E_CHROMIUM_PATH } }
      : {}),
  },
  projects: [
    {
      name: 'ar',
      testIgnore: ['setup.spec.ts', 'a11y.spec.ts', 'visual.spec.ts'],
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 860 },
        locale: 'ar-AE',
        language: 'ar',
      },
    },
    {
      name: 'en',
      testIgnore: ['setup.spec.ts', 'a11y.spec.ts', 'visual.spec.ts'],
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 860 },
        locale: 'en-GB',
        language: 'en',
      },
    },
    // The accessibility audit visits record pages that the functional specs create, so it runs after both language projects
    // (on a fresh database it would otherwise find empty lists).
    {
      name: 'ar-a11y',
      testMatch: 'a11y.spec.ts',
      dependencies: ['ar', 'en'],
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 860 },
        locale: 'ar-AE',
        language: 'ar',
      },
    },
    {
      name: 'en-a11y',
      testMatch: 'a11y.spec.ts',
      dependencies: ['ar', 'en'],
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1366, height: 860 },
        locale: 'en-GB',
        language: 'en',
      },
    },
    // Visual-regression baselines per theme (ADR 0175): opt-in, because pixel baselines depend on the machine's font rendering.
    ...(process.env.E2E_VISUAL
      ? (['ar', 'en'] as const).map((language) => ({
          name: `visual-${language}`,
          testMatch: 'visual.spec.ts',
          use: {
            ...devices['Desktop Chrome'],
            viewport: { width: 1366, height: 860 },
            locale: language === 'ar' ? 'ar-AE' : 'en-GB',
            language,
          },
        }))
      : []),
  ],
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: `dotnet run --project ../src/Host/Strategy.Host --no-launch-profile --urls ${baseURL}`,
        url: `${baseURL}/health/ready`,
        timeout: 180_000,
        reuseExistingServer: !process.env.CI,
        env: {
          ASPNETCORE_ENVIRONMENT: 'Development',
          ConnectionStrings__Strategy: connectionString,
          Spa__RootPath: '../../../web/dist',
          // The whole suite calls the API from one IP within a minute; production keeps the default limits.
          RateLimiting__PermitLimit: '10000',
          RateLimiting__AuthPermitLimit: '1000',
          // Known authenticator key for the seeded administrator, so tests can compute codes.
          Seed__AdminTotpKey: process.env.E2E_ADMIN_TOTP_KEY ?? 'JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP',
          // Stand-in UAE Pass issuer for the vendor UAE Pass spec (ADR 0154); Development only.
          Development__MockUaePass: 'true',
        },
      },
});
