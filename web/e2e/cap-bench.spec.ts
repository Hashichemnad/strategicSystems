import type { APIRequestContext, Browser, Page } from '@playwright/test';
import { apiPost, apiPut, expect, screenshotPath, test, tr, unitIdByCode, type Language } from './fixtures';

/**
 * Phase 15 — Corrective Action Plan and Benchmarking (docs/design/cap.md, docs/design/benchmarking.md).
 * Screenshots go to docs/screenshots/phase-15 (E2E_SCREENSHOTS=phase-15).
 */

const today = () => new Date().toISOString().slice(0, 10);
const inDays = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10);

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

async function employeeIdByNumber(api: APIRequestContext, number: string): Promise<string> {
  const page = await json<{ items: { id: string; employeeNo: string }[] }>(
    await api.get('/api/v1/org/employees', { params: { search: number } }),
  );
  const employee = page.items.find((e) => e.employeeNo === number);
  if (!employee) throw new Error(`Employee ${number} not found`);
  return employee.id;
}

/** The administrator (Mariam: CAP Office, Benchmarking Office, Requests Office in the demo) approves her open task on the subject. */
async function approve(admin: APIRequestContext, subject: string, id: string) {
  const panel = await json<{ instances: { myTask: { taskId: string } | null }[] }>(
    await admin.get(`/api/v1/workflow/subjects/${subject}/${id}`),
  );
  const task = panel.instances.map((x) => x.myTask).find((t) => t !== null);
  if (!task) throw new Error(`No approval task on ${subject} ${id}`);
  const response = await apiPost(admin, `/api/v1/workflow/tasks/${task.taskId}/act`, {
    action: 'approve',
    comment: null,
  });
  expect(response.status(), await response.text()).toBe(200);
}

/** A browser page signed in as the administrator (the office), in the project's language. */
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

interface Ref {
  id: string;
  number: string;
  version: number;
}

