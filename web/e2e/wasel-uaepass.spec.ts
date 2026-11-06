import type { APIRequestContext } from '@playwright/test';
import {
  apiPost,
  apiPut,
  expect,
  latestEmail,
  nextTotp,
  screenshotPath,
  signInViaApi,
  test,
  useMailpit,
} from './fixtures';

/**
 * Phase 18 — UAE Pass sign-in for Wasel vendors (ADR 0154), in Arabic (RTL) and English (LTR). The installation's UAE Pass provider points at the
 * Development stand-in issuer (`Development__MockUaePass=true`); Settings → Portfolio → Wasel allows UAE Pass for vendors. The vendor signs in with
 * UAE Pass, is matched by e-mail and must still complete two-step verification. Screenshots: docs/screenshots/phase-18 (E2E_SCREENSHOTS=phase-18).
 */

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

const wasel = '/api/wasel/v1';
const year = new Date().getFullYear();
const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');
const base = (id: string) => `/api/v1/portfolio/projects/${id}`;

async function antiforgery(api: APIRequestContext) {
  const token = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [token.headerName]: token.token, 'X-Client': 'spa' };
}

async function approve(admin: APIRequestContext, subject: string, id: string, times: number) {
  for (let i = 0; i < times; i++) {
    const panel = await json<{ instances: { myTask: { taskId: string } | null }[] }>(
      await admin.get(`/api/v1/workflow/subjects/${subject}/${id}`),
    );
    const task = panel.instances.map((x) => x.myTask).find((t) => t !== null);
    if (!task) throw new Error(`No approval task on ${subject} ${id}`);
    await json(
      await apiPost(admin, `/api/v1/workflow/tasks/${task.taskId}/act`, { action: 'approve', comment: null }),
    );
  }
}

/** An initiative in planning with Ahmed as PM (no charter: planning directly, Q-P2). */
async function initiative(ahmed: APIRequestContext, admin: APIRequestContext, name: string) {
  const values = {
    nameAr: `مبادرة ${name}`,
    nameEn: name,
    description: 'Vendor-delivered online services.',
    justifications: 'Applicants wait too long today.',
    strategicObjectiveCode: 'digital-government',
    suggestionSourceCode: 'idea',
    programTypeCode: 'digital',
    requiresBudget: true,
    estimatedBudget: 40_000,
    year1Cost: 28_000,
    year2Cost: 4_000,
    year3Cost: 4_000,
    year4Cost: 4_000,
    costPath: 'cost-estimation',
    expectedStartDate: `${year + 1}-01-01`,
    expectedEndDate: `${year + 1}-12-31`,
    programNature: 'new',
    scopeOfWork: 'Online forms, payments and tracking delivered by a vendor.',
    screenCoreWork: true,
    screenOneTime: true,
    screenTimeBound: true,
    screenUniqueOutput: true,
    screenMinCost: false,
    screenOperationalCosts: true,
    priorityScores: { 'vision-alignment': 5, impact: 4, complexity: 3, 'radical-change': 4 },
    mainOptionCode: 'contracting',
    practiceService: 'Permits service',
  };
  const request = await json<{ id: string }>(await apiPost(ahmed, '/api/v1/portfolio/requests', { values }));
  const upload = await ahmed.post(`/api/v1/portfolio/requests/${request.id}/files/cost-estimation`, {
    multipart: { file: { name: 'cost.pdf', mimeType: 'application/pdf', buffer: pdf } },
    headers: await antiforgery(ahmed),
  });
  expect(upload.ok(), await upload.text()).toBe(true);
  const draft = await json<{ version: number }>(await ahmed.get(`/api/v1/portfolio/requests/${request.id}`));
  await json(
    await apiPost(ahmed, `/api/v1/portfolio/requests/${request.id}/submit`, { version: draft.version }),
  );
  await approve(admin, 'portfolio.request', request.id, 3);
  const approved = await json<{ project: { id: string } | null }>(
    await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
  );
  const id = approved.project?.id ?? '';
  expect(id).not.toBe('');
  const p = await json<{ version: number }>(await ahmed.get(base(id)));
  await json(await apiPost(ahmed, `${base(id)}/take-ownership`, { version: p.version }));
  await json(
    await apiPost(ahmed, `${base(id)}/planning-items`, {
      values: { category: 'in-scope', description: 'Online services' },
    }),
  );
  return id;
}

const providers = '/api/v1/identity/admin/providers';

async function ensureProvider(admin: APIRequestContext, baseURL: string) {
  const list = await json<{ id: string; code: string; enabled: boolean }[]>(await admin.get(providers));
  let provider: { id: string; enabled: boolean } | undefined = list.find((p) => p.code === 'e2e-uaepass');
  if (!provider) {
    const root = `${baseURL.replace(/\/$/u, '')}/dev/mock-uaepass`;
    provider = await json<{ id: string; enabled: boolean }>(
      await apiPost(admin, providers, {
        code: 'e2e-uaepass',
        type: 'uaepass',
        displayNameAr: 'الهوية الرقمية',
        displayNameEn: 'UAE Pass',
        sort: 50,
        autoLink: false,
        jitCreateUser: false,
        secret: 'mock-uaepass-secret',
        matchField: 'email',
        settings: {
          environment: 'staging',
          clientId: 'mock-uaepass-client',
          authorizationEndpoint: `${root}/authorize`,
          tokenEndpoint: `${root}/token`,
          userInfoEndpoint: `${root}/userinfo`,
          allowInsecureHttp: true,
          allowedUserTypes: 'SOP2,SOP3',
        },
      }),
    );
  }
  if (!provider.enabled) {
    await json(await apiPost(admin, `${providers}/${provider.id}/enable`));
  }
}

