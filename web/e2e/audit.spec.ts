import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { APIRequestContext, Browser, Page } from '@playwright/test';
import { apiPost, apiPut, expect, screenshotPath, test, tr, unitIdByCode, type Language } from './fixtures';

/**
 * Phase 16 — Audit Management (docs/design/audit.md). The Audit Office (Mariam) records reports, findings and follow-ups; Ahmed (a department
 * user of the Strategy & Performance Department) sees his department's findings read-only. Screenshots: docs/screenshots/phase-16
 * (E2E_SCREENSHOTS=phase-16).
 */

const today = () => new Date().toISOString().slice(0, 10);
const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);
const base = '/api/v1/auditmgmt';

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

async function officePage(
  browser: Browser,
  admin: APIRequestContext,
  baseURL: string,
  language: Language,
): Promise<Page> {
  const context = await browser.newContext({
    baseURL,
    locale: language === 'ar' ? 'ar-AE' : 'en-GB',
    viewport: { width: 1366, height: 860 },
  });
  await context.addInitScript((lang) => {
    localStorage.setItem('strategy.language', lang);
    localStorage.setItem('strategy.theme', 'light');
  }, language);
  await context.addCookies((await admin.storageState()).cookies);
  await apiPut(admin, '/api/v1/identity/me/preferences', { language, theme: 'light' });
  return context.newPage();
}

async function snap(page: Page, language: Language, name: string, fullPage = false) {
  await page.evaluate(() => document.fonts.ready);
  await page.screenshot({ path: screenshotPath(language, name), fullPage });
}

/** AI on with the offline demo provider (drafts only). */
async function ensureDemoAi(admin: APIRequestContext) {
  const current = await json<{
    settings: { enabled: boolean; defaultProviderId: string | null } & Record<string, unknown>;
    version: number;
  }>(await admin.get('/api/v1/ai/settings'));
  if (current.settings.enabled && current.settings.defaultProviderId) return;
  const provider = await json<{ id: string }>(
    await apiPost(admin, '/api/v1/ai/providers', {
      name: `Demo ${Date.now()}`,
      type: 'demo',
      defaultModel: 'demo-large',
    }),
  );
  await json(
    await apiPut(admin, '/api/v1/ai/settings', {
      settings: { ...current.settings, enabled: true, defaultProviderId: provider.id },
      version: current.version,
    }),
  );
}

async function setLifecycle(admin: APIRequestContext, enabled: boolean) {
  const features = await json<{ code: string; version: number }[]>(
    await admin.get('/api/v1/settings/features'),
  );
  const feature = features.find((f) => f.code === 'auditmgmt.lifecycle');
  if (!feature) throw new Error('auditmgmt.lifecycle feature missing');
  await json(
    await apiPut(admin, '/api/v1/settings/features/auditmgmt.lifecycle', {
      enabled,
      version: feature.version,
    }),
  );
}

interface Ref {
  id: string;
  number: string;
}