test.describe('corrective action plan', () => {
  test('source, accept / reject, root cause, actions with budget routing, PMO validation, achievement, extension, dashboard', async ({
    page,
    language,
    shot,
    adminApi,
    browser,
    baseURL,
  }) => {
    test.setTimeout(600_000);
    const c = (key: string, params: Record<string, string | number> = {}) =>
      tr(language, `cap:${key}`, params);
    const arabic = language === 'ar';
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const department = await unitIdByCode(adminApi, 'DEP-STR');
    const ahmedId = await employeeIdByNumber(adminApi, 'E0002');
    const ahmed = page.request;

    // ---- Data: the CAP Office records an internal audit with three remarks owned by Ahmed ---------------------------------
    const source = await json<Ref>(
      await apiPost(adminApi, '/api/v1/cap/sources', {
        titleAr: `تقرير التدقيق الداخلي للخدمات ${tag}`,
        titleEn: `Internal audit of services ${tag}`,
        sourceTypeCode: 'internal-audit',
        description: arabic
          ? 'تدقيق إجراءات تقديم الخدمات للربع الثالث'
          : 'Audit of service delivery procedures, Q3',
        receivedOn: today(),
        ownerEmployeeId: null,
        unitId: department,
        teamId: null,
      }),
    );
    const remark = async (ar: string, en: string, priority: string, typeCode = 'nonconformity') =>
      json<Ref>(
        await apiPost(adminApi, `/api/v1/cap/sources/${source.id}/remarks`, {
          textAr: ar,
          textEn: en,
          criterionCode: null,
          typeCode,
          priority,
          departmentUnitId: department,
          sectionUnitId: null,
          ownerEmployeeId: ahmedId,
        }),
      );
    const r1 = await remark(
      'لا توجد آلية موثقة لمتابعة شكاوى المتعاملين خلال المدة المحددة',
      'No documented mechanism to follow up customer complaints within the set time',
      'high',
    );
    const r2 = await remark(
      'عدم تحديث دليل الإجراءات منذ أكثر من سنتين',
      'The procedures manual has not been updated for over two years',
      'medium',
      'observation',
    );
    const r3 = await remark(
      'غياب لوحة متابعة للمؤشرات في قسم الأرشيف',
      'No KPI dashboard in the archive section',
      'low',
      'improvement',
    );

    // ---- Remarks source -------------------------------------------------------------------------------------------------
    await page.goto(`/cap/sources/${source.id}`);
    await expect(page.getByTestId('cap-remark-row')).toHaveCount(3);
    await shot('cap-remarks-source');

    // ---- Accept / reject (rejection is final by default) ----------------------------------------------------------------
    await page.goto(`/cap/remarks/${r3.id}`);
    const banner = page.getByTestId('cap-remark-current-status');
    await expect(banner.getByRole('button', { name: c('actions.accept') })).toBeVisible();
    await shot('cap-remark-accept-reject');
    await banner.getByRole('button', { name: c('actions.reject') }).click();
    await page
      .getByTestId('cap-remark-reject-confirm-reason')
      .fill(
        arabic
          ? 'القسم لا يتبع له أرشيف؛ الملاحظة خارج النطاق'
          : 'The section has no archive; the remark is out of scope',
      );
    await shot('cap-remark-reject');
    await page.getByTestId('cap-remark-reject-confirm').click();
    await expect(page.getByTestId('cap-remark-status')).toHaveText(c('remarkStatus.rejected'));

    await page.goto(`/cap/remarks/${r1.id}`);
    await page
      .getByTestId('cap-remark-current-status')
      .getByRole('button', { name: c('actions.accept') })
      .click();
    await expect(page.getByTestId('cap-remark-status')).toHaveText(c('remarkStatus.planning'));

    // ---- Root cause (required for high priority by the default rule) -------------------------------------------------------
    await page.goto(`/cap/remarks/${r1.id}?tab=root-cause`);
    const whys = arabic
      ? [
          'لم تُغلق الشكاوى في الوقت المحدد',
          'لا يوجد مسؤول متابعة لكل شكوى',
          'الإجراء لا يحدد الأدوار',
          'لم يُراجع الإجراء منذ إعادة الهيكلة',
        ]
      : [
          'Complaints were not closed on time',
          'No follow-up owner per complaint',
          'The procedure does not define roles',
          'The procedure was not reviewed after the restructuring',
        ];
    for (const [i, why] of whys.entries()) {
      if (i >= 3) await page.getByRole('button', { name: c('rootCause.addWhy') }).click();
      await page.getByTestId(`cap-why-${i}`).fill(why);
    }
    await page
      .getByTestId('cap-root-cause-text')
      .fill(
        arabic
          ? 'غياب مراجعة دورية للإجراء بعد التغييرات التنظيمية'
          : 'No periodic review of the procedure after organisational changes',
      );
    await page.getByTestId('cap-root-cause-save').click();
    await expect(page.getByTestId('cap-root-cause-save')).toBeEnabled();
    await shot('cap-root-cause');

    // ---- Actions: one with budget (not available → draft program request), one shared by two remarks --------------------------
    await json(await apiPost(ahmed, `/api/v1/cap/remarks/${r2.id}/accept`));
    const action = (ar: string, en: string, extra: Record<string, unknown> = {}) => ({
      detailsAr: ar,
      detailsEn: en,
      typeCode: 'corrective',
      budgetRequired: false,
      budgetAvailable: null,
      estimatedCost: null,
      relatedProcess: arabic ? 'إدارة الشكاوى' : 'Complaints management',
      assignedEmployeeId: ahmedId,
      plannedStart: today(),
      plannedEnd: inDays(45),
      ...extra,
    });
    const a1 = await json<Ref>(
      await apiPost(
        ahmed,
        `/api/v1/cap/remarks/${r1.id}/actions`,
        action(
          'تطبيق نظام إلكتروني لتتبع الشكاوى مع تنبيهات المهلة',
          'Deploy an electronic complaint-tracking system with deadline alerts',
          {
            budgetRequired: true,
            budgetAvailable: false,
            estimatedCost: 250000,
          },
        ),
      ),
    );
    const a2 = await json<Ref>(
      await apiPost(
        ahmed,
        `/api/v1/cap/remarks/${r1.id}/actions`,
        action(
          'تحديث دليل إجراءات الشكاوى وتحديد الأدوار والمهل',
          'Update the complaints procedure manual with roles and deadlines',
        ),
      ),
    );
    await json(await apiPost(ahmed, `/api/v1/cap/remarks/${r2.id}/link-action`, { actionId: a2.id }));

    await page.goto(`/cap/remarks/${r1.id}?tab=actions`);
    await expect(page.getByTestId('cap-action-row')).toHaveCount(2);
    await page.getByTestId('cap-action-new').click();
    await page.getByTestId('cap-action-details-ar').fill('تدريب موظفي مركز الاتصال على إجراء الشكاوى المحدث');
    await page
      .getByTestId('cap-action-details-en')
      .fill('Train contact-centre staff on the updated complaints procedure');
    await page.getByLabel(c('budget.required')).check();
    await page.getByLabel(c('budget.available')).click();
    await page.getByRole('option', { name: c('common.no') }).click();
    await page.getByTestId('cap-action-cost').fill('40000');
    await expect(page.getByTestId('cap-budget-routing-hint')).toBeVisible();
    await shot('cap-action-budget-routing');
    await page.keyboard.press('Escape');
    await shot('cap-remark-actions');

    await json(await apiPost(ahmed, `/api/v1/cap/remarks/${r1.id}/submit`, { comment: null }));

    // ---- PMO validation per action (the budget action becomes a draft program request "Action Plan") ---------------------------
    const office = await officePage(browser, adminApi, baseURL ?? '', language);
    await office.goto(`/cap/remarks/${r1.id}?tab=actions`);
    const budgetRow = office.getByTestId('cap-action-row').filter({ hasText: a1.number });
    await budgetRow.getByTestId('cap-action-validate').click();
    await office.getByLabel(c('validation.decision')).click();
    await office.getByRole('option', { name: c('validation.approve') }).click();
    await expect(office.getByTestId('cap-validate-routing')).toBeVisible();
    await snap(office, language, 'cap-pmo-validation');
    await office.getByTestId('cap-validate-confirm').click();
    await expect(budgetRow.getByTestId('cap-program-request-link')).toBeVisible();
    await json(
      await apiPost(adminApi, `/api/v1/cap/actions/${a2.id}/validate`, {
        decision: 'approve',
        comment: null,
        projectId: null,
      }),
    );
    await office.goto(`/cap/actions/${a1.id}`);
    await expect(office.getByTestId('cap-program-request-link')).toBeVisible();
    await snap(office, language, 'cap-budget-routed');

    // ---- Achievement update (monthly) and history ---------------------------------------------------------------------------
    await page.goto(`/cap/actions/${a2.id}?tab=progress`);
    await page.getByTestId('cap-action-progress').click();
    await page.getByTestId('cap-progress-pct').fill('60');
    await page
      .getByTestId('cap-progress-note')
      .fill(arabic ? 'تمت مراجعة المسودة مع الإدارة القانونية' : 'Draft reviewed with the legal department');
    await page.getByTestId('cap-progress-save').click();
    await expect(page.getByTestId('cap-progress-row')).toHaveCount(1);
    await shot('cap-achievement-update');

    // ---- Extension request (PM Office approves the new date) ----------------------------------------------------------------
    await json(
      await apiPost(ahmed, `/api/v1/cap/actions/${a2.id}/extensions`, {
        requestedEnd: inDays(75),
        justification: arabic
          ? 'بانتظار اعتماد اللجنة القانونية للدليل المحدث'
          : 'Waiting for the legal committee to approve the updated manual',
      }),
    );
    await page.goto(`/cap/actions/${a2.id}?tab=extensions`);
    await expect(page.getByTestId('cap-extension-row')).toHaveCount(1);
    await shot('cap-extension-request');

    // ---- Dashboard ---------------------------------------------------------------------------------------------------------
    await office.goto('/cap');
    await expect(office.getByTestId('cap-tiles')).toBeVisible();
    await expect(office.getByTestId('cap-budget-dependent')).toContainText(a1.number);
    await snap(office, language, 'cap-dashboard', true);
    await office.context().close();
  });
});

