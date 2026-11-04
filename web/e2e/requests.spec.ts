import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { APIRequestContext, APIResponse, Browser, Page } from '@playwright/test';
import {
  apiPost,
  apiPut,
  expect,
  savePreferences,
  screenshotPath,
  signInViaApi,
  test,
  unitIdByCode,
  type Language,
} from './fixtures';

/**
 * Phase 14 — Request Framework, Document Registry, IP Registry (docs/design/requests.md). Setup goes through the API; every screen is shown
 * in the UI. Screenshots go to docs/screenshots/phase-14 (E2E_SCREENSHOTS=phase-14).
 */

async function json<T>(response: APIResponse): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

async function antiforgery(api: APIRequestContext): Promise<Record<string, string>> {
  const { token, headerName } = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [headerName]: token, 'X-Client': 'spa' };
}

/** A small valid one-page PDF with a text layer (offsets computed), so previews, the stamp and text extraction work on it. */
function pdf(text: string): Buffer {
  const safe = text.replace(/[()\\]/gu, ' ');
  const stream = `BT /F1 14 Tf 60 760 Td (${safe}) Tj ET`;
  const objects = [
    '<< /Type /Catalog /Pages 2 0 R >>',
    '<< /Type /Pages /Kids [3 0 R] /Count 1 >>',
    '<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>',
    `<< /Length ${stream.length} >>\nstream\n${stream}\nendstream`,
    '<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>',
  ];
  let body = '%PDF-1.4\n';
  const offsets: number[] = [];
  objects.forEach((o, i) => {
    offsets.push(Buffer.byteLength(body));
    body += `${i + 1} 0 obj\n${o}\nendobj\n`;
  });
  const xref = Buffer.byteLength(body);
  body += `xref\n0 ${objects.length + 1}\n0000000000 65535 f \n`;
  for (const offset of offsets) body += `${String(offset).padStart(10, '0')} 00000 n \n`;
  body += `trailer\n<< /Size ${objects.length + 1} /Root 1 0 R >>\nstartxref\n${xref}\n%%EOF\n`;
  return Buffer.from(body, 'latin1');
}

async function upload(
  api: APIRequestContext,
  requestId: string,
  name: string,
  mimeType: string,
  buffer: Buffer,
) {
  const uploaded = await json<{ id: string }>(
    await api.post(`/api/v1/files/owners/requests.request/${requestId}`, {
      headers: await antiforgery(api),
      multipart: { file: { name, mimeType, buffer } },
    }),
  );
  return uploaded.id;
}

interface RequestRecord {
  id: string;
  number: string;
  version: number;
  status: string;
  subjectId: string | null;
}

async function draft(api: APIRequestContext, typeCode: string, data: object) {
  return json<RequestRecord>(await apiPost(api, '/api/v1/requests', { typeCode, data }));
}

async function save(api: APIRequestContext, request: RequestRecord, data: object) {
  return json<RequestRecord>(
    await apiPut(api, `/api/v1/requests/${request.id}`, { version: request.version, data }),
  );
}

async function submit(api: APIRequestContext, request: RequestRecord) {
  return json<RequestRecord>(
    await apiPost(api, `/api/v1/requests/${request.id}/submit`, { version: request.version }),
  );
}

async function decide(api: APIRequestContext, id: string, form?: object) {
  return json<RequestRecord>(
    await apiPost(api, `/api/v1/requests/${id}/decide`, { action: 'approve', form }),
  );
}

async function act(api: APIRequestContext, id: string, action: string, form?: object) {
  const current = await json<RequestRecord>(await api.get(`/api/v1/requests/${id}`));
  return json<RequestRecord>(
    await apiPost(api, `/api/v1/requests/${id}/actions/${action}`, { version: current.version, form }),
  );
}