async function vendorOption(admin: APIRequestContext, on: boolean) {
  const current = await json<{ value: { wasel: Record<string, unknown> }; version: number }>(
    await admin.get('/api/v1/portfolio/settings'),
  );
  await json(
    await apiPut(admin, '/api/v1/portfolio/settings', {
      settings: { ...current.value, wasel: { ...current.value.wasel, uaePassForVendors: on } },
      version: current.version,
    }),
  );
}

test.describe('wasel — UAE Pass for vendors', () => {
  test.use({ signedInAs: null });

  test('a vendor signs in with UAE Pass, is matched by e-mail and still sets up two-step verification', async ({
    page,
    language,
    shot,
    adminApi,
    baseURL,
    playwright,
  }) => {
    test.setTimeout(300_000);
    const mock = await page.request.get('/dev/mock-uaepass/authorize');
    test.skip(
      !mock.ok(),
      'The Development stand-in UAE Pass issuer is not mapped (Development__MockUaePass=true).',
    );
    const tag = `${language}${Date.now().toString(36)}`;
    await useMailpit(adminApi, baseURL ?? '');
    await ensureProvider(adminApi, baseURL ?? '');
    await vendorOption(adminApi, true);
    try {
      // A vendor account (invitation → password), as in the Phase 11 flow, on a project of the demo PM Office.
      const vendor = await json<{ id: string }>(
        await apiPost(adminApi, '/api/v1/portfolio/vendors', {
          values: {
            nameAr: `شركة الهوية ${tag}`,
            nameEn: `Identity Works ${tag}`,
            tradeLicenceNo: `TL-UP-${tag}`,
            email: 'info@identity.example',
          },
        }),
      );
      const ahmed = await playwright.request.newContext({ baseURL });
      await signInViaApi(ahmed, 'ahmed');
      const projectId = await initiative(ahmed, adminApi, `UAE Pass vendor ${tag}`);
      await apiPost(ahmed, `/api/v1/portfolio/projects/${projectId}/wasel/assignments`, {
        vendorId: vendor.id,
        accessUntil: `${year + 1}-12-31`,
        expiryRule: 'date',
      });
      const email = `lead.${tag}@identity.example`;
      const since = new Date(Date.now() - 1000);
      await json(
        await apiPost(ahmed, `/api/v1/portfolio/projects/${projectId}/wasel/invitations`, {
          vendorId: vendor.id,
          email,
          name: 'Identity lead',
          role: 'vendor-lead',
          language,
        }),
      );
      const mail = await latestEmail(page, email, since);
      const link = new URL(/https?:\/\/\S*\/wasel\/set-password\?\S+/u.exec(mail.text)?.[0] ?? '');

      // The sign-in page offers UAE Pass next to the password form.
      await page.goto('/wasel/login');
      const button = page.getByTestId('wasel-login-uaepass');
      await expect(button).toBeVisible();
      await shot('wasel-uaepass-login');

      // An unknown UAE Pass e-mail is refused without creating anything.
      await button.click();
      await page.locator('#email').fill(`nobody.${tag}@unknown.example`);
      await page.getByRole('button').click();
      await page.waitForURL(/\/wasel\/login\?error=/u);
      await expect(page.getByRole('alert')).toBeVisible();
      await shot('wasel-uaepass-no-account');

      // The invited vendor (password set from the invitation) signs in with UAE Pass: two-step verification still follows.
      await page.goto(`${link.pathname}${link.search}`);
      await page.getByTestId('wasel-new-password').fill('Vendor#Portal2026');
      await page.getByTestId('wasel-confirm-password').fill('Vendor#Portal2026');
      await page.getByTestId('wasel-set-password').click();
      await page.waitForURL(/\/wasel\/login/u);
      await page.getByTestId('wasel-login-uaepass').click();
      await expect(page.locator('#email')).toBeVisible();
      await page.locator('#email').fill(email);
      await page.getByRole('button').click();
      await page.waitForURL(/\/wasel\/login\/mfa/u);
      await expect(page.getByTestId('wasel-mfa-enrol')).toBeVisible();
      await page.screenshot({ path: screenshotPath(language, 'wasel-uaepass-mfa') });

      // No session before the second factor.
      expect((await page.request.get(`${wasel}/me`)).status()).toBe(401);
      const token = (await (await page.request.get(`${wasel}/antiforgery/token`)).json()) as {
        token: string;
        headerName: string;
      };
      const headers = { [token.headerName]: token.token, 'X-Client': 'spa' };
      const setup = await json<{ sharedKey: string }>(
        await page.request.post(`${wasel}/auth/mfa/enrol/totp`, { headers }),
      );
      await json(
        await page.request.post(`${wasel}/auth/mfa/enrol/confirm`, {
          headers,
          data: { method: 'totp', code: await nextTotp(setup.sharedKey) },
        }),
      );
      await page.goto('/wasel');
      await expect(page.getByTestId('wasel-projects')).toBeVisible();
      await shot('wasel-uaepass-signed-in');
    } finally {
      await vendorOption(adminApi, false);
    }
  });
});
