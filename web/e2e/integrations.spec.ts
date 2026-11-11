import { expect, screenshotPath, test, tr } from './fixtures';

test.use({ signedInAs: 'mariam' });

/**
 * Phase 22 (SPEC-ADDENDUM §F, ADR 0191–0195): an administrator creates an integration client, gives it roles, subscribes a webhook and
 * sends a test; the developer portal shows the feeds, metadata, webhook catalogue and the Power BI guide; the client reads the OData feed
 * with HTTP Basic credentials, as Power BI / Excel do.
 */
test('integration client, permissions, webhooks, delivery log, developer portal and an OData feed read with Basic', async ({
  page,
  language,
  shot,
  browser,
  baseURL,
}) => {
  const i = (key: string, params?: Record<string, string | number>) =>
    tr(language, `integration:${key}`, params);
  const tag = `${language}${Date.now().toString(36)}`;
  const name = language === 'ar' ? `Power BI مكتب الاستراتيجية ${tag}` : `Power BI — Strategy Office ${tag}`;

  // 1. New client (API key) for the Sample and Portfolio systems.
  await page.goto('/settings/integrations/clients');
  await expect(page.getByRole('heading', { level: 1, name: i('clients.title') })).toBeVisible();
  await page.getByRole('button', { name: i('clients.create') }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByLabel(i('fields.nameAr')).fill(`باور بي آي ${tag}`);
  await dialog.getByLabel(i('fields.nameEn')).fill(name);
  await dialog.getByTestId('system-sample').getByRole('checkbox').first().check();
  await dialog.getByTestId('system-portfolio').getByRole('checkbox').first().check();
  await dialog.getByLabel(i('clients.ipAllowList')).fill('');
  await shot('integration-client-editor');
  await dialog.getByRole('button', { name: tr(language, 'common:actions.save') }).click();

  const secret = page.getByTestId('client-secret');
  await expect(secret).toBeVisible();
  const codes = secret.locator('code');
  const clientId = ((await codes.nth(0).textContent()) ?? '').trim();
  const key = ((await codes.nth(1).textContent()) ?? '').trim();
  expect(clientId).toMatch(/^ic_/u);
  expect(key).toMatch(/^sik_/u);
  await shot('integration-client-secret');
  await page
    .getByRole('dialog')
    .getByRole('button', { name: tr(language, 'common:actions.close') })
    .last()
    .click();
  await expect(page.getByTestId(`client-${clientId}`)).toBeVisible();
  await shot('integration-clients', { fullPage: true });

  // 2. Permissions: the sample viewer role (read, all records).
  await page.getByTestId(`client-${clientId}`).getByRole('link').click();
  await page.getByRole('tab', { name: i('clients.tabs.permissions') }).click();
  const viewer = page.getByTestId('role-viewer').first();
  await viewer.getByRole('checkbox').check();
  await page.getByRole('button', { name: tr(language, 'common:actions.save') }).click();
  await expect(page.getByText(tr(language, 'common:status.saved')).first()).toBeVisible();
  await shot('integration-client-permissions', { fullPage: true });

  // 3. Webhook subscription with its signing secret, then a test delivery in the log.
  await page.getByRole('tab', { name: i('clients.tabs.webhooks') }).click();
  await page.getByRole('button', { name: i('webhooks.create') }).click();
  const hook = page.getByRole('dialog');
  await hook.getByLabel(i('webhooks.name')).fill('ERP');
  await hook.getByLabel(i('webhooks.url')).fill('https://erp.example.test/hooks/strategy');
  await hook.getByRole('checkbox').first().check();
  await shot('integration-webhook-editor');
  await hook.getByRole('button', { name: tr(language, 'common:actions.save') }).click();
  await expect(page.getByRole('dialog').getByText(i('webhooks.secretTitle'))).toBeVisible();
  await page
    .getByRole('dialog')
    .getByRole('button', { name: tr(language, 'common:actions.close') })
    .last()
    .click();
  await page
    .getByTestId('webhook-ERP')
    .getByRole('button', { name: i('webhooks.sendTest') })
    .click();
  await expect(page.getByText(i('webhooks.testQueued'))).toBeVisible();
  await shot('integration-webhooks', { fullPage: true });
  await page.getByRole('tab', { name: i('clients.tabs.deliveries') }).click();
  await expect(page.getByTestId('delivery-log').getByRole('row')).not.toHaveCount(0);
  await shot('integration-delivery-log', { fullPage: true });

  // 4. Developer portal: feeds with metadata, webhook catalogue with a sample payload, Power BI guide.
  await page.goto('/settings/integrations/developer');
  await expect(page.getByRole('heading', { level: 1, name: i('portal.title') })).toBeVisible();
  await expect(page.getByTestId('odata-metadata')).toContainText('EntityType');
  await shot('developer-portal', { fullPage: true });
  await page.getByRole('tab', { name: i('portal.tabs.webhooks') }).click();
  await expect(page.getByTestId('sample-payload')).toContainText('"resource"');
  await shot('developer-portal-webhooks', { fullPage: true });
  await page.getByRole('tab', { name: i('portal.tabs.powerbi') }).click();
  await expect(page.getByTestId('power-bi-guide')).toBeVisible();
  await shot('developer-portal-power-bi', { fullPage: true });

  // 5. The client reads the feed with HTTP Basic, as Power BI's OData connector does (service document, metadata, rows with labels).
  const bi = await browser.newContext({
    baseURL,
    httpCredentials: { username: clientId, password: key },
    locale: language,
  });
  const feed = await bi.request.get(`/odata/v1/sample/Items?$top=3&$count=true&lang=${language}`);
  expect(feed.status()).toBe(200);
  const json = (await feed.json()) as { value: { statusLabel?: string }[]; '@odata.count': number };
  expect(json['@odata.count']).toBeGreaterThanOrEqual(0);
  const biPage = await bi.newPage();
  await biPage.goto('/odata/v1/sample/$metadata');
  await expect(biPage.locator('body')).toContainText('Strategy.Sample');
  await biPage.screenshot({ path: screenshotPath(language, 'odata-metadata-basic') });
  await biPage.goto(`/odata/v1/sample/Items?$top=3&$select=number,nameEn,statusLabel&lang=${language}`);
  await biPage.screenshot({ path: screenshotPath(language, 'odata-feed-basic') });
  await bi.close();

  // 6. Deliveries page (all clients, dead letters first).
  await page.goto('/settings/integrations/deliveries');
  await expect(page.getByRole('heading', { level: 1, name: i('deliveries.title') })).toBeVisible();
  await shot('integration-deliveries', { fullPage: true });
});