/** Ahmed submits a document; Mariam (Strategy Office) classifies, approves and publishes it. Returns the document id. */
async function publishDocument(
  ahmed: APIRequestContext,
  mariam: APIRequestContext,
  values: { nameAr: string; nameEn: string; ownerUnit: string; description: string; text: string },
  classification: { level: string; docType: string; sensitivity: string },
  publish: { shareToAll?: boolean; effectiveDate?: string; expiryDate?: string; existing?: string } = {},
) {
  let request = await draft(ahmed, 'docreg.document', { nameAr: values.nameAr, nameEn: values.nameEn });
  const file = await upload(ahmed, request.id, 'draft.txt', 'text/plain', Buffer.from(values.text));
  request = await save(ahmed, request, {
    isNewVersion: publish.existing !== undefined,
    existingDocument: publish.existing,
    nameAr: values.nameAr,
    nameEn: values.nameEn,
    ownerUnit: values.ownerUnit,
    description: values.description,
    draftFile: file,
    changeReason: publish.existing ? 'Annual review: travel limit raised to 7000.' : undefined,
  });
  request = await submit(ahmed, request);
  await decide(mariam, request.id, classification);
  const finalFile = await upload(mariam, request.id, 'signed.pdf', 'application/pdf', pdf(values.text));
  const published = await act(mariam, request.id, 'publish', {
    finalFile,
    shareToAll: publish.shareToAll ?? false,
    effectiveDate: publish.effectiveDate,
    expiryDate: publish.expiryDate,
  });
  expect(published.status).toBe('completed');
  if (publish.existing) return publish.existing;
  const library = await json<{ page: { items: { id: string; nameEn: string }[] } }>(
    await mariam.get('/api/v1/docreg/library', { params: { search: values.nameEn, pageSize: 50 } }),
  );
  const found = library.page.items.find((d) => d.nameEn === values.nameEn);
  if (!found) throw new Error(`Document ${values.nameEn} not in the library`);
  return found.id;
}

async function pageAs(browser: Browser, baseURL: string, language: Language): Promise<Page> {
  const context = await browser.newContext({ baseURL, viewport: { width: 1366, height: 900 } });
  const page = await context.newPage();
  await page.addInitScript((lang) => {
    localStorage.setItem('strategy.language', lang);
    localStorage.setItem('strategy.theme', 'light');
  }, language);
  await signInViaApi(page, 'ahmed');
  await savePreferences(page, language);
  return page;
}

async function settle(page: Page) {
  await page.waitForLoadState('networkidle');
  await page.evaluate(() => document.fonts.ready);
}

function iso(date: Date) {
  return date.toISOString().slice(0, 10);
}