test.describe('audit management', () => {
  test('report, AI extraction review, finding, follow-up, calendar, dashboard, department view, committee pack', async ({
    page,
    language,
    adminApi,
    browser,
    baseURL,
  }) => {
    test.setTimeout(600_000);
    const a = (key: string, params: Record<string, string | number> = {}) =>
      tr(language, `auditmgmt:${key}`, params);
    const arabic = language === 'ar';
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const strategy = await unitIdByCode(adminApi, 'DEP-STR');
    const finance = await unitIdByCode(adminApi, 'DEP-FIN');
    await ensureDemoAi(adminApi);

    // ---- Data: a revenue audit with findings for two departments (API, as the Audit Office) ---------------------------
    const report = await json<Ref>(
      await apiPost(adminApi, `${base}/reports`, {
        titleAr: `تدقيق الإيرادات ${tag}`,
        titleEn: `Revenue audit ${tag}`,
        typeCode: 'revenue-audit',
        auditorBody: arabic ? 'مكتب التدقيق الداخلي' : 'Internal Audit Office',
        leadAuditorEmployeeId: null,
        auditedUnitIds: [strategy, finance],
        periodFrom: inDays(-365),
        periodTo: today(),
        reportDate: today(),
        distributionEmployeeIds: [],
        distributionNote: null,
        summary: arabic ? 'تدقيق دورة الإيرادات والتحصيل' : 'Audit of the revenue and collection cycle',
      }),
    );
    const finding = async (
      titleAr: string,
      titleEn: string,
      unit: string,
      priority: string,
      recs: [string, string, number][],
    ) =>
      json<Ref>(
        await apiPost(adminApi, `${base}/reports/${report.id}/findings`, {
          finding: {
            titleAr,
            titleEn,
            typeCode: 'finding',
            condition: arabic ? `${titleAr}: لوحظ خلال التدقيق.` : `${titleEn}: observed during the audit.`,
            criteria: arabic ? 'دليل الإجراءات المالية، البند 4' : 'Financial procedures manual §4',
            criteriaDocumentId: null,
            cause: arabic ? 'عدم تحديد مسؤول للمراجعة' : 'No reviewer assigned',
            effect: arabic ? 'احتمال فقدان إيرادات' : 'Possible loss of revenue',
            findingDate: today(),
            responsibleUnitId: unit,
            priority,
            repeat: false,
            repeatOfFindingId: null,
            expectedClosingOn: inDays(120),
            auditorEmployeeId: null,
          },
          recommendations: recs.map(([ar, en, due]) => ({
            actionAr: ar,
            actionEn: en,
            managementResponseCode: 'agree',
            managementComment: null,
            implementationPlan: arabic ? 'خطة متفق عليها مع الإدارة' : 'Plan agreed with the department',
            contactEmployeeId: null,
            dueOn: inDays(due),
            notes: null,
          })),
        }),
      );
    const f1 = await finding(
      'مطابقات الإيرادات غير مراجعة',
      'Revenue reconciliations not reviewed',
      strategy,
      'high',
      [
        [
          'اعتماد مراجعة شهرية موثقة للمطابقات',
          'Introduce a documented monthly review of reconciliations',
          30,
        ],
        ['تدريب الموظفين على إجراء المطابقة', 'Train the staff on the reconciliation procedure', 60],
      ],
    );
    await finding('تأخر إيداع المتحصلات', 'Late deposit of collections', strategy, 'critical', [
      ['الإيداع اليومي للمتحصلات', 'Deposit collections daily', 20],
    ]);
    await finding(
      'صلاحيات النظام المالي غير مراجعة',
      'Finance system access not reviewed',
      finance,
      'medium',
      [['مراجعة الصلاحيات ربع سنوياً', 'Review access rights quarterly', 45]],
    );
    await json(await apiPost(adminApi, `${base}/reports/${report.id}/issue`));

    const office = await officePage(browser, adminApi, baseURL ?? '', language);

    // ---- Audit report with its findings register --------------------------------------------------------------------
    await office.goto(`/audit-management/reports/${report.id}`);
    await expect(office.getByTestId('audit-report-findings').getByRole('row')).toHaveCount(4);
    await snap(office, language, 'audit-report');

    // ---- AI extraction: drafts reviewed by the Audit Office, nothing saved until confirmed ---------------------------
    await office.getByTestId('audit-ai-extract').click();
    await office
      .getByTestId('audit-ai-text')
      .fill(
        arabic
          ? '- عدم وجود فصل للمهام في الصندوق\n- غياب سجل للإيصالات الملغاة'
          : '- No segregation of duties at the cashier\n- No register of cancelled receipts',
      );
    await office.getByTestId('audit-ai-run').click();
    await expect(office.getByTestId('audit-ai-row')).toHaveCount(2);
    await snap(office, language, 'audit-ai-extraction');
    await office.keyboard.press('Escape');
    await expect(office.getByTestId('audit-report-findings').getByRole('row')).toHaveCount(4);

    // ---- Finding page and record follow-up (Audit Office) --------------------------------------------------------------
    await office.goto(`/audit-management/findings/${f1.id}?tab=recommendations`);
    await expect(office.getByTestId('audit-recommendation')).toHaveCount(2);
    await office.getByTestId('audit-record-follow-up').first().click();
    await office.getByTestId('audit-follow-up-pct').fill('40');
    await office
      .getByTestId('audit-follow-up-actions')
      .fill(arabic ? 'بدأت المراجعة الشهرية للمطابقات' : 'Monthly review of reconciliations started');
    await office
      .getByTestId('audit-follow-up-evidence')
      .fill(arabic ? 'مطابقات موقعة لشهرين' : 'Signed reconciliations for two months');
    await office.getByTestId('audit-ai-evidence').click();
    await expect(office.getByTestId('audit-ai-evidence-result')).toBeVisible();
    await snap(office, language, 'audit-record-follow-up');
    // Record through the API with the next follow-up date (the date picker interaction is covered by the shared component tests).
    await office.keyboard.press('Escape');
    const recs = await json<{ id: string }[]>(
      await adminApi.get(`${base}/findings/${f1.id}/recommendations`),
    );
    const [rec] = recs;
    if (!rec) throw new Error('The finding has no recommendation');
    await json(
      await apiPost(adminApi, `${base}/recommendations/${rec.id}/follow-ups`, {
        followUpOn: today(),
        achievedOn: null,
        pct: 0.4,
        actionsImplemented: arabic
          ? 'بدأت المراجعة الشهرية للمطابقات'
          : 'Monthly review of reconciliations started',
        evidenceText: arabic ? 'مطابقات موقعة لشهرين' : 'Signed reconciliations for two months',
        nextFollowUpOn: inDays(5),
        comment: arabic
          ? 'تم الاطلاع على الأدلة؛ المتابعة القادمة بعد خمسة أيام.'
          : 'Evidence seen; next follow-up in five days.',
      }),
    );
    await office.goto(`/audit-management/findings/${f1.id}?tab=recommendations`);
    await expect(office.getByTestId('audit-follow-up-timeline')).toBeVisible();
    await expect(office.getByTestId('audit-office-comment')).toBeVisible();
    await snap(office, language, 'audit-finding', true);

    // ---- Follow-up calendar and dashboard ---------------------------------------------------------------------------------
    await office.goto('/audit-management/calendar');
    await office.getByTestId('audit-calendar-month').click();
    await expect(office.getByTestId('audit-calendar-item').first()).toBeVisible();
    await snap(office, language, 'audit-calendar');
    await office.goto('/audit-management');
    await expect(office.getByTestId('audit-tiles')).toBeVisible();
    await office.getByTestId('audit-ai-summary').click();
    await expect(office.getByTestId('audit-ai-summary-draft')).toBeVisible();
    await snap(office, language, 'audit-dashboard', true);

    // ---- Department view (Ahmed, read-only): only his department's findings, no actions -----------------------------
    await page.goto('/audit-management');
    await expect(page.getByTestId('audit-department-view')).toBeVisible();
    await expect(
      page.getByTestId('audit-department-active').getByTestId('audit-finding-row').first(),
    ).toBeVisible();
    await expect(page.getByText(a('department.readOnly'))).toBeVisible();
    await snap(page, language, 'audit-department-view', true);
    await page.goto(`/audit-management/findings/${f1.id}?tab=recommendations`);
    await expect(page.getByTestId('audit-office-comment')).toBeVisible();
    await expect(page.getByTestId('audit-record-follow-up')).toHaveCount(0);
    await expect(page.getByTestId('audit-finding-edit')).toHaveCount(0);
    expect((await apiPost(page, `${base}/findings/${f1.id}/cancel`, { reason: 'x' })).status()).toBe(403);

    // ---- Audit committee pack (PDF, rendered to PNG for the reviewers) ----------------------------------------------------
    const pack = await apiPost(adminApi, `${base}/exports/committee`, {
      format: 'pdf',
      language,
      reportId: null,
      unitId: null,
      year: null,
      summaryAr: '(مسودة معتمدة) ملخص حالة ملاحظات التدقيق للجنة.',
      summaryEn: '(Accepted draft) Audit findings status summary for the committee.',
    });
    expect(pack.ok(), await pack.text()).toBe(true);
    const dir = mkdtempSync(join(tmpdir(), 'audit-pdf-'));
    const pdf = join(dir, 'pack.pdf');
    writeFileSync(pdf, await pack.body());
    execFileSync('pdftoppm', [
      '-png',
      '-r',
      '70',
      '-singlefile',
      pdf,
      screenshotPath(language, 'audit-committee-pack').replace(/\.png$/u, ''),
    ]);
    await office.context().close();
  });

  test('layer 2 switched on: risk-based plan and engagement with working papers; off again hides it', async ({
    language,
    adminApi,
    browser,
    baseURL,
  }) => {
    test.setTimeout(300_000);
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    await setLifecycle(adminApi, true);
    try {
      await json(await apiPost(adminApi, `${base}/lifecycle/universe/sync`));
      const plans = await json<{ id: string; year: number; status: string }[]>(
        await adminApi.get(`${base}/lifecycle/plans`),
      );
      const year = Math.max(new Date().getFullYear() + 1, ...plans.map((p) => p.year + 1));
      const plan = await json<{ id: string }>(
        await apiPost(adminApi, `${base}/lifecycle/plans`, {
          year,
          titleAr: null,
          titleEn: `Audit plan ${year}`,
        }),
      );
      const universe = await json<{ id: string; values: { unitId: string | null } }[]>(
        await adminApi.get(`${base}/lifecycle/universe`),
      );
      const engagement = await json<{ id: string }>(
        await apiPost(adminApi, `${base}/lifecycle/engagements`, {
          planId: plan.id,
          values: {
            titleAr: `تدقيق دورة الإيرادات ${tag}`,
            titleEn: `Revenue cycle audit ${tag}`,
            entityId: universe[0]?.id ?? null,
            objectives: language === 'ar' ? 'تقييم ضوابط الإيرادات' : 'Assess the revenue controls',
            scope: language === 'ar' ? 'التحصيل والفوترة' : 'Collections and billing',
            teamId: null,
            leadAuditorEmployeeId: null,
            plannedQuarter: 2,
            plannedStart: `${year}-04-01`,
            plannedEnd: `${year}-05-31`,
            budgetHours: 240,
          },
        }),
      );
      for (const action of ['start', 'to-fieldwork']) {
        await json(
          await apiPost(adminApi, `${base}/lifecycle/engagements/${engagement.id}/transition`, {
            action,
            reason: null,
          }),
        );
      }
      await json(
        await apiPost(adminApi, `${base}/lifecycle/engagements/${engagement.id}/work-papers`, {
          reference: 'WP-1',
          title: language === 'ar' ? 'اختبار إيداع المتحصلات' : 'Test of deposits',
          testStep: language === 'ar' ? 'تتبع 25 إيصالاً' : 'Trace 25 receipts',
          sample: '25',
          result: language === 'ar' ? '3 استثناءات' : '3 exceptions',
          conclusion: language === 'ar' ? 'الضابط ضعيف' : 'Control weak',
        }),
      );
      const office = await officePage(browser, adminApi, baseURL ?? '', language);
      await office.goto(`/audit-management/plans/${plan.id}`);
      await expect(office.getByTestId('audit-plan-engagements')).toBeVisible();
      await expect(office.getByTestId('audit-qaip')).toBeVisible();
      await snap(office, language, 'audit-plan', true);
      await office.goto(`/audit-management/engagements/${engagement.id}`);
      await expect(office.getByTestId('audit-work-papers')).toBeVisible();
      await snap(office, language, 'audit-engagement', true);
      await setLifecycle(adminApi, false);
      await office.goto(`/audit-management/plans/${plan.id}`);
      await expect(office.getByTestId('audit-lifecycle-off')).toBeVisible();
      await expect(office.getByTestId('audit-nav-plans')).toHaveCount(0);
      await office.context().close();
    } finally {
      await setLifecycle(adminApi, false);
    }
  });
});
