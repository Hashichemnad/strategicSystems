import { execFileSync } from 'node:child_process';
import { mkdtempSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { APIRequestContext, Browser, Page } from '@playwright/test';
import {
  apiPost,
  apiPut,
  createUserWithPassword,
  expect,
  savePreferences,
  screenshotPath,
  signInViaApi,
  test,
  tr,
  unitIdByCode,
  type Language,
} from './fixtures';

/** Phase 7 — Teams Management (docs/design/teams.md): screenshots go to docs/screenshots/phase-7 (E2E_SCREENSHOTS=phase-7). */

const k = (language: Language, key: string, params?: Record<string, string | number>) =>
  tr(language, `teams:${key}`, params);

async function employeeIdByNumber(api: APIRequestContext, number: string): Promise<string> {
  const page = (await (await api.get('/api/v1/org/employees', { params: { search: number } })).json()) as {
    items: { id: string; employeeNo: string }[];
  };
  const employee = page.items.find((e) => e.employeeNo === number);
  if (!employee) throw new Error(`Employee ${number} not found`);
  return employee.id;
}

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

/** Approves the caller's open approval task on the team (any process). */
async function approve(api: APIRequestContext, teamId: string) {
  const panel = await json<{ instances: { myTask: { taskId: string } | null }[] }>(
    await api.get(`/api/v1/workflow/subjects/teams.team/${teamId}`),
  );
  const task = panel.instances.map((i) => i.myTask).find((t) => t !== null);
  if (!task) throw new Error('No approval task for this user');
  const response = await apiPost(api, `/api/v1/workflow/tasks/${task.taskId}/act`, {
    action: 'approve',
    comment: null,
  });
  expect(response.status(), await response.text()).toBe(200);
}

/** A generated PDF rendered to a PNG next to the screenshots (pdftoppm), so reviewers see the real bilingual document. */
async function pdfShot(api: APIRequestContext, fileId: string, language: Language, name: string) {
  const response = await api.get(`/api/v1/files/${fileId}/content`);
  expect(response.ok()).toBe(true);
  const dir = mkdtempSync(join(tmpdir(), 'teams-pdf-'));
  const pdf = join(dir, 'file.pdf');
  writeFileSync(pdf, await response.body());
  execFileSync('pdftoppm', [
    '-png',
    '-r',
    '70',
    '-singlefile',
    pdf,
    screenshotPath(language, name).replace(/\.png$/u, ''),
  ]);
}

async function pageAs(browser: Browser, baseURL: string, language: Language, user: 'ahmed'): Promise<Page> {
  const context = await browser.newContext({ baseURL, viewport: { width: 1280, height: 900 } });
  const page = await context.newPage();
  await page.addInitScript((lang) => {
    localStorage.setItem('strategy.language', lang);
    localStorage.setItem('strategy.theme', 'light');
  }, language);
  await signInViaApi(page, user);
  await savePreferences(page, language);
  return page;
}

async function poll<T>(read: () => Promise<T>, done: (value: T) => boolean): Promise<T> {
  for (let i = 0; i < 60; i++) {
    const value = await read();
    if (done(value)) return value;
    await new Promise((resolve) => setTimeout(resolve, 500));
  }
  throw new Error('Timed out');
}

test.describe('teams management', () => {
  test.use({ signedInAs: 'mariam' });

  test('a committee is formed through approval with a memo, runs meetings and recommendations, is evaluated with suggested levels, approved by HR, rewarded with certificates; members do not see results', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    browser,
    baseURL,
  }) => {
    test.setTimeout(300_000);
    const tag = `${language}${Date.now().toString(36)}`;
    const year = new Date().getFullYear();
    const ahmed = await employeeIdByNumber(adminApi, 'E0002');
    const fatima = await employeeIdByNumber(adminApi, 'E0003');
    const member = await createUserWithPassword(page, adminApi, baseURL ?? '', tag);
    const memberEmployee = await employeeIdByNumber(adminApi, `E2E-${tag}`);
    const department = await unitIdByCode(adminApi, 'DEP-STR');
    const ahmedApi = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmedApi, 'ahmed');

    // Rewards are approved by the head of the Teams system's owning unit: the PMO section, headed by Ahmed in this demo.
    const pmo = await unitIdByCode(adminApi, 'SEC-PMO');
    const unit = await json<Record<string, unknown> & { version: number }>(
      await adminApi.get(`/api/v1/org/units/${pmo}`),
    );
    await apiPut(adminApi, `/api/v1/org/units/${pmo}`, {
      ...unit,
      headEmployeeId: ahmed,
      deputyEmployeeId: null,
      costCentre: null,
      effectiveDate: null,
    });
    const systems = await json<{ code: string; enabled: boolean; version: number }[]>(
      await adminApi.get('/api/v1/access/systems'),
    );
    const teamsSystem = systems.find((s) => s.code === 'teams');
    if (teamsSystem)
      await apiPut(adminApi, '/api/v1/access/systems/teams', {
        enabled: true,
        owningUnitId: pmo,
        version: teamsSystem.version,
      });

    // A reward matrix for this year (Q-T5 seeds it empty: the administrator fills the amounts).
    const matrix = await json<{ id: string }>(
      await apiPost(adminApi, '/api/v1/teams/settings/reward-matrices', {
        id: null,
        year,
        version: null,
        content: {
          cells: [
            { grade: 'very-active', workload: 'high', role: 'president', amount: 5000 },
            { grade: 'very-active', workload: 'high', role: 'member', amount: 3000 },
            { grade: 'very-active', workload: 'high', role: 'secretary', amount: 2000 },
            { grade: 'active', workload: 'high', role: 'member', amount: 1500 },
          ],
          certificateGrades: ['very-active'],
          annualBudget: 250000,
          currency: 'AED',
        },
      }),
    );
    await json(await apiPost(adminApi, `/api/v1/teams/settings/reward-matrices/${matrix.id}/publish`));

    // 1. Ahmed requests a standing committee (he chairs it) with members, an external expert and their responsibilities.
    const draft = await json<{ id: string; version: number; number: string }>(
      await apiPost(ahmedApi, '/api/v1/teams', {
        typeCode: 'standing-committee',
        nameAr: `لجنة الحوكمة الاستراتيجية ${tag}`,
        nameEn: `Strategic Governance Committee ${tag}`,
        permanent: true,
        subject: language === 'ar' ? 'حوكمة الخطة الاستراتيجية' : 'Governance of the strategic plan',
        objectives:
          language === 'ar'
            ? 'متابعة تنفيذ الخطة الاستراتيجية ورفع التوصيات.'
            : 'Follow the strategic plan and recommend improvements.',
        mandate:
          language === 'ar'
            ? 'مراجعة التقارير الفصلية\nاعتماد التوصيات'
            : 'Review the quarterly reports\nAdopt recommendations',
        owningUnitId: department,
        sponsorEmployeeId: null,
        approvedByCode: 'general-manager',
        memoMode: 'generate',
        memoDraft: null,
        startDate: `${year}-01-01`,
        endDate: null,
        cadence: 'quarterly',
        reportTo: null,
        workload: 'high',
        replacesTeamId: null,
        evaluatorRule: null,
        evaluatorEmployeeId: null,
        members: [
          {
            id: null,
            employeeId: ahmed,
            roleCode: 'president',
            responsibilities: language === 'ar' ? 'رئاسة اللجنة' : 'Chairs the committee',
          },
          {
            id: null,
            employeeId: memberEmployee,
            roleCode: 'member',
            responsibilities: language === 'ar' ? 'التحليل' : 'Analysis',
          },
          {
            id: null,
            employeeId: fatima,
            roleCode: 'secretary',
            responsibilities: language === 'ar' ? 'المحاضر' : 'Minutes',
          },
          {
            id: null,
            employeeId: null,
            externalName: 'Dr. Lina Haddad',
            externalOrganisation: 'University',
            roleCode: 'member',
          },
        ],
      }),
    );
    await page.goto(`/teams/${draft.id}/edit`);
    await expect(page.getByRole('heading', { level: 1, name: k(language, 'form.titleEdit') })).toBeVisible();
    await page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await page.getByRole('button', { name: tr(language, 'common:actions.next') }).click();
    await expect(page.getByTestId('team-members-step')).toBeVisible();
    await shot('teams-formation-request', { fullPage: true });

    // 2. Formation approval (Q-T5): director of the owning unit, then the General Manager — both Mariam in the demo.
    await json(
      await apiPost(ahmedApi, `/api/v1/teams/${draft.id}/submit`, {
        version: draft.version,
        comment: null,
        reason: null,
      }),
    );
    await page.goto(`/teams/${draft.id}?tab=approvals`);
    await expect(page.getByText(k(language, 'status.pending_approval')).first()).toBeVisible();
    await shot('teams-formation-approval', { fullPage: true });
    await approve(adminApi, draft.id);
    await approve(adminApi, draft.id);

    // 3. The numbered bilingual memo PDF.
    const team = await poll(
      async () =>
        json<{ memoFileId: string | null; memoNumber: string | null; status: string }>(
          await adminApi.get(`/api/v1/teams/${draft.id}`),
        ),
      (t) => t.memoFileId !== null,
    );
    expect(team.status).toBe('active');
    expect(team.memoNumber).toMatch(new RegExp(`^MEMO-${year}-`, 'u'));
    await pdfShot(adminApi, team.memoFileId ?? '', language, 'teams-memo-pdf');
    await page.goto(`/teams/${draft.id}`);
    await expect(page.getByTestId('memo-download')).toBeVisible();
    await shot('team-page-overview', { fullPage: true });
    await page.getByRole('tab', { name: k(language, 'tabs.members') }).click();
    await expect(page.getByTestId('team-members')).toContainText('Dr. Lina Haddad');
    await shot('team-page-members');

    // 4. A meeting whose minutes are submitted 25 hours after it ended, and the recommendations register (3 of 4 closed).
    const ended = new Date(Date.now() - 25 * 3600_000);
    const meeting = await json<{ id: string; version: number }>(
      await apiPost(ahmedApi, `/api/v1/teams/${draft.id}/meetings`, {
        title: language === 'ar' ? 'الاجتماع الفصلي الأول' : 'First quarterly meeting',
        scheduledAt: new Date(ended.getTime() - 2 * 3600_000).toISOString(),
        endsAt: ended.toISOString(),
        location: language === 'ar' ? 'قاعة المجلس' : 'Board room',
        agenda: language === 'ar' ? 'مراجعة مؤشرات الربع الأول' : 'Review the Q1 indicators',
        version: 0,
      }),
    );
    const attendees = await json<{ attendance: { memberId: string; externalName: string | null }[] }>(
      await ahmedApi.get(`/api/v1/teams/meetings/${meeting.id}`),
    );
    const saved = await json<{ version: number }>(
      await apiPut(ahmedApi, `/api/v1/teams/meetings/${meeting.id}/minutes`, {
        minutes:
          language === 'ar'
            ? 'ناقشت اللجنة نتائج الربع الأول واتفقت على تحديث خطة العمل.'
            : 'The committee discussed the Q1 results and agreed to update the work plan.',
        decisions: language === 'ar' ? 'اعتماد خطة العمل المحدّثة.' : 'Adopt the updated work plan.',
        attendance: attendees.attendance.map((a) => ({
          memberId: a.memberId,
          status: a.externalName ? 'excused' : 'present',
          note: null,
        })),
        aiDrafted: false,
        version: meeting.version,
      }),
    );
    await json(
      await apiPost(ahmedApi, `/api/v1/teams/meetings/${meeting.id}/submit-minutes`, {
        version: saved.version,
        comment: null,
        reason: null,
      }),
    );
    for (let i = 1; i <= 4; i++) {
      const recommendation = await json<{ id: string; version: number }>(
        await apiPost(ahmedApi, `/api/v1/teams/${draft.id}/recommendations`, {
          meetingId: meeting.id,
          kind: 'recommendation',
          text:
            language === 'ar'
              ? `التوصية رقم ${i}: تحديث مؤشرات الأداء`
              : `Recommendation ${i}: update the performance indicators`,
          ownerEmployeeId: ahmed,
          dueDate: `${year + 1}-03-31`,
          version: 0,
        }),
      );
      if (i <= 3)
        await json(
          await apiPost(ahmedApi, `/api/v1/teams/recommendations/${recommendation.id}/status`, {
            status: 'closed',
            note: 'done',
            evidenceFileIds: null,
            version: recommendation.version,
          }),
        );
    }
    await page.goto(`/teams/meetings/${meeting.id}`);
    await expect(page.getByTestId('meeting-minutes')).toBeVisible();
    await shot('team-meeting-minutes', { fullPage: true });
    await page.goto(`/teams/${draft.id}?tab=recommendations`);
    await expect(page.getByTestId('team-recommendations')).toContainText(
      k(language, 'recommendations.statuses.closed'),
    );
    await shot('team-recommendations', { fullPage: true });

    // 5. HR opens the evaluation; the President (default evaluator) sees the suggested levels and the data behind them.
    const opened = await json<{ id: string }>(
      await apiPost(adminApi, `/api/v1/teams/${draft.id}/evaluations`, { year }),
    );
    const ahmedPage = await pageAs(browser, baseURL ?? '', language, 'ahmed');
    await ahmedPage.goto(`/teams/evaluations/${opened.id}`);
    await expect(ahmedPage.getByTestId('suggestion-recommendations')).toBeVisible();
    await expect(ahmedPage.getByTestId('suggestion-governance')).toBeVisible();
    await ahmedPage.evaluate(() => document.fonts.ready);
    await ahmedPage.screenshot({ path: screenshotPath(language, 'team-evaluation-wizard'), fullPage: true });
    await ahmedPage.context().close();

    const evaluation = await json<{
      version: number;
      criteria: { code: string; suggestedLevel: string | null }[];
      members: { id: string; canEvaluate: boolean }[];
    }>(await ahmedApi.get(`/api/v1/teams/evaluations/${opened.id}`));
    const suggestion = (code: string) =>
      evaluation.criteria.find((c) => c.code === code)?.suggestedLevel ?? null;
    expect(suggestion('recommendations')).toBe('low'); // 3 of 4 = 75 %
    expect(suggestion('governance')).toBe('late'); // 25 h
    const filled = await json<{ version: number }>(
      await apiPut(ahmedApi, `/api/v1/teams/evaluations/${opened.id}`, {
        criteria: [
          { code: 'recommendations', level: 'low', comment: null, overrideJustification: null },
          { code: 'effectiveness', level: 'steering', comment: null, overrideJustification: null },
          { code: 'quality', level: 'feedback', comment: null, overrideJustification: null },
          { code: 'governance', level: 'late', comment: null, overrideJustification: null },
          { code: 'cooperation', level: 'shared', comment: null, overrideJustification: null },
        ],
        workload: 'high',
        evidenceFileIds: null,
        members: evaluation.members.map((m) => ({
          id: m.id,
          answers: { contribution: 5, commitment: 5, communication: 4 },
          comment: null,
        })),
        version: evaluation.version,
      }),
    );
    await json(
      await apiPost(ahmedApi, `/api/v1/teams/evaluations/${opened.id}/submit`, {
        version: filled.version,
        comment: null,
        reason: null,
      }),
    );

    // 6. HR review with the approval panel and previous cycles, then approval.
    await page.goto(`/teams/evaluations/${opened.id}`);
    await page.getByRole('tab', { name: k(language, 'evaluation.review') }).click();
    await expect(page.getByTestId('evaluation-summary').first()).toBeVisible();
    await shot('team-hr-review', { fullPage: true });
    await approve(adminApi, draft.id);

    // 7. Rewards from the matrix, HR submits, the owning unit's head approves, certificates are issued.
    await page.reload();
    await page.getByRole('tab', { name: k(language, 'rewards.title') }).click();
    await expect(page.getByTestId('evaluation-rewards')).toBeVisible();
    await shot('team-rewards', { fullPage: true });
    const withRewards = await json<{ version: number }>(
      await adminApi.get(`/api/v1/teams/evaluations/${opened.id}`),
    );
    await json(
      await apiPost(adminApi, `/api/v1/teams/evaluations/${opened.id}/rewards/submit`, {
        version: withRewards.version,
        comment: null,
        reason: null,
      }),
    );
    await approve(ahmedApi, draft.id);
    const rewarded = await poll(
      async () =>
        json<{ members: { certificate: boolean; certificateFileId: string | null }[] }>(
          await adminApi.get(`/api/v1/teams/evaluations/${opened.id}`),
        ),
      (e) => e.members.filter((m) => m.certificate).every((m) => m.certificateFileId !== null),
    );
    const certificate = rewarded.members.find((m) => m.certificateFileId !== null);
    if (!certificate?.certificateFileId) throw new Error('No certificate');
    await pdfShot(adminApi, certificate.certificateFileId, language, 'team-certificate-pdf');
    await page.reload();
    await page.getByRole('tab', { name: k(language, 'rewards.title') }).click();
    await expect(page.getByTestId('evaluation-rewards')).toContainText('CERT-');
    await shot('team-rewards-certificates', { fullPage: true });

    // 8. A member signs in and cannot see the results; the President can.
    const memberApi = await playwright.request.newContext({ baseURL });
    const login = await apiPost(memberApi, '/api/v1/identity/auth/login', {
      login: member.email,
      password: member.password,
    });
    expect(login.status()).toBe(200);
    expect((await memberApi.get(`/api/v1/teams/evaluations/${opened.id}`)).status()).toBe(403);
    expect(
      ((await (await memberApi.get(`/api/v1/teams/${draft.id}`)).json()) as { lastScore: number | null })
        .lastScore,
    ).toBeNull();
    expect((await ahmedApi.get(`/api/v1/teams/evaluations/${opened.id}`)).status()).toBe(200);
    await memberApi.dispose();

    // 9. The employee profile's Teams tab, the register and the dashboard.
    await page.goto(`/org/employees/${memberEmployee}`);
    await page.getByRole('tab', { name: k(language, 'employeeTab.title') }).click();
    await expect(page.getByTestId('employee-teams')).toContainText(tag);
    await shot('employee-teams-tab');
    await page.goto('/teams');
    await expect(page.getByTestId('teams-register')).toContainText(tag);
    await shot('teams-register', { fullPage: true });
    await page.goto('/teams/dashboard');
    await expect(page.getByTestId('teams-dashboard')).toBeVisible();
    await shot('teams-dashboard', { fullPage: true });

    await ahmedApi.post('/api/v1/identity/auth/logout').catch(() => undefined);
    await ahmedApi.dispose();
  });
});