test.describe('requests, documents and intellectual property', () => {
  test.use({ signedInAs: 'mariam' });

  test('catalogue, form builder, document submission and review, library by sensitivity, compare, expiry reminder, IP request, MOET form, register and IP from a document', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    browser,
    baseURL,
  }) => {
    test.setTimeout(420_000);
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const strategy = await unitIdByCode(adminApi, 'DEP-STR');
    const ahmedApi = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmedApi, 'ahmed');
    const ahmed = await pageAs(browser, baseURL ?? '', language);

    // ---- Form builder: a custom type without code ------------------------------------------------------------
    const code = `training-${tag.toLowerCase()}`;
    await json(
      await apiPost(adminApi, '/api/v1/requests/admin/types', {
        code,
        nameAr: `طلب تدريب ${tag}`,
        nameEn: `Training request ${tag}`,
        descriptionAr: 'طلب حضور دورة تدريبية خارجية أو داخلية.',
        descriptionEn: 'Ask to attend an internal or external training course.',
        category: 'services',
        icon: 'school',
        numberPrefix: 'TRN',
        submitters: { everyone: true },
        form: {
          sections: [
            {
              code: 'course',
              titleAr: 'الدورة',
              titleEn: 'Course',
              fields: [
                {
                  code: 'courseName',
                  type: 'text',
                  labelAr: 'اسم الدورة',
                  labelEn: 'Course name',
                  required: true,
                  titleOf: 'en',
                },
                { code: 'provider', type: 'text', labelAr: 'الجهة المقدمة', labelEn: 'Provider' },
                {
                  code: 'cost',
                  type: 'currency',
                  labelAr: 'التكلفة',
                  labelEn: 'Cost',
                  required: true,
                  min: 0,
                },
                {
                  code: 'startDate',
                  type: 'date',
                  labelAr: 'تاريخ البدء',
                  labelEn: 'Start date',
                  required: true,
                },
                { code: 'external', type: 'checkbox', labelAr: 'جهة خارجية', labelEn: 'External provider' },
              ],
            },
          ],
        },
        stages: { approval: true, processing: true, processorRoleKeys: ['requests.office'], slaHours: 16 },
        active: true,
        showInCatalogue: true,
        embeddable: true,
      }),
    );
    await page.goto(`/settings/request-types/${code}`);
    await expect(
      page
        .getByText(`Training request ${tag}`)
        .or(page.getByText(`طلب تدريب ${tag}`))
        .first(),
    ).toBeVisible();
    await settle(page);
    await shot('form-builder', { fullPage: true });

    // ---- Catalogue (Ahmed) ------------------------------------------------------------------------------------
    await ahmed.goto('/requests');
    await expect(ahmed.getByTestId(`catalogue-type-${code}`)).toBeVisible();
    await expect(ahmed.getByTestId('catalogue-type-docreg.document')).toBeVisible();
    await settle(ahmed);
    await ahmed.screenshot({ path: screenshotPath(language, 'request-catalogue'), fullPage: true });

    // ---- Document submission (wizard) and review (Strategy Office) --------------------------------------------
    const policyAr = `سياسة السفر ${tag}`;
    const policyEn = `Travel policy ${tag}`;
    const prefill = encodeURIComponent(
      JSON.stringify({
        nameAr: policyAr,
        nameEn: policyEn,
        ownerUnit: strategy,
        description: 'Rules for business travel.',
      }),
    );
    await ahmed.goto(`/requests/new/docreg.document?values=${prefill}`);
    await expect(ahmed.getByRole('textbox').first()).toBeVisible();
    await settle(ahmed);
    await ahmed.screenshot({ path: screenshotPath(language, 'document-submission'), fullPage: true });

    let review = await draft(ahmedApi, 'docreg.document', { nameAr: policyAr, nameEn: policyEn });
    const draftFile = await upload(
      ahmedApi,
      review.id,
      'travel-policy.txt',
      'text/plain',
      Buffer.from('Travel approval limit is 5000 AED per trip.'),
    );
    review = await save(ahmedApi, review, {
      nameAr: policyAr,
      nameEn: policyEn,
      ownerUnit: strategy,
      description: 'Rules for business travel.',
      draftFile,
    });
    review = await submit(ahmedApi, review);
    await page.goto(`/requests/${review.id}`);
    await page.getByTestId('request-decide-approve').click();
    await expect(page.getByRole('dialog')).toBeVisible();
    await settle(page);
    await shot('document-review');
    await page.keyboard.press('Escape');
    await decide(adminApi, review.id, { level: 'operational', docType: 'policy', sensitivity: 'internal' });
    const final = await upload(
      adminApi,
      review.id,
      'signed.pdf',
      'application/pdf',
      pdf(`Travel policy ${tag} - limit 5000`),
    );
    await act(adminApi, review.id, 'publish', { finalFile: final, shareToAll: false });
    const travel =
      (
        await json<{ page: { items: { id: string; nameEn: string }[] } }>(
          await adminApi.get('/api/v1/docreg/library', { params: { search: policyEn } }),
        )
      ).page.items[0]?.id ?? '';

    // ---- New version supersedes; compare --------------------------------------------------------------------------
    await publishDocument(
      ahmedApi,
      adminApi,
      {
        nameAr: policyAr,
        nameEn: policyEn,
        ownerUnit: strategy,
        description: 'Rules for business travel.',
        text: 'Travel approval limit is 7000 AED per trip.',
      },
      { level: 'operational', docType: 'policy', sensitivity: 'internal' },
      { existing: travel },
    );
    await ahmed.goto(`/documents/${travel}/compare`);
    await expect(ahmed.getByTestId('diff-added').first()).toBeVisible();
    await settle(ahmed);
    await ahmed.screenshot({ path: screenshotPath(language, 'version-compare'), fullPage: true });

    // ---- Library by sensitivity: chips and lock icons per level (visibility itself is proven by the integration tests) ----
    await publishDocument(
      ahmedApi,
      adminApi,
      {
        nameAr: `دليل خدمة العملاء ${tag}`,
        nameEn: `Customer service manual ${tag}`,
        ownerUnit: strategy,
        description: 'How we serve customers.',
        text: 'Customer service manual.',
      },
      { level: 'operational', docType: 'manual', sensitivity: 'confidential' },
    );
    await ahmed.goto('/documents');
    await ahmed.getByTestId('library-search').fill(tag);
    await expect(
      ahmed
        .getByText(`Customer service manual ${tag}`)
        .or(ahmed.getByText(`دليل خدمة العملاء ${tag}`))
        .first(),
    ).toBeVisible();
    await settle(ahmed);
    await ahmed.screenshot({ path: screenshotPath(language, 'library-sensitivity'), fullPage: true });

    // ---- Expiry reminder: a procedure expiring in 45 days; the validity job reminds the owner ------------------------
    const expiring = await publishDocument(
      ahmedApi,
      adminApi,
      {
        nameAr: `إجراء المشتريات ${tag}`,
        nameEn: `Procurement procedure ${tag}`,
        ownerUnit: strategy,
        description: 'Purchasing steps.',
        text: 'Procurement steps.',
      },
      { level: 'operational', docType: 'procedure', sensitivity: 'internal' },
      {
        effectiveDate: iso(new Date(Date.now() - 400 * 86_400_000)),
        expiryDate: iso(new Date(Date.now() + 45 * 86_400_000)),
      },
    );
    expect(
      (
        await adminApi.post('/api/v1/scheduling/jobs/docreg.validity/run', {
          headers: await antiforgery(adminApi),
        })
      ).status(),
    ).toBe(204);
    await expect
      .poll(
        async () => {
          const doc = await json<{ validity: { kind: string }[] }>(
            await ahmedApi.get(`/api/v1/docreg/documents/${expiring}`),
          );
          return doc.validity.some((v) => v.kind === 'reminder');
        },
        { timeout: 30_000 },
      )
      .toBe(true);
    await ahmed.goto(`/documents/${expiring}?tab=validity`);
    await expect(ahmed.getByTestId('document-confirm-valid')).toBeVisible();
    await settle(ahmed);
    await ahmed.screenshot({ path: screenshotPath(language, 'expiry-reminder'), fullPage: true });

    // ---- IP request (wizard) and review, MOET form ---------------------------------------------------------------
    const ipPrefill = encodeURIComponent(
      JSON.stringify({
        nameAr: `منهجية قياس السعادة ${tag}`,
        nameEn: `Happiness measurement methodology ${tag}`,
        description: 'A methodology to measure customer happiness across channels.',
        contributors: [
          {
            externalName: 'Dr. Sara Ali',
            externalOrganisation: 'UAE University',
            role: 'contributor',
            share: 30,
          },
        ],
      }),
    );
    await ahmed.goto(`/requests/new/ip.registration?values=${ipPrefill}`);
    await expect(ahmed.getByRole('textbox').first()).toBeVisible();
    await settle(ahmed);
    await ahmed.screenshot({ path: screenshotPath(language, 'ip-request'), fullPage: true });

    let ip = await draft(ahmedApi, 'ip.registration', {
      nameAr: `منهجية قياس السعادة ${tag}`,
      nameEn: `Happiness measurement methodology ${tag}`,
      description: 'A methodology to measure customer happiness across channels.',
      contributors: [
        {
          employee: await employeeId(adminApi, 'E0002'),
          emiratesId: '784-1990-1234567-1',
          role: 'author',
          share: 70,
        },
        {
          externalName: 'Dr. Sara Ali',
          externalOrganisation: 'UAE University',
          emiratesId: '784-1985-7654321-9',
          role: 'contributor',
          share: 30,
        },
      ],
      declaration: true,
    });
    ip = await submit(ahmedApi, ip);
    await decide(adminApi, ip.id, {
      propertyType: 'methodology',
      protectionType: 'copyright',
      notes: 'Original work.',
    });
    const recordId = ip.subjectId ?? '';
    const form = await adminApi.get(`/api/v1/ip/records/${recordId}/moet-form`);
    expect(form.ok(), await form.text()).toBe(true);
    const dir = mkdtempSync(join(tmpdir(), 'moet-'));
    writeFileSync(join(dir, 'moet.pdf'), await form.body());
    execFileSync('pdftoppm', [
      '-png',
      '-r',
      '70',
      '-singlefile',
      join(dir, 'moet.pdf'),
      screenshotPath(language, 'moet-form-pdf').replace(/\.png$/u, ''),
    ]);
    await json(
      await apiPost(adminApi, `/api/v1/ip/records/${recordId}/filing`, {
        filingDate: iso(new Date()),
        applicationNo: `MOET-${tag}`,
        version: (await json<{ version: number }>(await adminApi.get(`/api/v1/ip/records/${recordId}`)))
          .version,
      }),
    );

    // ---- IP from a published document of a flagged type ---------------------------------------------------------
    const settings = await json<{
      value: { types: { code: string; createsIp: boolean }[] } & Record<string, unknown>;
      version: number;
    }>(await adminApi.get('/api/v1/docreg/settings'));
    await json(
      await apiPut(adminApi, '/api/v1/docreg/settings', {
        settings: {
          ...settings.value,
          types: settings.value.types.map((t) => (t.code === 'manual' ? { ...t, createsIp: true } : t)),
        },
        version: settings.version,
      }),
    );
    const guide = `Service design guide ${tag}`;
    const guideDoc = await publishDocument(
      ahmedApi,
      adminApi,
      {
        nameAr: `دليل تصميم الخدمات ${tag}`,
        nameEn: guide,
        ownerUnit: strategy,
        description: 'Our method to design services.',
        text: 'Service design method.',
      },
      { level: 'operational', docType: 'manual', sensitivity: 'internal' },
    );
    let fromDocument: { id: string } | undefined;
    await expect
      .poll(
        async () => {
          const list = await json<{ items: { id: string }[] }>(
            await adminApi.get('/api/v1/ip/records', { params: { search: guide } }),
          );
          fromDocument = list.items[0];
          return fromDocument !== undefined;
        },
        { timeout: 60_000 },
      )
      .toBe(true);

    await page.goto('/ip');
    await expect(page.getByTestId(`ip-row-${recordId}`)).toBeVisible();
    await settle(page);
    await shot('ip-register', { fullPage: true });

    await page.goto(`/ip/records/${fromDocument?.id ?? ''}`);
    await expect(
      page
        .getByText(guide)
        .or(page.getByText(`دليل تصميم الخدمات ${tag}`))
        .first(),
    ).toBeVisible();
    await settle(page);
    await shot('ip-from-document', { fullPage: true });

    await page.goto(`/documents/${guideDoc}?tab=linked`);
    await settle(page);
    await shot('document-linked-ip');

    await ahmed.context().close();
    await ahmedApi.dispose();
  });
});

async function employeeId(api: APIRequestContext, number: string): Promise<string> {
  const page = await json<{ items: { id: string; employeeNo: string }[] }>(
    await api.get('/api/v1/org/employees', { params: { search: number } }),
  );
  const employee = page.items.find((e) => e.employeeNo === number);
  if (!employee) throw new Error(`Employee ${number} not found`);
  return employee.id;
}
