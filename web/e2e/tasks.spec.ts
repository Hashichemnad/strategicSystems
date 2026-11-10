import type { APIRequestContext, Page } from '@playwright/test';
import {
  apiPost,
  createItemViaApi,
  createUserWithPassword,
  expect,
  signInViaApi,
  test,
  tr,
  unitIdByCode,
  type Language,
} from './fixtures';

/** Phase 6 — Task Management (docs/design/tasks.md): screenshots go to docs/screenshots/phase-6 (E2E_SCREENSHOTS=phase-6). */

async function employeeIdByNumber(api: APIRequestContext, number: string): Promise<string> {
  const page = (await (await api.get('/api/v1/org/employees', { params: { search: number } })).json()) as {
    items: { id: string; employeeNo: string }[];
  };
  const employee = page.items.find((e) => e.employeeNo === number);
  if (!employee) throw new Error(`Employee ${number} not found`);
  return employee.id;
}

async function createEmployee(
  api: APIRequestContext,
  tag: string,
  ar: string,
  en: string,
  managerId: string,
) {
  const response = await apiPost(api, '/api/v1/org/employees', {
    employeeNo: `T-${tag}-${en.slice(0, 3)}`,
    nameAr: `${ar} ${tag}`,
    nameEn: `${en} ${tag}`,
    email: `${en.toLowerCase()}.${tag}@strategy.local`,
    unitId: await unitIdByCode(api, 'DEP-STR'),
    managerEmployeeId: managerId,
    joinDate: '2024-01-01',
  });
  expect(response.status()).toBe(201);
  return ((await response.json()) as { id: string }).id;
}

async function act(
  api: APIRequestContext | Page,
  id: string,
  action: string,
  body: Record<string, unknown> = {},
) {
  const request = 'goto' in api ? api.request : api;
  const task = (await (await request.get(`/api/v1/tasks/${id}`)).json()) as { version: number };
  const response = await apiPost(api, `/api/v1/tasks/${id}/${action}`, { version: task.version, ...body });
  expect(response.status(), await response.text()).toBe(200);
}

async function pickEmployee(page: Page, scope: ReturnType<Page['getByTestId']>, search: string) {
  await scope.locator('input[role="combobox"]').fill(search);
  await page.getByRole('option').filter({ hasText: search }).first().click();
}

function inDays(days: number): string {
  const date = new Date();
  const target = new Date(
    date.getFullYear(),
    date.getMonth(),
    Math.min(date.getDate() + days, new Date(date.getFullYear(), date.getMonth() + 1, 0).getDate()),
  );
  return `${target.getFullYear()}-${String(target.getMonth() + 1).padStart(2, '0')}-${String(target.getDate()).padStart(2, '0')}`;
}

const k = (language: Language, key: string, params?: Record<string, string | number>) =>
  tr(language, `tasks:${key}`, params);

