import type { APIRequestContext, Page } from '@playwright/test';
import { apiPost, apiPut, expect, nextTotp, seed, test, tr, unitIdByCode } from './fixtures';

test.use({ signedInAs: 'mariam' });
test.setTimeout(300_000);

const Sync = '/api/v1/integration/sync';
const Objects = [
  'department',
  'user',
  'program-request',
  'project',
  'work-plan',
  'files',
  'approval-history',
];

interface Connection {
  id: string;
  mode: string;
  version: number;
  mappingPackId: string;
}

async function json<T>(response: {
  ok(): boolean;
  status(): number;
  text(): Promise<string>;
  json(): Promise<unknown>;
}): Promise<T> {
  expect(response.ok(), `${response.status()} ${await response.text()}`).toBeTruthy();
  return (await response.json()) as T;
}

async function connection(api: APIRequestContext): Promise<Connection> {
  const list = await json<Connection[]>(await api.get(`${Sync}/connections`));
  const first = list[0];
  expect(first).toBeDefined();
  return first as Connection;
}

/** Waits until a run that started after `since` has finished (the scheduler job runs every minute in Development). */
async function waitForRun(api: APIRequestContext, id: string, since: number) {
  await expect
    .poll(
      async () => {
        const status = await json<{ lastRun?: { startedAt: string; status: string } }>(
          await api.get(`${Sync}/connections/${id}/status`),
        );
        return status.lastRun && Date.parse(status.lastRun.startedAt) >= since - 1000
          ? status.lastRun.status
          : 'waiting';
      },
      { timeout: 150_000, intervals: [2000] },
    )
    .not.toBe('waiting');
}

async function syncNow(api: APIRequestContext, id: string, full = false) {
  const since = Date.now();
  await json(await apiPost(api, `${Sync}/connections/${id}/sync-now`, { full }));
  await waitForRun(api, id, since);
}

/**
 * Brings the connection to standby with the mock org (Development:MockSalesforce) — idempotent, so the Arabic and the English run
 * each start from standby: an outage left open by an earlier run is returned and closed first.
 */
async function standby(api: APIRequestContext): Promise<Connection> {
  let c = await connection(api);
  if (c.mode === 'primary') {
    await json(await apiPost(api, `${Sync}/connections/${c.id}/return-to-standby`, { acknowledged: true }));
    c = await connection(api);
  }

  if (c.mode === 'reconciling') {
    const status = await json<{ outage?: { id: string } }>(
      await api.get(`${Sync}/connections/${c.id}/status`),
    );
    const outageId = status.outage?.id ?? '';
    const conflicts = await json<{ recordId: string; conflict: boolean }[]>(
      await api.get(`${Sync}/outages/${outageId}/conflicts`),
    );
    for (const conflict of conflicts) {
      await json(
        await apiPost(api, `${Sync}/outages/${outageId}/conflicts/${conflict.recordId}/resolve`, {
          choice: 'take_source',
        }),
      );
    }

    await json(await apiPost(api, `${Sync}/connections/${c.id}/close-outage`));
    c = await connection(api);
  }

  if (c.mode === 'disabled') {
    const mock = await json<{
      loginUrl: string;
      clientId: string;
      username: string;
      apiVersion: string;
      privateKeyPem: string;
    }>(await api.get('/dev/mock-salesforce'));
    await json(await apiPut(api, `${Sync}/connections/${c.id}/key`, { privateKeyPem: mock.privateKeyPem }));
    c = await connection(api);
    await json(
      await apiPut(api, `${Sync}/connections/${c.id}`, {
        loginUrl: mock.loginUrl,
        clientId: mock.clientId,
        username: mock.username,
        apiVersion: mock.apiVersion,
        bulkThreshold: 2000,
        pageSize: 2000,
        mappingPackId: c.mappingPackId,
        version: c.version,
      }),
    );
    const tested = await json<{ lastTest: { ok: boolean; messageKey?: string } }>(
      await apiPost(api, `${Sync}/connections/${c.id}/test`),
    );
    expect(tested.lastTest.ok, tested.lastTest.messageKey).toBeTruthy();
    const mappings = await json<{ id: string; key: string; enabled: boolean }[]>(
      await api.get(`${Sync}/packs/${c.mappingPackId}/mappings`),
    );
    for (const mapping of mappings.filter((m) => Objects.includes(m.key) && !m.enabled)) {
      const validated = await json<{ ok: boolean; issues: unknown[] }>(
        await apiPost(api, `${Sync}/connections/${c.id}/mappings/${mapping.id}/validate`),
      );
      expect(validated.ok, JSON.stringify(validated.issues)).toBeTruthy();
      const current = await json<{ version: number }>(await api.get(`${Sync}/mappings/${mapping.id}`));
      await json(
        await apiPost(api, `${Sync}/mappings/${mapping.id}/enabled`, {
          enabled: true,
          version: current.version,
        }),
      );
    }

    await json(await apiPost(api, `${Sync}/connections/${c.id}/activate`));
    c = await connection(api);
    await syncNow(api, c.id, true);
  }

  return c;
}

