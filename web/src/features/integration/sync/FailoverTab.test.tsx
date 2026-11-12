import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { OutageRecordReport, OutageReport } from './api';
import { MakePrimaryDialog, OutageReportView } from './FailoverTab';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseApi = {
  GET: (path: string, ...args: unknown[]) => Promise<unknown>;
  POST: (path: string, ...args: unknown[]) => Promise<unknown>;
};

const ok = (data: unknown, status = 200) => ({
  data,
  error: undefined,
  response: new Response(null, { status }),
});

function render(ui: React.ReactElement, language: 'ar' | 'en' = 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(<QueryClientProvider client={client}>{ui}</QueryClientProvider>, { language });
}

describe('Make this system primary', () => {
  afterEach(() => vi.restoreAllMocks());

  it('requires a reason and a code, then sends the method and code', async () => {
    vi.spyOn(api as unknown as LooseApi, 'GET').mockResolvedValue(ok({ methods: ['totp', 'email'] }));
    const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue(ok({}));
    const onClose = vi.fn();
    await render(<MakePrimaryDialog connectionId="c-1" onClose={onClose} />);
    const confirm = await screen.findByRole('button', { name: 'Make primary' });
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox', { name: /reason/i }), 'Salesforce is down');
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox', { name: /authenticator app/i }), '123456');
    expect(confirm).toBeEnabled();
    await userEvent.click(confirm);
    expect(post).toHaveBeenCalledWith(
      '/api/v1/integration/sync/connections/{id}/make-primary',
      expect.objectContaining({
        params: { path: { id: 'c-1' } },
        body: { reason: 'Salesforce is down', method: 'totp', code: '123456' },
      }),
    );
    expect(onClose).toHaveBeenCalled();
  });

  it('sends an e-mail code when the user verifies by e-mail (Arabic)', async () => {
    vi.spyOn(api as unknown as LooseApi, 'GET').mockResolvedValue(ok({ methods: ['email'] }));
    const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue(ok(undefined, 204));
    await render(<MakePrimaryDialog connectionId="c-1" onClose={() => undefined} />, 'ar');
    expect(await screen.findByRole('dialog', { name: 'جعل هذا النظام أساسياً' })).toBeInTheDocument();
    await userEvent.click(await screen.findByRole('button', { name: 'إرسال الرمز بالبريد' }));
    expect(post).toHaveBeenCalledWith('/api/v1/integration/sync/step-up/email-code');
    expect(await screen.findByText('أُرسل رمز إلى بريدك الإلكتروني.')).toBeInTheDocument();
  });
});

function record(overrides: Partial<OutageRecordReport>): OutageRecordReport {
  return {
    entityType: 'portfolio.project',
    recordId: 'p-1',
    typeNameAr: 'مشروع',
    typeNameEn: 'Project',
    number: 'PROJ-001',
    titleAr: 'التحول الرقمي',
    titleEn: 'Digital transformation',
    link: null,
    salesforceId: 'a0B000000000001',
    resubmitRequired: false,
    changedLocally: true,
    reEntered: false,
    conflict: false,
    resolution: null,
    changes: [
      {
        at: '2026-11-10T09:00:00Z',
        action: 'update',
        entityType: 'portfolio.project',
        field: 'Stage',
        oldValue: 'planning',
        newValue: 'execution',
        userName: 'Mariam',
        channel: 'ui',
        kind: 'change',
      },
    ],
    ...overrides,
  };
}

const report: OutageReport = {
  outage: {
    id: 'o-1',
    status: 'open',
    startedAt: '2026-11-10T08:00:00Z',
    startedByName: 'Mariam',
    reason: 'Salesforce down',
    returnedAt: null,
    reportAcknowledgedAt: null,
    closedAt: null,
    resubmitRequired: 1,
    changedLocally: 2,
    reEntered: 0,
    pendingConflicts: 0,
  },
  from: '2026-11-10T08:00:00Z',
  to: '2026-11-11T08:00:00Z',
  changes: 2,
  truncated: false,
  systems: [
    { systemCode: 'portfolio', records: [record({})] },
    {
      systemCode: 'risk',
      records: [
        record({
          entityType: 'risk.risk',
          recordId: 'r-1',
          typeNameEn: 'Risk',
          number: 'RSK-7',
          titleEn: 'Vendor delay',
          salesforceId: null,
          resubmitRequired: true,
        }),
      ],
    },
  ],
};

describe('Outage changes report', () => {
  afterEach(() => vi.restoreAllMocks());

  it('groups the changes by system and record and toggles "re-entered"', async () => {
    vi.spyOn(api as unknown as LooseApi, 'GET').mockResolvedValue(ok(report));
    const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue(ok(undefined, 204));
    await render(<OutageReportView outageId="o-1" acknowledgedAt={null} />);
    const portfolio = await screen.findByTestId('report-system-portfolio');
    expect(within(portfolio).getByRole('heading', { name: /Portfolio/ })).toBeInTheDocument();
    const project = within(portfolio).getByTestId('report-record-p-1');
    expect(within(project).getByText('PROJ-001 — Digital transformation')).toBeInTheDocument();
    expect(within(project).getByText('a0B000000000001')).toHaveAttribute('dir', 'ltr');
    expect(within(project).getByText('planning → execution')).toBeInTheDocument();
    expect(within(project).getByText('Updated')).toBeInTheDocument();

    const risk = within(screen.getByTestId('report-system-risk')).getByTestId('report-record-r-1');
    expect(within(risk).getByText('Created here')).toBeInTheDocument();
    expect(within(risk).getByText('Resubmit required')).toBeInTheDocument();

    await userEvent.click(within(project).getByRole('checkbox', { name: 'Re-entered' }));
    expect(post).toHaveBeenCalledWith(
      '/api/v1/integration/sync/outages/{outageId}/records/{type}/{recordId}/re-entered',
      expect.objectContaining({
        params: { path: { outageId: 'o-1', type: 'portfolio.project', recordId: 'p-1' } },
        body: { reEntered: true },
      }),
    );
  });

  it('is in Arabic, with an Acknowledge action while not acknowledged', async () => {
    vi.spyOn(api as unknown as LooseApi, 'GET').mockResolvedValue(ok(report));
    const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue(ok(undefined, 204));
    await render(<OutageReportView outageId="o-1" acknowledgedAt={null} />, 'ar');
    expect(await screen.findByRole('heading', { name: /إدارة المحافظ/ })).toBeInTheDocument();
    expect(screen.getByText('PROJ-001 — التحول الرقمي')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'إقرار' }));
    expect(post).toHaveBeenCalledWith(
      '/api/v1/integration/sync/outages/{outageId}/acknowledge',
      expect.objectContaining({ params: { path: { outageId: 'o-1' } } }),
    );
  });
});