test.describe('task management', () => {
  test.use({ signedInAs: 'mariam' });

  test('manager assigns one task to three people, the assignee pauses and restarts, the creator approves with a feedback point', async ({
    page,
    language,
    shot,
    adminApi,
    playwright,
    baseURL,
  }) => {
    test.setTimeout(180_000);
    const tag = `${language}${Date.now().toString(36)}`;
    const mariam = await employeeIdByNumber(adminApi, 'E0001');
    const ahmed = await employeeIdByNumber(adminApi, 'E0002');
    await createEmployee(adminApi, tag, 'فاطمة', 'Fatima', mariam);
    await createEmployee(adminApi, tag, 'خالد', 'Khalid', mariam);

    // New Task with three assignees: one task each, tied by an assignment group.
    await page.goto('/tasks');
    await expect(page.getByRole('heading', { level: 1, name: k(language, 'title') })).toBeVisible();
    await page.getByTestId('new-task').click();
    const dialog = page.getByRole('dialog');
    await dialog.getByLabel(k(language, 'fields.titleAr')).fill(`إعداد التقرير الربعي ${tag}`);
    await dialog.getByLabel(k(language, 'fields.titleEn')).fill(`Prepare the quarterly report ${tag}`);
    await dialog
      .getByLabel(k(language, 'fields.description'))
      .fill(
        language === 'ar'
          ? 'جمع بيانات الإدارات وإعداد المسودة الأولى.'
          : 'Collect the departments’ data and prepare the first draft.',
      );
    const assignees = page.getByTestId('assignees');
    await pickEmployee(page, assignees, language === 'ar' ? 'أحمد' : 'Ahmed');
    await pickEmployee(page, assignees, `${language === 'ar' ? 'فاطمة' : 'Fatima'} ${tag}`);
    await pickEmployee(page, assignees, `${language === 'ar' ? 'خالد' : 'Khalid'} ${tag}`);
    await expect(dialog.getByText(k(language, 'newTask.multiHint', { count: 3 }))).toBeVisible();
    await shot('tasks-new-multi-assign');
    await dialog.getByTestId('create-task').click();
    await expect(dialog).toBeHidden();

    const list = (await (
      await page.request.get('/api/v1/tasks', { params: { view: 'created', search: tag } })
    ).json()) as {
      items: { id: string; assignee: { employeeId: string }; groupId: string }[];
    };
    expect(list.items).toHaveLength(3);
    const ahmedTask = list.items.find((t) => t.assignee.employeeId === ahmed);
    if (!ahmedTask) throw new Error('No task for Ahmed');

    // Ahmed accepts, pauses with a hold reason, starts again and submits (his own session).
    const ahmedApi = await playwright.request.newContext({ baseURL });
    await signInViaApi(ahmedApi, 'ahmed');
    await act(ahmedApi, ahmedTask.id, 'accept');
    await act(ahmedApi, ahmedTask.id, 'pause', {
      reason: language === 'ar' ? 'بانتظار بيانات المالية' : 'Waiting for finance data',
    });
    await act(ahmedApi, ahmedTask.id, 'start');
    await act(ahmedApi, ahmedTask.id, 'submit', {
      comment: language === 'ar' ? 'المسودة الأولى مرفقة' : 'First draft attached',
    });
    await ahmedApi.post('/api/v1/identity/auth/logout').catch(() => undefined);
    await ahmedApi.dispose();

    // The task page: timeline with durations and the timesheet with the hold reason.
    await page.goto(`/tasks/${ahmedTask.id}?tab=timeline`);
    await expect(page.getByTestId('task-timeline')).toContainText(k(language, 'timeline.pause'));
    await expect(page.getByTestId('timesheet')).toContainText(
      language === 'ar' ? 'بانتظار بيانات المالية' : 'Waiting for finance data',
    );
    await shot('task-page-timeline', { fullPage: true });

    // The creator approves from the current-status banner with a feedback point.
    await page.goto(`/tasks/${ahmedTask.id}`);
    await page
      .getByRole('status')
      .getByRole('button', { name: k(language, 'actions.approve') })
      .click();
    const approve = page.getByRole('dialog');
    await approve.getByRole('combobox').click();
    await page.getByRole('option', { name: '4', exact: true }).click();
    await approve.getByTestId('task-action-confirm').click();
    await expect(approve).toBeHidden();
    await expect(
      page
        .getByRole('status')
        .filter({ hasText: k(language, 'currentStatus.approved') })
        .first(),
    ).toBeVisible();

    // The assignment group shows "1 of 3 approved".
    await page.goto(`/tasks/groups/${ahmedTask.groupId}`);
    await expect(page.getByTestId('group-progress')).toHaveText(
      k(language, 'group.progress', { finished: 1, total: 3 }),
    );
    await shot('task-assignment-group');

    // More tasks with due dates this month for the list, Kanban and calendar.
    for (const [ar, en, due] of [
      ['مراجعة سجل المخاطر', 'Review the risk register', inDays(2)],
      ['تحديث مؤشرات الأداء', 'Update the KPI results', inDays(5)],
      ['إعداد عرض اللجنة', 'Prepare the committee pack', inDays(1)],
    ] as const) {
      const response = await apiPost(page, '/api/v1/tasks', {
        titleAr: `${ar} ${tag}`,
        titleEn: `${en} ${tag}`,
        typeCode: 'manager-assignment',
        sourceCode: 'manager-assignment',
        priority: 'medium',
        dueDate: due,
        assigneeEmployeeIds: [ahmed],
      });
      expect(response.status()).toBe(201);
    }

    await page.goto('/tasks?tab=created');
    await expect(page.getByTestId('task-row').first()).toBeVisible();
    await shot('tasks-list');
    await page.goto('/tasks?tab=created&view=kanban');
    await expect(page.getByTestId('kanban-card').first()).toBeVisible();
    await shot('tasks-kanban');
    await page.goto('/tasks?tab=created&view=calendar');
    await expect(page.getByTestId('task-calendar')).toBeVisible();
    await shot('tasks-calendar');
    await page.goto('/tasks/dashboard');
    await expect(page.getByTestId('task-dashboard')).toBeVisible();
    await shot('tasks-dashboard', { fullPage: true });
  });

  test('an employee who leaves is offered for reassignment and their open tasks move to a colleague', async ({
    page,
    language,
    shot,
    adminApi,
  }) => {
    const tag = `${language}${Date.now().toString(36)}`;
    const mariam = await employeeIdByNumber(adminApi, 'E0001');
    const leaver = await createEmployee(adminApi, tag, 'سالم', 'Salem', mariam);
    for (const [ar, en] of [
      ['تحديث سجل المخاطر', 'Update the risk register'],
      ['إعداد محضر الاجتماع', 'Write the meeting minutes'],
    ] as const) {
      const response = await apiPost(page, '/api/v1/tasks', {
        titleAr: `${ar} ${tag}`,
        titleEn: `${en} ${tag}`,
        typeCode: 'manager-assignment',
        sourceCode: 'manager-assignment',
        dueDate: inDays(3),
        assigneeEmployeeIds: [leaver],
      });
      expect(response.status()).toBe(201);
    }
    const employee = (await (await adminApi.get(`/api/v1/org/employees/${leaver}`)).json()) as {
      version: number;
    };
    expect(
      (
        await apiPost(adminApi, `/api/v1/org/employees/${leaver}/change-status`, {
          status: 'left',
          version: employee.version,
        })
      ).status(),
    ).toBe(200);

    // The employee page offers the action once the employee has left.
    await page.goto(`/org/employees/${leaver}`);
    const offer = page.getByTestId('reassign-offer');
    await expect(offer).toBeVisible();
    await shot('employee-left-offer');
    await offer.click();

    // Preview, choose the colleague, give a reason, confirm.
    await expect(page.getByTestId('reassign-preview').getByRole('row')).toHaveCount(3);
    await pickEmployee(page, page.locator('form'), language === 'ar' ? 'أحمد' : 'Ahmed');
    await page
      .getByLabel(k(language, 'reassign.reason'))
      .fill(language === 'ar' ? 'انتهت خدمة الموظف' : 'The employee left the organisation');
    await shot('tasks-reassign');
    await page.getByTestId('reassign-submit').click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'reassign.submit', { count: 2 }) })
      .click();
    await expect(
      page.getByText(k(language, 'reassign.done', { reassigned: 2, returned: 0 }), { exact: true }),
    ).toBeVisible();
    await expect(page.getByText(k(language, 'reassign.none'))).toBeVisible();
  });

  test('the creator role of a leaver’s tasks moves to a colleague, who then approves them (Phase 19, ADR 0169)', async ({
    page,
    language,
    shot,
    adminApi,
    baseURL,
    playwright,
  }) => {
    const tag = `cr${language}${Date.now().toString(36)}`;
    const { email, password } = await createUserWithPassword(
      page,
      adminApi,
      baseURL ?? 'http://localhost:5080',
      tag,
    );
    const creator = await playwright.request.newContext({ baseURL });
    expect((await apiPost(creator, '/api/v1/identity/auth/login', { login: email, password })).status()).toBe(
      200,
    );
    const me = (await (await creator.get('/api/v1/identity/me')).json()) as { employee: { id: string } };
    const leaver = me.employee.id;
    const assignee = await createEmployee(adminApi, tag, 'ريم', 'Reem', leaver);
    for (const [ar, en] of [
      ['مراجعة الميزانية', 'Review the budget'],
      ['تحديث خطة التدريب', 'Update the training plan'],
    ] as const) {
      const response = await apiPost(creator, '/api/v1/tasks', {
        titleAr: `${ar} ${tag}`,
        titleEn: `${en} ${tag}`,
        typeCode: 'manager-assignment',
        sourceCode: 'manager-assignment',
        dueDate: inDays(5),
        assigneeEmployeeIds: [assignee],
      });
      expect(response.status(), await response.text()).toBe(201);
    }
    await creator.dispose();

    await page.goto(`/settings/tasks/reassign?employee=${leaver}`);
    const section = page.getByTestId('creator-transfer');
    await expect(section.getByTestId('creator-transfer-preview').getByRole('row')).toHaveCount(3);
    await pickEmployee(page, section.locator('form'), language === 'ar' ? 'أحمد' : 'Ahmed');
    await section
      .getByTestId('creator-transfer-reason')
      .fill(language === 'ar' ? 'انتقل الموظف إلى جهة أخرى' : 'The employee moved to another organisation');
    await shot('tasks-take-over-as-creator');
    await page.getByTestId('creator-transfer-submit').click();
    await page
      .getByRole('dialog')
      .getByRole('button', { name: k(language, 'reassign.creatorSubmit', { count: 2 }) })
      .click();
    await expect(
      page.getByText(k(language, 'reassign.creatorDone', { transferred: 2, skipped: 0 }), { exact: true }),
    ).toBeVisible();
    await expect(page.getByText(k(language, 'reassign.createdNone'))).toBeVisible();
  });

  test('AI turns pasted meeting notes into draft tasks that the creator confirms', async ({
    page,
    language,
    shot,
  }) => {
    await page.goto('/tasks');
    await page.getByTestId('new-task').click();
    await page.getByRole('button', { name: k(language, 'newTask.fromText') }).click();
    const notes =
      language === 'ar'
        ? 'محضر اجتماع اللجنة:\n- إرسال المحضر إلى الأعضاء @أحمد قبل 2026-12-01\n- حجز القاعة للاجتماع القادم عاجل'
        : 'Committee meeting minutes:\n- Send the minutes to all members @Ahmed by 2026-12-01\n- Book the hall for the next meeting urgent';
    const fromText = page.getByTestId('from-text');
    await fromText.getByRole('textbox').first().fill(notes);
    await page.getByRole('button', { name: k(language, 'fromText.generate') }).click();
    await expect(page.getByTestId('task-draft')).toHaveCount(2);
    await shot('tasks-from-text');
  });
});

