import { execFileSync } from 'node:child_process';
import { mkdtempSync, readdirSync, renameSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import type { APIRequestContext, Page } from '@playwright/test';
import {
  apiPost,
  apiPut,
  expect,
  recordSection,
  screenshotPath,
  signInViaApi,
  test,
  tr,
  type Language,
} from './fixtures';

/**
 * Phase 10 — Agile / hybrid, PM standards, Gantt and portfolio views (docs/design/portfolio.md §29–§45), in Arabic (RTL) and English (LTR).
 * An agile initiative runs two sprints with the board and a monthly roll-up approval; a 30-milestone waterfall initiative shows the Gantt
 * (rendered < 5 s), a re-plan change request, health, baseline variance, EVM, RAID and RACI; the status report PDF and the portfolio dashboard.
 * Screenshots go to docs/screenshots/phase-10 (E2E_SCREENSHOTS=phase-10).
 */

const k = (language: Language, key: string, params?: Record<string, string | number>) =>
  tr(language, `portfolio:${key}`, params);

async function json<T>(response: Awaited<ReturnType<APIRequestContext['get']>>): Promise<T> {
  expect(response.ok(), await response.text()).toBe(true);
  return (await response.json()) as T;
}

interface ProjectDto {
  id: string;
  version: number;
  subState: string;
  performance: number;
  delivery: { methodology: string };
}

interface AgileView {
  epics: { id: string; code: string }[];
  stories: {
    id: string;
    title: string;
    epicId: string;
    version: number;
    storyPoints: number;
    asA: string;
    iWant: string;
    soThat: string;
    priority: string;
  }[];
  sprints: { id: string; name: string; version: number; status: string }[];
  releases: { id: string }[];
}

const year = new Date().getFullYear();
const iso = (d: Date) => d.toISOString().slice(0, 10);
const daysFromToday = (n: number) => iso(new Date(Date.now() + n * 86_400_000));
const pdf = Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF\n');
const base = (id: string) => `/api/v1/portfolio/projects/${id}`;

async function antiforgery(api: APIRequestContext) {
  const token = (await (await api.get('/api/v1/antiforgery/token')).json()) as {
    token: string;
    headerName: string;
  };
  return { [token.headerName]: token.token, 'X-Client': 'spa' };
}

async function project(api: APIRequestContext, id: string) {
  return json<ProjectDto>(await api.get(base(id)));
}

async function approve(admin: APIRequestContext, subject: string, id: string, times = 1) {
  for (let i = 0; i < times; i++) {
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
}

async function employeeId(api: APIRequestContext, number: string): Promise<string> {
  const page = (await (await api.get('/api/v1/org/employees', { params: { search: number } })).json()) as {
    items: { id: string; employeeNo: string }[];
  };
  const found = page.items.find((e) => e.employeeNo === number);
  if (!found) throw new Error(`Employee ${number} not found`);
  return found.id;
}

async function openTab(page: Page, language: Language, id: string, tab: string) {
  await page.goto(`/portfolio/projects/${id}?tab=${tab}`);
  await expect(recordSection(page, language, k(language, `project.tabs.${tab}`))).toHaveAttribute(
    'aria-current',
    'page',
  );
}

/** An initiative (a screening answer is No, Q-P5): Take Ownership → Planning without charter, LPO or team formation (Q-P2). */
async function initiative(ahmed: APIRequestContext, admin: APIRequestContext, name: string) {
  const values = {
    nameAr: `مبادرة ${name}`,
    nameEn: name,
    description: 'Deliver online services for permits.',
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
    scopeOfWork: 'Online forms, payments and tracking.',
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
  await approve(admin, 'portfolio.request', request.id, 3); // director → PM Office → Budget
  const approved = await json<{ project: { id: string } | null }>(
    await ahmed.get(`/api/v1/portfolio/requests/${request.id}`),
  );
  const id = approved.project?.id ?? '';
  expect(id).not.toBe('');
  const p = await project(ahmed, id);
  await json(await apiPost(ahmed, `${base(id)}/take-ownership`, { version: p.version }));
  await json(
    await apiPost(ahmed, `${base(id)}/planning-items`, {
      values: { category: 'in-scope', description: 'Online services' },
    }),
  );
  return id;
}

async function portfolioSettings(
  admin: APIRequestContext,
  change: (s: Record<string, unknown>) => Record<string, unknown>,
) {
  const current = await json<{ value: Record<string, unknown>; version: number }>(
    await admin.get('/api/v1/portfolio/settings'),
  );
  await json(
    await apiPut(admin, '/api/v1/portfolio/settings', {
      settings: change(current.value),
      version: current.version,
    }),
  );
}

async function move(
  ahmed: APIRequestContext,
  view: AgileView,
  story: string,
  column: string,
): Promise<AgileView> {
  const version = view.stories.find((s) => s.id === story)?.version ?? 0;
  return json<AgileView>(
    await apiPost(ahmed, `/api/v1/portfolio/stories/${story}/move`, { column, rank: 1, version }),
  );
}

test.describe('portfolio — agile, standards, Gantt and portfolio views', () => {
  test('agile sprints with board, burndown and monthly roll-up; Gantt, health, baseline, EVM, RAID, RACI, status report and dashboard', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    baseURL,
  }) => {
    test.setTimeout(900_000);
    const tag = `${language}${Date.now().toString(36)}`;
    const ahmed = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmed, 'ahmed');
    const ahmedId = await employeeId(adminApi, 'E0002');
    const mariamId = await employeeId(adminApi, 'E0001');
    await portfolioSettings(adminApi, (s) => ({
      ...s,
      features: { ...(s.features as object), earnedValue: true },
    }));

    // 1. Methodology choice on an initiative in planning (no charter: chosen before the first planning submission).
    const id = await initiative(ahmed, adminApi, `Agile permits ${tag}`);
    await openTab(page, language, id, 'overview');
    const card = page.getByTestId('methodology-card');
    await expect(card).toBeVisible();
    await card.getByRole('button').first().click();
    const methodologyDialog = page.getByTestId('methodology-dialog');
    await expect(methodologyDialog).toBeVisible();
    await shot('methodology-choice');
    await page.keyboard.press('Escape');
    let p = await project(ahmed, id);
    await json(
      await apiPut(ahmed, `${base(id)}/methodology`, {
        methodology: 'agile',
        sprintLengthDays: 14,
        definitionOfDone: 'Tested, reviewed and deployed',
        agileApprovalMode: 'monthly',
        version: p.version,
      }),
    );

    // 2. AI plan generator review (demo provider): a draft, nothing saved until "Add".
    await openTab(page, language, id, 'overview');
    await page.getByTestId('ai-plan').click();
    const planDialog = page.getByTestId('ai-plan-dialog');
    await expect(planDialog).toBeVisible();
    await expect(planDialog.getByRole('checkbox').first()).toBeVisible({ timeout: 30_000 });
    await shot('ai-plan-generator-review');
    await page.keyboard.press('Escape');

    // 3. Release plan, epics (60 / 40) and stories; planning approval → execution.
    let view = await json<AgileView>(
      await apiPost(ahmed, `${base(id)}/releases`, {
        values: {
          nameAr: 'الإصدار الأول',
          nameEn: 'Release 1',
          goal: 'Online permits',
          targetDate: daysFromToday(90),
          status: null,
          sort: 0,
        },
      }),
    );
    const release = view.releases[0]?.id ?? null;
    for (const [ar, en, weight] of [
      ['بوابة الخدمات', 'Service portal', 0.6],
      ['التتبع والإشعارات', 'Tracking and notifications', 0.4],
    ] as const) {
      view = await json<AgileView>(
        await apiPost(ahmed, `${base(id)}/epics`, {
          values: {
            titleAr: ar,
            titleEn: en,
            weight,
            releaseId: release,
            milestoneId: null,
            acceptanceCriteria: null,
            sort: 0,
            status: null,
          },
        }),
      );
    }
    const [epic1, epic2] = [...view.epics].sort((a, b) => a.code.localeCompare(b.code));
    const storyTitles: [string, string, number][] = [
      [epic1?.id ?? '', language === 'ar' ? 'تقديم الطلب إلكترونياً' : 'Submit online', 5],
      [epic1?.id ?? '', language === 'ar' ? 'الدفع الإلكتروني' : 'Pay online', 5],
      [epic2?.id ?? '', language === 'ar' ? 'تتبع حالة الطلب' : 'Track status', 3],
      [epic2?.id ?? '', language === 'ar' ? 'إشعار بالبريد' : 'Email notice', 2],
    ];
    for (const [epicId, title, points] of storyTitles) {
      view = await json<AgileView>(
        await apiPost(ahmed, `${base(id)}/stories`, {
          values: {
            epicId,
            title,
            asA: 'applicant',
            iWant: title,
            soThat: 'I save time',
            acceptanceCriteria: null,
            storyPoints: points,
            priority: 'must',
            assigneeEmployeeId: ahmedId,
            sprintId: null,
          },
        }),
      );
    }
    const stories = view.stories.map((s) => s.id);
    p = await project(ahmed, id);
    await json(await apiPost(ahmed, `${base(id)}/submit-planning`, { version: p.version }));
    await approve(adminApi, 'portfolio.project', id, 2);
    await expect.poll(async () => (await project(ahmed, id)).subState, { timeout: 60_000 }).toBe('execution');

    // 4. Two sprints: the board (WIP limits, swimlanes) and the burndown.
    const sprint = async (name: string, ids: string[]) => {
      view = await json<AgileView>(
        await apiPost(ahmed, `${base(id)}/sprints`, {
          values: {
            name,
            goal: `${name} goal`,
            startDate: daysFromToday(0),
            endDate: null,
            capacityPoints: 12,
          },
        }),
      );
      const sid = view.sprints.find((s) => s.name === name)?.id ?? '';
      for (const storyId of ids) {
        const s = view.stories.find((x) => x.id === storyId);
        if (!s) throw new Error('story');
        view = await json<AgileView>(
          await apiPut(ahmed, `${base(id)}/stories/${storyId}`, {
            values: {
              epicId: s.epicId,
              title: s.title,
              asA: s.asA,
              iWant: s.iWant,
              soThat: s.soThat,
              acceptanceCriteria: null,
              storyPoints: s.storyPoints,
              priority: s.priority,
              assigneeEmployeeId: ahmedId,
              sprintId: sid,
            },
            version: s.version,
          }),
        );
      }
      view = await json<AgileView>(
        await apiPost(ahmed, `/api/v1/portfolio/sprints/${sid}/start`, {
          version: view.sprints.find((x) => x.id === sid)?.version,
        }),
      );
      return sid;
    };
    const s1 = await sprint('Sprint 1', [stories[0] ?? '', stories[1] ?? '']);
    view = await move(ahmed, view, stories[0] ?? '', 'in-progress');
    view = await move(ahmed, view, stories[1] ?? '', 'review');
    await openTab(page, language, id, 'board');
    await expect(page.getByTestId('agile-board')).toBeVisible();
    await shot('agile-board');
    view = await move(ahmed, view, stories[0] ?? '', 'done');
    view = await move(ahmed, view, stories[1] ?? '', 'done');
    view = await json<AgileView>(
      await apiPost(ahmed, `/api/v1/portfolio/sprints/${s1}/close`, {
        values: {
          demoOutcome: 'Online form demonstrated',
          stakeholderFeedback: 'Positive',
          wentWell: 'Daily stand-ups',
          toImprove: 'Earlier test data',
          actions: 'Prepare data in sprint planning',
        },
        version: view.sprints.find((x) => x.id === s1)?.version,
      }),
    );
    const s2 = await sprint('Sprint 2', [stories[2] ?? '', stories[3] ?? '']);
    view = await move(ahmed, view, stories[2] ?? '', 'done');
    await openTab(page, language, id, 'sprints');
    await expect(page.getByTestId('sprint-burndown')).toBeVisible();
    await shot('sprint-burndown', { fullPage: true });
    view = await json<AgileView>(
      await apiPost(ahmed, `/api/v1/portfolio/sprints/${s2}/close`, {
        values: { demoOutcome: 'Tracking page demonstrated' },
        version: view.sprints.find((x) => x.id === s2)?.version,
      }),
    );

    // 5. Monthly roll-up: the window carries both sprints into one approval; the narrative feeds the status report.
    await json(await apiPost(adminApi, `${base(id)}/monthly/open`));
    await json(
      await apiPut(ahmed, `${base(id)}/monthly/narrative`, {
        narrativeAr: 'أُنجز السبرنت الأول بالكامل وجزء من الثاني؛ بقيت قصة الإشعارات للسبرنت القادم.',
        narrativeEn:
          'Sprint 1 was fully delivered and most of sprint 2; the notification story moves to the next sprint.',
      }),
    );
    p = await project(ahmed, id);
    await json(
      await apiPost(ahmed, `${base(id)}/monthly/submit`, { version: p.version, comment: 'Two sprints' }),
    );
    await openTab(page, language, id, 'monthly');
    await expect(page.getByTestId('project-page')).toBeVisible();
    await shot('monthly-rollup-approval', { fullPage: true });
    await approve(adminApi, 'portfolio.project', id, 2);
    await expect
      .poll(async () => (await project(ahmed, id)).performance, { timeout: 60_000 })
      .toBeCloseTo(0.84, 2);

    // 6. Status report PDF from the approved update, rendered to a PNG for review.
    const report = await ahmed.get(`${base(id)}/status-report?language=${language}`);
    expect(report.status(), await report.text()).toBe(200);
    const dir = mkdtempSync(join(tmpdir(), 'status-report-'));
    writeFileSync(join(dir, 'report.pdf'), await report.body());
    execFileSync('pdftoppm', [
      '-png',
      '-r',
      '80',
      '-f',
      '1',
      '-l',
      '1',
      join(dir, 'report.pdf'),
      join(dir, 'page'),
    ]);
    const png = readdirSync(dir).find((f) => f.endsWith('.png'));
    if (!png) throw new Error('pdftoppm produced no page');
    renameSync(join(dir, png), screenshotPath(language, 'status-report-pdf'));

    // 7. A 30-milestone waterfall initiative: Gantt under 5 s, then a re-plan change request and the standards tabs.
    const wf = await initiative(ahmed, adminApi, `Gantt permits ${tag}`);
    const milestone = async (values: Record<string, unknown>) =>
      json<{ id: string; version: number }>(
        await apiPost(ahmed, `${base(wf)}/milestones`, {
          values: {
            titleAr: null,
            titleEn: null,
            parentId: null,
            isHeader: false,
            highLevel: false,
            plannedStart: null,
            plannedEnd: null,
            weight: 0,
            cost: null,
            isDeliverable: false,
            mainDeliverable: null,
            acceptanceCriteria: null,
            qualityMet: null,
            isInnovative: false,
            innovativeReason: null,
            sort: 0,
            deliveredInSprints: false,
            ...values,
          },
        }),
      );
    const phases =
      language === 'ar'
        ? ['التحليل', 'التصميم', 'البناء', 'الاختبار', 'الإطلاق']
        : ['Analysis', 'Design', 'Build', 'Testing', 'Launch'];
    let firstLeaf = { id: '', version: 0 };
    for (let f = 0; f < 5; f++) {
      const phase = await milestone({ titleAr: phases[f], titleEn: phases[f], isHeader: true, sort: f });
      for (let m = 0; m < 6; m++) {
        const n = f * 6 + m;
        const start = -120 + n * 10;
        const leaf = await milestone({
          titleAr: `${phases[f]} ${m + 1}`,
          titleEn: `${phases[f]} ${m + 1}`,
          parentId: phase.id,
          plannedStart: daysFromToday(start),
          plannedEnd: daysFromToday(start + 14),
          weight: n === 29 ? 0.0343 : 0.0333,
          cost: 1000,
          sort: m,
          isDeliverable: n === 29,
          mainDeliverable: n === 29 ? 'Live service' : null,
          acceptanceCriteria: n === 29 ? 'Accepted by the department' : null,
        });
        if (n === 0) firstLeaf = leaf;
      }
    }
    p = await project(ahmed, wf);
    await json(await apiPost(ahmed, `${base(wf)}/submit-planning`, { version: p.version }));
    await approve(adminApi, 'portfolio.project', wf, 2);
    await expect.poll(async () => (await project(ahmed, wf)).subState, { timeout: 60_000 }).toBe('execution');
    const started = Date.now();
    await page.goto(`/portfolio/projects/${wf}?tab=gantt`);
    await expect(page.getByTestId('project-gantt').locator('svg[data-gantt]').first()).toBeVisible();
    const elapsed = Date.now() - started;
    expect(elapsed, `Gantt of 30 milestones rendered in ${elapsed} ms`).toBeLessThan(5_000);
    await shot('gantt-project', { fullPage: true });

    // Re-plan through an approved change request: the first milestone ends 15 days later; the variance shows it against baseline v1.
    const cr = await json<{ id: string; version: number }>(
      await apiPost(ahmed, `${base(wf)}/change-requests`, {
        values: {
          type: 'details',
          areas: ['plan', 'dates'],
          reason: 'Vendor slipped',
          impactOfNonResponse: null,
          proposedEnd: daysFromToday(240),
          costAffected: false,
        },
      }),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/change-requests/${cr.id}/submit`, { version: cr.version }),
    );
    await approve(adminApi, 'portfolio.project', wf, 2);
    const plan = await json<{
      milestones: {
        id: string;
        version: number;
        plannedStart: string;
        plannedEnd: string;
        titleEn: string;
        weight: number;
        parentId: string | null;
      }[];
    }>(await ahmed.get(`${base(wf)}/milestones`));
    const first = plan.milestones.find((m) => m.id === firstLeaf.id);
    if (!first) throw new Error('first milestone');
    await json(
      await apiPut(ahmed, `/api/v1/portfolio/milestones/${first.id}`, {
        values: {
          titleAr: first.titleEn,
          titleEn: first.titleEn,
          parentId: first.parentId,
          isHeader: false,
          highLevel: false,
          plannedStart: first.plannedStart,
          plannedEnd: iso(new Date(Date.parse(first.plannedEnd) + 15 * 86_400_000)),
          weight: first.weight,
          cost: 1000,
          isDeliverable: false,
          mainDeliverable: null,
          acceptanceCriteria: null,
          qualityMet: null,
          isInnovative: false,
          innovativeReason: null,
          sort: 0,
          deliveredInSprints: false,
        },
        version: first.version,
      }),
    );
    const approvedCr = await json<{ version: number }>(
      await ahmed.get(`/api/v1/portfolio/change-requests/${cr.id}`),
    );
    await json(
      await apiPost(ahmed, `/api/v1/portfolio/change-requests/${cr.id}/apply`, {
        values: { rebaseline: false },
        version: approvedCr.version,
      }),
    );

    // Health with an override, baseline variance, EVM (actual cost recorded), RAID and RACI.
    p = await project(ahmed, wf);
    await json(
      await apiPut(ahmed, `${base(wf)}/health/scope`, {
        value: 'amber',
        justification: 'Re-plan approved this month',
        version: p.version,
      }),
    );
    for (const values of [
      {
        type: 'issue',
        title: language === 'ar' ? 'تأخر بيئة الاختبار' : 'Test environment late',
        priority: 'high',
        ownerEmployeeId: ahmedId,
        dueDate: daysFromToday(10),
      },
      {
        type: 'assumption',
        title: language === 'ar' ? 'توفر واجهات الدفع' : 'Payment APIs available',
        priority: 'medium',
      },
      {
        type: 'dependency',
        title: language === 'ar' ? 'يعتمد على مشروع البوابة' : 'Depends on the portal project',
        priority: 'high',
        dependsOnProjectId: id,
      },
      {
        type: 'decision',
        title: language === 'ar' ? 'اعتماد الدفع الإلكتروني فقط' : 'Online payment only',
        priority: 'low',
        decidedOn: daysFromToday(-3),
        decidedBy: 'Steering committee',
      },
    ]) {
      await json(
        await apiPost(ahmed, `${base(wf)}/raid`, {
          values: {
            description: null,
            dueDate: null,
            status: null,
            resolution: null,
            milestoneId: null,
            dependsOnProjectId: null,
            dependsOnMilestoneId: null,
            decidedOn: null,
            decidedBy: null,
            ownerEmployeeId: null,
            ...values,
          },
        }),
      );
    }
    for (const [area, employee, role] of [
      ['Design', ahmedId, 'R'],
      ['Design', mariamId, 'A'],
      ['Build', ahmedId, 'A'],
      ['Build', mariamId, 'I'],
    ] as const) {
      await json(
        await apiPost(ahmed, `${base(wf)}/raci`, {
          values: { area, milestoneId: null, employeeId: employee, role },
        }),
      );
    }
    await openTab(page, language, wf, 'health');
    await expect(page.getByTestId('health-panel')).toBeVisible();
    await shot('rag-health');
    await page.getByTestId('baseline-variance').scrollIntoViewIfNeeded();
    await expect(page.getByTestId('baseline-variance')).toBeVisible();
    await page
      .getByTestId('baseline-variance')
      .screenshot({ path: screenshotPath(language, 'baseline-variance') });
    await page.getByTestId('evm-panel').scrollIntoViewIfNeeded();
    await page.getByTestId('evm-panel').screenshot({ path: screenshotPath(language, 'evm-panel') });
    await openTab(page, language, wf, 'raid');
    await expect(page.getByTestId('raid-log')).toBeVisible();
    await shot('raid-log', { fullPage: true });
    await openTab(page, language, wf, 'raci');
    await expect(page.getByTestId('raci-matrix')).toBeVisible();
    await shot('raci');

    // 8. Portfolio views: the dashboard (agile and waterfall together) and the portfolio-wide Gantt.
    await page.goto('/portfolio/dashboard');
    await expect(page.getByTestId('portfolio-dashboard')).toBeVisible();
    await page.waitForTimeout(800); // chart animation
    await shot('portfolio-dashboard', { fullPage: true });
    await page.goto('/portfolio/gantt');
    await expect(page.getByTestId('portfolio-gantt')).toBeVisible();
    await shot('gantt-portfolio');

    await portfolioSettings(adminApi, (s) => ({
      ...s,
      features: { ...(s.features as object), earnedValue: false },
    }));
    await ahmed.dispose();
  });
});