interface StudyItem {
  id: string;
  number: string;
}

test.describe('benchmarking', () => {
  test('plan request, annual plan, search form, visit info, analysis with development plan, linked CAP actions, register and library', async ({
    page,
    language,
    shot,
    adminApi,
    browser,
    baseURL,
  }) => {
    test.setTimeout(600_000);
    const arabic = language === 'ar';
    const tag = `${language.toUpperCase()}${Date.now().toString(36).slice(-5).toUpperCase()}`;
    const year = new Date().getFullYear() + 1;
    const department = await unitIdByCode(adminApi, 'DEP-STR');
    const ahmedId = await employeeIdByNumber(adminApi, 'E0002');
    const ahmed = page.request;
    const base = '/api/v1/bench';

    // ---- Plan: Ahmed requests the study from the catalogue; the Benchmarking Office approves it into the annual plan ---------------
    const draft = await json<{ id: string; version: number }>(
      await apiPost(ahmed, '/api/v1/requests', {
        typeCode: 'bench.request',
        data: {
          titleAr: `مقارنة قنوات الخدمات الرقمية ${tag}`,
          titleEn: `Digital service channels benchmarking ${tag}`,
          responsibleUnit: department,
          responsibleEmployee: ahmedId,
          targetYear: year,
          targetQuarter: 1,
          comparisonType: 'functional',
          purpose: 'process-improvement',
          scope: arabic ? 'قنوات تقديم الخدمات ومدة الإنجاز' : 'Service channels and turnaround times',
          topics: [
            { topic: arabic ? 'قنوات الخدمة' : 'Service channels', owner: ahmedId, description: null },
            { topic: arabic ? 'مدة الإنجاز' : 'Turnaround time', owner: ahmedId },
          ],
        },
      }),
    );
    const submitted = await json<{ id: string }>(
      await apiPost(ahmed, `/api/v1/requests/${draft.id}/submit`, { version: draft.version }),
    );
    await json(
      await apiPost(adminApi, `/api/v1/requests/${submitted.id}/decide`, {
        action: 'approve',
        comment: null,
      }),
    );
    const study = (
      await json<{ items: StudyItem[] }>(await adminApi.get(`${base}/studies`, { params: { search: tag } }))
    ).items[0];
    if (!study) throw new Error('Study not created');

    const office = await officePage(browser, adminApi, baseURL ?? '', language);
    await office.goto(`/bench/plan?year=${year}`);
    await expect(office.getByTestId('bench-plan-q1')).toContainText(study.number);
    await office.getByTestId('bench-plan-circulate').click();
    await expect(office.getByTestId('bench-plan-circulations').locator('li')).not.toHaveCount(0);
    await snap(office, language, 'bench-plan', true);

    // ---- Search: candidates with reasons and the code of conduct → PMO approval → outreach ------------------------------------
    const candidates = await json<{ id: string; comparatorId: string }[]>(
      await apiPut(ahmed, `${base}/studies/${study.id}/search`, {
        candidates: [
          {
            comparatorId: null,
            newComparator: {
              nameAr: `هيئة الخدمات الذكية ${tag}`,
              nameEn: `Smart Services Authority ${tag}`,
              sectorCode: null,
              country: arabic ? 'الإمارات' : 'UAE',
              scope: 'local',
              website: null,
              contactName: null,
              contactEmail: null,
              contactPhone: null,
              notes: null,
              active: true,
            },
            selected: true,
            reason: arabic ? 'رائدة في قنوات الخدمة الرقمية' : 'Leader in digital service channels',
            availableInfo: arabic ? 'التقرير السنوي 2025' : 'Annual report 2025',
            contact: 'excellence@example.test',
            codeOfConductAck: true,
          },
          {
            comparatorId: null,
            newComparator: {
              nameAr: `بلدية المدينة النموذجية ${tag}`,
              nameEn: `Model City Municipality ${tag}`,
              sectorCode: null,
              country: arabic ? 'سنغافورة' : 'Singapore',
              scope: 'international',
              website: null,
              contactName: null,
              contactEmail: null,
              contactPhone: null,
              notes: null,
              active: true,
            },
            selected: false,
            reason: arabic ? 'تجربة في التتبع المباشر للطلبات' : 'Experience with live request tracking',
            availableInfo: null,
            contact: null,
            codeOfConductAck: false,
          },
        ],
      }),
    );
    await page.goto(`/bench/studies/${study.id}?tab=search`);
    await expect(page.getByTestId('bench-candidate')).toHaveCount(2);
    await shot('bench-search-form', { fullPage: true });
    await json(await apiPost(ahmed, `${base}/studies/${study.id}/search/submit`, { comment: null }));
    await approve(adminApi, 'bench.study', study.id);
    const chosen = candidates[0];
    if (!chosen) throw new Error('No candidate');
    await json(
      await apiPost(ahmed, `${base}/studies/${study.id}/outreach`, {
        studyEntityId: chosen.id,
        on: today(),
        channel: 'email',
        summary: arabic ? 'طلب زيارة ميدانية' : 'Site visit requested',
        response: 'accepted',
      }),
    );

    // ---- Acquire: visit info ---------------------------------------------------------------------------------------------------
    await json(
      await apiPut(ahmed, `${base}/studies/${study.id}/visits`, {
        visits: [
          {
            id: null,
            comparatorId: chosen.comparatorId,
            agreedOn: inDays(20),
            method: 'site-visit',
            participantIds: [ahmedId],
            teamId: null,
            questionnaire: arabic
              ? '1. كيف تديرون قنوات الخدمة؟\n2. ما نسبة الطلبات الرقمية؟\n3. كيف تتابعون مدة الإنجاز؟'
              : '1. How do you run your service channels?\n2. What share of requests is digital?\n3. How do you track turnaround?',
            questionnaireSentOn: today(),
            notes: arabic
              ? 'يمر 80٪ من الطلبات عبر تطبيق موحد مع تتبع مباشر للحالة.'
              : '80 % of requests go through a single app with live status tracking.',
          },
        ],
      }),
    );
    await page.goto(`/bench/studies/${study.id}?tab=visit`);
    await expect(page.getByTestId('bench-visit')).toHaveCount(1);
    await shot('bench-visit-info', { fullPage: true });
    await json(await apiPost(ahmed, `${base}/studies/${study.id}/send-to-analysis`));

    // ---- Analysis with the development plan → PMO approval creates CAP remarks with proposed actions ---------------------------
    await json(
      await apiPut(ahmed, `${base}/studies/${study.id}/analysis`, {
        practices: [
          {
            id: null,
            topicId: null,
            subject: arabic ? `تطبيق خدمات موحد ${tag}` : `Single services app ${tag}`,
            ourPractice: arabic ? 'خمس بوابات منفصلة' : 'Five separate portals',
            theirPractice: arabic ? 'تطبيق واحد مع تتبع مباشر' : 'One app with live tracking',
            category: 'compared',
            applicable: true,
            addToLibrary: true,
          },
          {
            id: null,
            topicId: null,
            subject: arabic ? 'مراكز الخدمة الفعلية' : 'Physical service counters',
            ourPractice: null,
            theirPractice: null,
            category: 'not-applicable',
            applicable: false,
            addToLibrary: false,
          },
        ],
        indicators: [
          {
            id: null,
            kpiCardId: null,
            indicator: arabic ? 'نسبة استخدام القنوات الرقمية' : 'Digital channel uptake',
            ourValue: 0.45,
            theirValue: 0.8,
            period: '2025',
            source: arabic ? 'التقرير السنوي' : 'Annual report',
            updateKpiBenchmark: false,
          },
        ],
        planItems: [
          {
            id: null,
            topicId: null,
            titleAr: 'إطلاق تطبيق خدمات موحد',
            titleEn: 'Launch a single services app',
            description: null,
            ownerEmployeeId: ahmedId,
            unitId: department,
            dueOn: inDays(120),
            priority: 'high',
            actionTypeCode: null,
            budgetRequired: true,
          },
          {
            id: null,
            topicId: null,
            titleAr: 'نشر التتبع المباشر للطلبات',
            titleEn: 'Publish live request tracking',
            description: null,
            ownerEmployeeId: ahmedId,
            unitId: department,
            dueOn: inDays(90),
            priority: 'medium',
            actionTypeCode: null,
            budgetRequired: false,
          },
        ],
      }),
    );
    await page.goto(`/bench/studies/${study.id}?tab=analysis`);
    await expect(page.getByTestId('bench-plan-item')).toHaveCount(2);
    await shot('bench-analysis', { fullPage: true });
    await json(await apiPost(ahmed, `${base}/studies/${study.id}/analysis/submit`, { comment: null }));
    await approve(adminApi, 'bench.study', study.id);

    // ---- Linked CAP actions, live on the study ----------------------------------------------------------------------------------
    await page.goto(`/bench/studies/${study.id}?tab=actions`);
    await expect(page.getByTestId('bench-linked-remark')).toHaveCount(2);
    await expect(page.getByTestId('bench-linked-action')).toHaveCount(2);
    await shot('bench-linked-actions', { fullPage: true });

    // ---- Comparator register and best-practice library -------------------------------------------------------------------------
    await office.goto('/bench/comparators');
    await expect(office.getByTestId('bench-comparator-row').first()).toBeVisible();
    await snap(office, language, 'bench-comparators');
    await office.goto('/bench/practices');
    await expect(office.getByTestId('bench-practice-card').filter({ hasText: tag })).toHaveCount(1);
    await snap(office, language, 'bench-practices');
    await office.context().close();
  });
});
