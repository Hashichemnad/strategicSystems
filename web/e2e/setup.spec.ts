import { expect, test, type Page } from '@playwright/test';
import { execFileSync, spawn, type ChildProcess } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { expectAccessible } from './a11y';
import { nextTotp, tr, type Language } from './fixtures';

/**
 * First-run setup wizard (ADR 0150) on an empty installation, in Arabic and English (playwright.setup.config.ts). Each project starts
 * its own host on a fresh database without the demo seed, reads the one-time token with `--setup-token`, completes stage 1 (token,
 * organisation, branding, languages, administrator), signs in with MFA enrolment and walks through the guided configuration.
 */

const here = dirname(fileURLToPath(import.meta.url));
const hostDir = join(here, '..', '..', 'src', 'Host', 'Strategy.Host');
const hostDll = join(hostDir, 'bin', 'Debug', 'net10.0', 'Strategy.Host.dll');
const screenshotDir = join(
  here,
  '..',
  '..',
  'docs',
  'screenshots',
  process.env.E2E_SCREENSHOTS ?? 'phase-18',
);
mkdirSync(screenshotDir, { recursive: true });

const baseConnection =
  process.env.ConnectionStrings__Strategy ??
  'Host=localhost;Port=5432;Database=strategy;Username=strategy;Password=strategy';

function connectionFor(database: string): string {
  return `${baseConnection.replace(/Database=[^;]*;?/iu, '').replace(/;$/u, '')};Database=${database}`;
}

/** Drops and recreates the database with psql (the server and account of ConnectionStrings__Strategy). */
function recreateDatabase(database: string) {
  const parts = Object.fromEntries(
    baseConnection
      .split(';')
      .filter(Boolean)
      .map((part) => {
        const [key = '', ...value] = part.split('=');
        return [key.trim().toLowerCase(), value.join('=').trim()];
      }),
  );
  const args = [
    '-h',
    parts.host ?? 'localhost',
    '-p',
    parts.port ?? '5432',
    '-U',
    parts.username ?? 'postgres',
    '-d',
    'postgres',
  ];
  const env = { ...process.env, PGPASSWORD: parts.password ?? '' };
  execFileSync('psql', [...args, '-c', `DROP DATABASE IF EXISTS ${database} WITH (FORCE)`], {
    env,
    stdio: 'pipe',
  });
  execFileSync('psql', [...args, '-c', `CREATE DATABASE ${database}`], { env, stdio: 'pipe' });
}

function hostEnv(database: string): NodeJS.ProcessEnv {
  return {
    ...process.env,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ConnectionStrings__Strategy: connectionFor(database),
    Seed__Demo: 'false',
    RateLimiting__PermitLimit: '10000',
    RateLimiting__AuthPermitLimit: '1000',
    Scheduling__SchedulerName: `e2e-setup-${database}`,
  };
}

let host: ChildProcess | undefined;

test.describe.configure({ mode: 'serial' });

test.beforeAll(async ({ request }, testInfo) => {
  const { language, port } = testInfo.project.metadata as { language: Language; port: number };
  expect(existsSync(hostDll), `build the host first: dotnet build src/Host/Strategy.Host (${hostDll})`).toBe(
    true,
  );
  const database = `strategy_e2e_setup_${language}`;
  recreateDatabase(database);
  const log = createWriteStream(join(tmpdir(), `strategy-e2e-setup-${language}.log`));
  host = spawn('dotnet', [hostDll, '--urls', `http://localhost:${port}`], {
    cwd: hostDir,
    env: hostEnv(database),
  });
  host.stdout?.pipe(log);
  host.stderr?.pipe(log);
  await expect
    .poll(
      async () => {
        try {
          return (await request.get(`http://localhost:${port}/health/ready`)).status();
        } catch {
          return 0;
        }
      },
      { timeout: 180_000, intervals: [1_000] },
    )
    .toBe(200);
});

test.afterAll(() => {
  host?.kill();
});

/** A new token from the running installation's database (the one in the log stops working). */
function setupToken(language: Language): string {
  const output = execFileSync('dotnet', [hostDll, '--setup-token'], {
    cwd: hostDir,
    env: hostEnv(`strategy_e2e_setup_${language}`),
    encoding: 'utf8',
  });
  const token = /Setup token[^:]*:\s*(\S+)/u.exec(output)?.[1];
  if (!token) throw new Error(`No setup token in: ${output}`);
  return token;
}

async function shot(page: Page, language: Language, name: string) {
  await page.waitForLoadState('networkidle');
  // Every step of the wizard is also checked against WCAG 2.1 AA (docs/accessibility.md).
  await expectAccessible(page, name);
  await page.screenshot({ path: join(screenshotDir, `${language}-${name}.png`), fullPage: true });
}

