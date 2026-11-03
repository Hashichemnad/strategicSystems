import type { APIRequestContext, Browser, Page } from '@playwright/test';
import { createHash, randomBytes } from 'node:crypto';
import { apiPost, expect, savePreferences, screenshotPath, test, tr, type Language } from './fixtures';

/** Phase 5: AI platform (demo provider, offline) and the MCP server, in Arabic and English. */

async function mariamPage(browser: Browser, adminApi: APIRequestContext, language: Language): Promise<Page> {
  const context = await browser.newContext({
    locale: language === 'ar' ? 'ar-AE' : 'en-GB',
    viewport: { width: 1366, height: 860 },
  });
  await context.addInitScript((lang) => {
    localStorage.setItem('strategy.language', lang);
    localStorage.setItem('strategy.theme', 'light');
  }, language);
  await context.addCookies((await adminApi.storageState()).cookies);
  const page = await context.newPage();
  await savePreferences(page, language);
  return page;
}

const base64Url = (bytes: Buffer) =>
  bytes.toString('base64').replace(/=+$/u, '').replace(/\+/gu, '-').replace(/\//gu, '_');

test.describe('AI and MCP settings', () => {
  test.use({ signedInAs: 'mariam' });

  test('an administrator adds a provider and passes Test connection; AI features and MCP tools are configured', async ({
    page,
    language,
    shot,
  }) => {
    const ai = (key: string, params?: Record<string, string | number>) => tr(language, `ai:${key}`, params);
    const tag = `${language}${Date.now().toString(36)}`;

    await page.goto('/settings/ai');
    await expect(page.getByRole('heading', { level: 1, name: ai('settings.title') })).toBeVisible();
    await page.getByRole('button', { name: ai('settings.providers.add') }).click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(ai('settings.fields.name')).fill(`Claude (demo ${tag})`);
    await dialog.getByRole('combobox').first().click();
    await page.getByRole('option', { name: ai('settings.types.demo') }).click();
    await dialog.getByLabel(ai('settings.fields.defaultModel')).fill('demo-large');
    await dialog.getByRole('button', { name: tr(language, 'actions.save') }).click();
    await expect(dialog).toBeHidden();

    const card = page.getByTestId('ai-provider-card').filter({ hasText: `Claude (demo ${tag})` });
    await card.getByRole('button', { name: ai('settings.providers.test') }).click();
    await expect(
      card.getByText(
        new RegExp(
          ai('settings.providers.testOk', { ms: 'MS', model: 'demo-large' })
            .replace('MS', '.+')
            .replace(/[()]/gu, '\\$&'),
          'u',
        ),
      ),
    ).toBeVisible();
    // Let the "saved" toast go, then show the tested provider.
    await expect(page.getByRole('status').filter({ hasText: ai('settings.providers.saved') })).toHaveCount(
      0,
      { timeout: 15_000 },
    );
    await card.scrollIntoViewIfNeeded();
    await shot('ai-settings-test-connection');

    await page.getByRole('tab', { name: ai('settings.tabs.features') }).click();
    await expect(page.getByRole('cell', { name: ai('features.extract') })).toBeVisible();
    await shot('ai-settings-features');
    await page.getByRole('tab', { name: ai('settings.tabs.data') }).click();
    await expect(page.getByText(ai('settings.data.mask'))).toBeVisible();
    await shot('ai-settings-data', { fullPage: true });

    // MCP: approve tools are listed but off by default.
    const mcp = (key: string) => tr(language, `mcp:${key}`);
    await page.goto('/settings/mcp');
    await expect(page.getByRole('heading', { level: 1, name: mcp('settings.title') })).toBeVisible();
    await expect(page.getByTestId('mcp-tool-approval_decide').getByRole('switch')).not.toBeChecked();
    await expect(page.getByTestId('mcp-tool-search_records').getByRole('switch')).toBeChecked();
    await shot('mcp-settings', { fullPage: true });
  });

  test('an MCP client asks to connect: consent page, then the code goes back to the client', async ({
    page,
    language,
    shot,
    baseURL,
  }) => {
    const mcp = (key: string, params?: Record<string, string>) => tr(language, `mcp:${key}`, params);
    const redirect = 'http://127.0.0.1:33418/callback';
    const register = await page.request.post('/connect/register', {
      data: { client_name: 'Claude Code', redirect_uris: [redirect], token_endpoint_auth_method: 'none' },
    });
    expect(register.status()).toBe(201);
    const { client_id: clientId } = (await register.json()) as { client_id: string };
    const challenge = base64Url(
      createHash('sha256')
        .update(base64Url(randomBytes(32)))
        .digest(),
    );
    const query = new URLSearchParams({
      client_id: clientId,
      redirect_uri: redirect,
      response_type: 'code',
      scope: 'mcp',
      state: 'e2e',
      code_challenge: challenge,
      code_challenge_method: 'S256',
      resource: `${baseURL ?? ''}/mcp`,
    });

    await page.goto(`/connect/authorize?${query.toString()}`);
    await expect(
      page.getByRole('heading', { name: mcp('consent.title', { client: 'Claude Code' }) }),
    ).toBeVisible();
    await shot('mcp-consent');
    // The browser goes back to the client's loopback address with the authorization code (no client listens in the test).
    const callback = page.waitForRequest(/127\.0\.0\.1:33418\/callback\?code=.+&state=e2e/u);
    await page.getByRole('button', { name: mcp('consent.allow') }).click();
    expect(new URL((await callback).url()).searchParams.get('code')).toBeTruthy();
  });
});

test.describe('AI features', () => {
  test('a form filled from a document shows highlighted drafts with sources; nothing is saved until reviewed', async ({
    page,
    language,
    shot,
  }) => {
    const ai = (key: string, params?: Record<string, string | number>) => tr(language, `ai:${key}`, params);
    const sm = (key: string) => tr(language, `sample:${key}`);
    const tag = `${language}${Date.now().toString(36)}`;
    const document = [
      'Business case',
      `Name (English): Digital archive programme ${tag}`,
      'الاسم بالعربية: برنامج الأرشيف الرقمي',
      'Description (English): Scan and index the paper archive so that records can be found in seconds.',
      'الوصف بالعربية: مسح الأرشيف الورقي وفهرسته ليسهل الوصول إلى السجلات خلال ثوانٍ.',
      'Amount (AED): 1,250,000',
    ].join('\n');

    await page.goto('/sample');
    await page.getByRole('button', { name: sm('new') }).click();
    const dialog = page.getByRole('dialog', { name: sm('createTitle') });
    await dialog.getByRole('button', { name: ai('fill.button') }).click();
    const upload = page.getByRole('dialog', { name: ai('fill.title') });
    await upload
      .locator('input[type="file"]')
      .setInputFiles({ name: 'business-case.txt', mimeType: 'text/plain', buffer: Buffer.from(document) });
    await upload.getByRole('button', { name: ai('fill.run') }).click();
    await expect(upload).toBeHidden();

    await expect(dialog.getByLabel(sm('fields.nameEn'))).toHaveValue(`Digital archive programme ${tag}`);
    await expect(dialog.getByLabel(sm('fields.amount'))).toHaveValue('1250000');
    await expect(dialog.getByText(ai('reviewPending', { count: 5 }))).toBeVisible();
    await expect(dialog.getByRole('button', { name: tr(language, 'actions.create') })).toBeDisabled();
    await dialog.getByTestId('ai-field-review').first().getByText(ai('source')).click();
    await shot('ai-form-review');

    await dialog.getByRole('button', { name: ai('acceptAll') }).click();
    await dialog.getByRole('button', { name: tr(language, 'actions.create') }).click();
    await expect(page.getByText(sm('toast.created')).first()).toBeVisible();
  });

  test('the approver opens an AI brief on the approval panel and can ask the system', async ({
    page,
    language,
    browser,
    adminApi,
  }) => {
    const ai = (key: string) => tr(language, `ai:${key}`);
    const tag = `${language}${Date.now().toString(36)}`;
    const created = await apiPost(page, '/api/v1/sample/items', {
      nameAr: 'خطة التحول الرقمي',
      nameEn: `Digital transformation plan ${tag}`,
      amount: 250000,
    });
    expect(created.status()).toBe(201);
    const item = (await created.json()) as { id: string; version: number };
    expect(
      (
        await apiPost(page, `/api/v1/sample/items/${item.id}/submit`, {
          version: item.version,
          comment: 'Ready',
        })
      ).status(),
    ).toBe(200);

    const mariam = await mariamPage(browser, adminApi, language);
    await mariam.goto(`/sample/items/${item.id}?tab=approvals`);
    const brief = mariam.getByTestId('approver-brief');
    await brief.getByRole('button', { name: ai('brief.generate') }).click();
    await expect(brief.getByText(ai('generated'))).toBeVisible();
    await expect(brief.getByText(ai('brief.noChanges'))).toBeVisible();
    await mariam.evaluate(() => document.fonts.ready);
    await mariam.screenshot({ path: screenshotPath(language, 'approver-brief') });

    // Ask the system (read-only tools with Mariam's own permissions).
    await mariam.getByTestId('ask-button').click();
    const ask = mariam.getByRole('dialog', { name: ai('ask.title') });
    await ask.getByRole('textbox').fill(`Which items mention ${tag}?`);
    await ask.getByRole('button', { name: ai('ask.send') }).click();
    await expect(ask.getByText(ai('generated'))).toBeVisible();
    await mariam.screenshot({ path: screenshotPath(language, 'ask-the-system') });
    await mariam.context().close();
  });
});