/**
 * Leaves the connection disabled after the spec: while it is in standby, the business systems' jobs and notifications are suspended and
 * synced records are read-only, which would change every spec that runs after this one (in either language project).
 */
test.afterEach(async ({ adminApi }) => {
  const c = await standby(adminApi);
  await json(await apiPost(adminApi, `${Sync}/connections/${c.id}/deactivate`));
  expect((await connection(adminApi)).mode).toBe('disabled');
});

async function openTab(page: Page, language: 'ar' | 'en', tab: string) {
  await page.getByRole('tab', { name: tr(language, `integration:sync.tabs.${tab}`) }).click();
}

/**
 * Phase 23 (SPEC-ADDENDUM §G, ADR 0200–0208) against the Development stand-in org: connection, schedule, mapping editor with the live
 * describe, status after a full load, the exceptions queue, a synced record read-only with its badge, "Make this system primary" with the
 * MFA re-prompt, the outage changes report, and "Return to standby" with a conflict to decide.
 */
test('Salesforce standby sync: connection, mappings, status, exceptions, read-only record, failover, outage report and return to standby', async ({
  page,
  language,
  shot,
  adminApi,
}) => {
  const c = await standby(adminApi);

  // Status, connection, schedule, mapping editor.
  await page.goto('/settings/integrations/salesforce-sync');
  await expect(page.getByTestId('sync-mode')).toHaveText(tr(language, 'integration:sync.modes.standby'));
  await expect(page.getByTestId('sync-object-project')).toBeVisible();
  await shot('sync-status', { fullPage: true });

  await openTab(page, language, 'connection');
  await expect(page.getByTestId('key-state')).toBeVisible();
  await shot('sync-connection', { fullPage: true });

  await openTab(page, language, 'schedule');
  await expect(page.getByTestId('next-runs')).toBeVisible();
  await shot('sync-schedule', { fullPage: true });

  await openTab(page, language, 'mappings');
  await page
    .getByTestId('mapping-project')
    .getByRole('button', { name: tr(language, 'common:actions.edit') })
    .click();
  await expect(page.getByTestId('describe-panel')).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('field-row-0')).toBeVisible();
  await shot('sync-mapping-editor');
  await page.keyboard.press('Escape');

  // Exceptions: a new Salesforce status value that has no code here yet.
  const pending = `PR-E2E-${language}-${Date.now().toString(36)}`;
  const requestId = await json<string>(
    await apiPost(adminApi, '/dev/mock-salesforce/change', {
      objectName: 'PMS_Project_Request__c',
      field: 'Name',
      value: pending,
    }),
  );
  await json(
    await apiPost(adminApi, '/dev/mock-salesforce/change', {
      objectName: 'PMS_Project_Request__c',
      id: requestId,
      field: 'Status__c',
      value: 'On Hold',
    }),
  );
  await syncNow(adminApi, c.id);
  await page.reload();
  await openTab(page, language, 'exceptions');
  await expect(page.getByTestId(`exception-${requestId}`)).toBeVisible();
  await shot('sync-exceptions', { fullPage: true });

  // The synced project is read-only while Salesforce is primary.
  const projects = await json<{ items: { id: string; number: string }[] }>(
    await adminApi.get('/api/v1/portfolio/projects?search=PROJ-SPMO-2026-014'),
  );
  const projectId = projects.items[0]?.id ?? '';
  expect(projectId).not.toBe('');
  await page.goto(`/portfolio/projects/${projectId}`);
  await expect(
    page
      .getByText(
        tr(language, 'integration:sync.badges.readOnly', {
          source: language === 'ar' ? 'سيلزفورس' : 'Salesforce',
        }),
      )
      .first(),
  ).toBeVisible();
  await shot('sync-read-only-record');

  // Make this system primary (reason + verification code).
  await page.goto('/settings/integrations/salesforce-sync');
  await openTab(page, language, 'failover');
  await page.getByRole('button', { name: tr(language, 'integration:sync.actions.makePrimary') }).click();
  const dialog = page.getByRole('dialog');
  await dialog
    .getByLabel(tr(language, 'integration:sync.failover.reason'))
    .fill(
      language === 'ar' ? 'انقطاع خدمة Salesforce منذ الثامنة صباحاً' : 'Salesforce unavailable since 08:00',
    );
  await dialog
    .getByLabel(tr(language, 'integration:sync.failover.codeLabel.totp'))
    .fill(await nextTotp(seed.adminTotpKey));
  await shot('sync-make-primary');
  await dialog
    .getByRole('button', { name: tr(language, 'integration:sync.failover.confirmPrimary') })
    .click();
  await expect(page.getByTestId('sync-mode')).toHaveText(tr(language, 'integration:sync.modes.primary'));

  // A change here during the outage: the synced department is renamed.
  const unitId = await unitIdByCode(adminApi, 'SF-IT');
  const unit = await json<{ code: string; nameAr: string; levelCode: string; version: number }>(
    await adminApi.get(`/api/v1/org/units/${unitId}`),
  );
  const renamed = language === 'ar' ? 'إدارة التقنية والتحول الرقمي' : 'Technology and Digital Department';
  await json(
    await apiPut(adminApi, `/api/v1/org/units/${unitId}`, {
      id: unitId,
      code: unit.code,
      nameAr: unit.nameAr,
      nameEn: renamed,
      levelCode: unit.levelCode,
      version: unit.version,
    }),
  );

  // The outage changes report lists it with the Salesforce ID.
  await page.reload();
  await openTab(page, language, 'failover');
  await expect(page.getByTestId('outage-report')).toBeVisible();
  await expect(page.getByTestId('outage-report').getByText(renamed).first()).toBeVisible();
  await shot('sync-outage-report', { fullPage: true });

  // Return to standby; Salesforce then sends its own version of the department → a conflict to decide.
  await page.getByLabel(tr(language, 'integration:sync.outage.ackCheckbox')).check();
  await page.getByRole('button', { name: tr(language, 'integration:sync.actions.returnToStandby') }).click();
  const confirm = page.getByRole('dialog');
  if (await confirm.isVisible()) {
    await confirm
      .getByRole('button', { name: tr(language, 'integration:sync.actions.returnToStandby') })
      .click();
  }

  await expect(page.getByTestId('sync-mode')).toHaveText(tr(language, 'integration:sync.modes.reconciling'));
  const refs = await json<{ references: { externalId: string; sourceSystem: string }[] }>(
    await adminApi.get(`/api/v1/integration/references/org.unit/${unitId}`),
  );
  const departmentId = refs.references.find((r) => r.sourceSystem === 'salesforce')?.externalId ?? '';
  await json(
    await apiPost(adminApi, '/dev/mock-salesforce/change', {
      objectName: 'Department__c',
      id: departmentId,
      field: 'Department_Name_English__c',
      value: `Technology Department (Salesforce ${language})`,
    }),
  );
  await syncNow(adminApi, c.id);
  await page.reload();
  await openTab(page, language, 'failover');
  await expect(page.getByTestId(`conflict-${unitId}`)).toBeVisible();
  await shot('sync-return-to-standby', { fullPage: true });

  // Keep local, then close the outage: back to standby.
  await page
    .getByTestId(`conflict-${unitId}`)
    .getByRole('button', { name: tr(language, 'integration:sync.conflicts.keepLocal') })
    .click();
  await expect(
    page.getByRole('button', { name: tr(language, 'integration:sync.actions.closeOutage') }),
  ).toBeEnabled();
  await page.getByRole('button', { name: tr(language, 'integration:sync.actions.closeOutage') }).click();
  const close = page.getByRole('dialog');
  if (await close.isVisible()) {
    await close.getByRole('button', { name: tr(language, 'integration:sync.actions.closeOutage') }).click();
  }

  await expect(page.getByTestId('sync-mode')).toHaveText(tr(language, 'integration:sync.modes.standby'));
});