test('a sample item shows its tasks and creates one through the links registry', async ({
  page,
  language,
  shot,
}) => {
  const tag = `${language}${Date.now().toString(36)}`;
  const item = await createItemViaApi(page, {
    nameAr: `الأرشيف الرقمي ${tag}`,
    nameEn: `Digital archive ${tag}`,
  });
  await page.goto(`/sample/items/${item.id}?tab=tasks`);
  const panel = page.getByTestId('linked-tasks');
  await expect(panel.getByText(k(language, 'linked.empty'))).toBeVisible();
  await panel.getByRole('button', { name: k(language, 'linked.new') }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog.getByTestId('link-badge')).toContainText(
    language === 'ar' ? `الأرشيف الرقمي ${tag}` : `Digital archive ${tag}`,
  );
  await dialog.getByLabel(k(language, 'fields.titleAr')).fill(`مسح وثائق الأرشيف ${tag}`);
  await dialog.getByLabel(k(language, 'fields.titleEn')).fill(`Scan the archive documents ${tag}`);
  await pickEmployee(page, page.getByTestId('assignees'), language === 'ar' ? 'أحمد' : 'Ahmed');
  await dialog.getByTestId('create-task').click();
  await expect(dialog).toBeHidden();
  await expect(panel.getByTestId('task-row')).toHaveCount(1);
  await shot('sample-linked-tasks');
});
