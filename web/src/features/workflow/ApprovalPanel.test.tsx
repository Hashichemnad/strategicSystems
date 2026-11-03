import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { Instance, SubjectApprovals } from './api';
import { ApprovalPanel } from './ApprovalPanel';

const director = { userId: 'u-director', nameAr: 'مريم الحمادي', nameEn: 'Mariam Al Hammadi' };
const submitter = { userId: 'u-ahmed', nameAr: 'أحمد السويدي', nameEn: 'Ahmed Al Suwaidi' };

function instance(overrides: Partial<Instance>): Instance {
  return {
    id: 'i-2',
    processKey: 'sample.item-approval',
    processNameAr: 'اعتماد البند التجريبي',
    processNameEn: 'Sample item approval',
    status: 'in_progress',
    attempt: 2,
    submittedAt: '2026-11-02T08:00:00Z',
    completedAt: null,
    submitter,
    comment: null,
    steps: [
      {
        sort: 1,
        code: 'section-manager',
        nameAr: 'رئيس القسم',
        nameEn: 'Section manager',
        state: 'skipped',
        approvers: [],
        outcome: null,
        actedBy: null,
        onBehalfOf: null,
        comment: null,
        completedAt: null,
        dueAt: null,
        overdue: false,
        slaMode: null,
        businessHoursNameAr: null,
        businessHoursNameEn: null,
      },
      {
        sort: 2,
        code: 'department-director',
        nameAr: 'مدير الإدارة',
        nameEn: 'Department director',
        state: 'current',
        approvers: [director],
        outcome: null,
        actedBy: null,
        onBehalfOf: null,
        comment: null,
        completedAt: null,
        dueAt: '2026-11-03T08:00:00Z',
        overdue: true,
        slaMode: 'working',
        businessHoursNameAr: 'ساعات العمل الرسمية',
        businessHoursNameEn: 'Standard business hours',
      },
    ],
    history: [
      {
        id: 'h-2',
        occurredAt: '2026-11-02T08:00:01Z',
        event: 'step-skipped',
        stepNameAr: 'رئيس القسم',
        stepNameEn: 'Section manager',
        actor: null,
        onBehalfOf: null,
        comment: null,
        data: { reason: 'condition' },
      },
      {
        id: 'h-1',
        occurredAt: '2026-11-02T08:00:00Z',
        event: 'resubmitted',
        stepNameAr: null,
        stepNameEn: null,
        actor: submitter,
        onBehalfOf: null,
        comment: 'corrected',
        data: null,
      },
    ],
    myTask: {
      taskId: 't-1',
      version: 7,
      actions: [
        { code: 'approve', outcome: 'approve', labelAr: null, labelEn: null, commentRequired: false },
        { code: 'reject', outcome: 'reject', labelAr: null, labelEn: null, commentRequired: true },
        { code: 'return', outcome: 'return', labelAr: null, labelEn: null, commentRequired: true },
        {
          code: 'approve-conditions',
          outcome: 'approve',
          labelAr: 'موافقة مشروطة',
          labelEn: 'Approve with conditions',
          commentRequired: false,
        },
      ],
      onBehalfOf: [director],
      selfAssigned: false,
    },
    ...overrides,
  };
}

function stub(data: SubjectApprovals) {
  vi.spyOn(api, 'GET').mockResolvedValue({
    data,
    error: undefined,
    response: new Response(null, { status: 200 }),
  } as never);
  return vi.spyOn(api, 'POST').mockResolvedValue({
    data: {
      instanceId: 'i-2',
      instanceStatus: 'returned',
      taskId: 't-1',
      outcome: 'return',
      nextStepNameAr: null,
      nextStepNameEn: null,
    },
    error: undefined,
    response: new Response(null, { status: 200 }),
  });
}

async function renderPanel(language: 'ar' | 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <ApprovalPanel resourceType="sample.item" resourceId="item-1" />
    </QueryClientProvider>,
    { language },
  );
}

describe('ApprovalPanel', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the steps, the overdue step, the history and the decisions of a delegate in English', async () => {
    stub({
      resourceType: 'sample.item',
      resourceId: 'item-1',
      instances: [instance({}), instance({ id: 'i-1', attempt: 1, status: 'returned', myTask: null })],
    });
    await renderPanel('en');

    const [steps] = await screen.findAllByRole('list', { name: 'Approvals' });
    const current = within(steps as HTMLElement)
      .getAllByRole('listitem')
      .find((item) => item.textContent.includes('Department director'));
    if (!current) throw new Error('the current step is not listed');
    expect(current).toHaveAttribute('aria-current', 'step');
    expect(within(current).getByText('Overdue')).toBeInTheDocument();
    expect(within(current).getByText('Mariam Al Hammadi')).toBeInTheDocument();
    expect(within(current).getByText(/Standard business hours/u)).toBeInTheDocument();
    expect(screen.getAllByText('Section manager skipped')[0]).toBeInTheDocument();
    expect(screen.getAllByText(/its condition was false/u)[0]).toBeInTheDocument();
    expect(screen.getAllByText('Resubmitted after correction')[0]).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Approve with conditions' })).toBeInTheDocument();
    expect(screen.getByText('Earlier submissions')).toBeInTheDocument();
  });

  it('requires a comment to return, then sends the decision on behalf of the delegator with an idempotency key (Arabic)', async () => {
    const post = stub({ resourceType: 'sample.item', resourceId: 'item-1', instances: [instance({})] });
    await renderPanel('ar');
    const user = userEvent.setup();

    await user.click(await screen.findByRole('button', { name: 'إعادة للتصحيح' }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText('نيابة عن مريم الحمادي')).toBeInTheDocument();
    await user.click(within(dialog).getByRole('button', { name: 'تأكيد' }));
    expect(await within(dialog).findByText('أضف تعليقاً لهذا القرار.')).toBeInTheDocument();
    expect(post).not.toHaveBeenCalled();

    await user.type(within(dialog).getByRole('textbox'), 'يرجى إضافة التفاصيل');
    await user.click(within(dialog).getByRole('button', { name: 'تأكيد' }));
    expect(post).toHaveBeenCalledTimes(1);
    const [path, options] = post.mock.calls[0] as unknown as [
      string,
      { headers: Record<string, string>; body: Record<string, unknown> },
    ];
    expect(path).toBe('/api/v1/workflow/tasks/{taskId}/act');
    expect(options.headers['Idempotency-Key']).toMatch(/^[0-9a-f-]{36}$/u);
    expect(options.body).toMatchObject({
      action: 'return',
      comment: 'يرجى إضافة التفاصيل',
      onBehalfOfUserId: 'u-director',
    });
  });

  it('says so when the record was never submitted', async () => {
    stub({ resourceType: 'sample.item', resourceId: 'item-1', instances: [] });
    await renderPanel('en');
    expect(
      await screen.findByText('This record has not been submitted for approval yet.'),
    ).toBeInTheDocument();
  });
});