test('a fresh installation is set up through the wizard and the first administrator signs in with MFA', async ({
  page,
}, testInfo) => {
  const { language } = testInfo.project.metadata as { language: Language };
  const t = (key: string, params?: Record<string, string>) => tr(language, key, params);
  const next = () => page.getByRole('button', { name: t('common:actions.next'), exact: true }).click();
  // Stage 2 names its buttons "Next setup step": an embedded Settings screen (organisation import) has a wizard of its own.
  const nextStep = () => page.getByRole('button', { name: t('setup:configure.nextStep') }).click();
  const heading = (key: string) => page.getByRole('heading', { level: 2, name: t(key) });
  await page.addInitScript((lang) => {
    if (!sessionStorage.getItem('e2e.initialised')) {
      localStorage.setItem('strategy.language', lang);
      localStorage.setItem('strategy.theme', 'light');
      sessionStorage.setItem('e2e.initialised', '1');
    }
  }, language);

  // Nobody can sign in yet: the sign-in page sends visitors to the wizard.
  await page.goto('/login');
  await expect(page).toHaveURL(/\/setup$/u);
  await expect(page.getByRole('heading', { level: 1, name: t('setup:title') })).toBeVisible();

  // 1. Token: a wrong one is refused, the one printed by --setup-token opens the wizard.
  await page.getByTestId('setup-token').fill('not-the-token');
  await next();
  await expect(page.getByText(t('setup:token.invalid'))).toBeVisible();
  await page.getByTestId('setup-token').fill(setupToken(language));
  await shot(page, language, 'setup-01-token');
  await next();

  // 2. Organisation profile.
  await expect(heading('setup:steps.organisation')).toBeVisible();
  await page.getByLabel(t('setup:organisation.nameAr')).fill('الهيئة التجريبية للتخطيط');
  await page.getByLabel(t('setup:organisation.nameEn')).fill('Demo Planning Authority');
  await page.getByLabel(t('setup:organisation.code')).fill('DPA');
  await shot(page, language, 'setup-02-organisation');
  await next();

  // 3. Branding.
  await expect(heading('setup:steps.branding')).toBeVisible();
  await page.getByLabel(t('setup:branding.emailFooterAr')).fill('الهيئة التجريبية للتخطيط — رسالة آلية');
  await page
    .getByLabel(t('setup:branding.emailFooterEn'))
    .fill('Demo Planning Authority — automated message');
  await shot(page, language, 'setup-03-branding');
  await next();

  // 4. Languages.
  await expect(heading('setup:steps.languages')).toBeVisible();
  await shot(page, language, 'setup-04-languages');
  await next();

  // 5. First administrator.
  const email = `admin.${language}@setup.local`;
  const password = 'Setup#Admin2026';
  await expect(heading('setup:steps.administrator')).toBeVisible();
  await page.getByLabel(t('setup:administrator.nameAr')).fill('سارة المنصوري');
  await page.getByLabel(t('setup:administrator.nameEn')).fill('Sara Al Mansoori');
  await page.getByLabel(t('setup:administrator.employeeNo')).fill('ADM-001');
  await page.getByLabel(t('setup:administrator.email')).fill(email);
  await page.getByLabel(new RegExp(`^${t('setup:administrator.password')}`, 'u')).fill(password);
  await page.getByLabel(t('setup:administrator.confirmPassword')).fill(password);
  await shot(page, language, 'setup-05-administrator');
  await page.getByRole('button', { name: t('setup:administrator.create') }).click();
  await expect(page.getByTestId('setup-done')).toContainText(email);
  await shot(page, language, 'setup-06-done');

  // The wizard is closed now.
  const status = (await (await page.request.get('/api/v1/setup/status')).json()) as { needsSetup: boolean };
  expect(status.needsSetup).toBe(false);

  // Sign-in: administrators must set up two-step verification first.
  await page.getByRole('link', { name: t('setup:done.signIn') }).click();
  await page.getByLabel(t('auth:login.userName')).fill(email);
  await page.getByLabel(t('auth:login.password')).fill(password);
  await page.getByRole('button', { name: t('auth:login.submit') }).click();
  await expect(page).toHaveURL(/\/login\/enrol/u);
  await page.getByRole('button', { name: t('auth:setup.totpStart') }).click();
  const key = (await page.getByLabel(t('auth:setup.manualKey')).textContent()) ?? '';
  await page.getByLabel(`${t('auth:mfa.code')} 1`).click();
  await page.keyboard.type(await nextTotp(key));
  await shot(page, language, 'setup-07-mfa-enrolment');
  await page.getByRole('button', { name: t('auth:setup.confirm') }).click();
  await page.getByRole('button', { name: t('auth:enrol.continue') }).click();

  // Stage 2: the guided configuration, every step reusing its Settings screen.
  await expect(page).toHaveURL(/\/setup\/configure$/u);
  await expect(page.getByRole('heading', { level: 1, name: t('setup:configure.title') })).toBeVisible();
  const steps = ['email', 'authentication', 'ai', 'calendar', 'import', 'systems'];
  for (const [index, step] of steps.entries()) {
    await expect(heading(`setup:configure.steps.${step}`)).toBeVisible();
    await expect(page.getByTestId(`setup-step-${step}`)).toBeVisible();
    await shot(page, language, `setup-${String(index + 8).padStart(2, '0')}-configure-${step}`);
    await nextStep();
  }

  // The demo pack step appears only when this build offers it.
  if (await heading('setup:configure.steps.demo').isVisible()) await nextStep();
  await expect(heading('setup:configure.steps.finish')).toBeVisible();
  await page.getByRole('button', { name: t('setup:configure.finish') }).click();
  await expect(page).toHaveURL(/\/$/u);
  await expect(page.getByText(t('setup:configure.finished'), { exact: true })).toBeVisible();
  await shot(page, language, 'setup-14-finished');
  const completed = (await (await page.request.get('/api/v1/setup/status')).json()) as {
    setupCompleted: boolean;
  };
  expect(completed.setupCompleted).toBe(true);

  // Branding from the wizard is in place, and the About dialog shows the version.
  await page.getByTestId('profile-menu').click();
  await page.getByTestId('about-menu').click();
  await expect(page.getByTestId('about-version')).toContainText('1.0.0');
  await shot(page, language, 'setup-15-about');
});
