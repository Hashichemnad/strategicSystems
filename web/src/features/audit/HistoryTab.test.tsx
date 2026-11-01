import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { AuditEntry, AuditPage } from './api';
import { HistoryTab } from './HistoryTab';

function entry(overrides: Partial<AuditEntry>): AuditEntry {
  return {
    id: crypto.randomUUID(),
    seq: 1,
    occurredAt: '2026-11-01T09:30:00Z',
    kind: 'change',
    action: 'update',
    systemCode: 'sample',
    entityType: 'sample.item',
    recordId: 'item-1',
    parentType: null,
    parentId: null,
    field: null,
    oldValue: null,
    newValue: null,
    changeSetId: 'set-1',
    userId: 'user-1',
    userName: 'mariam',
    onBehalfOfId: null,
    onBehalfOfName: null,
    channel: 'ui',
    correlationId: null,
    ipAddress: null,
    userAgent: null,
    reason: null,
    success: null,
    data: null,
    ...overrides,
  };
}

function stubHistory(items: AuditEntry[]) {
  const page: AuditPage = { items, page: 1, pageSize: 50, totalCount: items.length, totalPages: 1 };
  vi.spyOn(api, 'GET').mockResolvedValue({
    data: page,
    error: undefined,
    response: new Response(null, { status: 200 }),
  } as never);
}

async function renderTab(language: 'ar' | 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <HistoryTab
        record="sample"
        id="item-1"
        fieldLabels={{ NameEn: language === 'ar' ? 'الاسم (إنجليزي)' : 'Name (English)' }}
      />
    </QueryClientProvider>,
    { language },
  );
}

describe('HistoryTab', () => {
  afterEach(() => vi.restoreAllMocks());

  it('groups the fields of one save and shows old → new with user and channel', async () => {
    stubHistory([
      entry({ seq: 3, field: 'NameEn', oldValue: 'Old plan', newValue: 'New plan' }),
      entry({ seq: 2, field: 'Status', oldValue: 'active', newValue: 'inactive' }),
      entry({
        seq: 1,
        action: 'create',
        changeSetId: 'set-0',
        newValue: '{"NameEn":"Old plan","Status":"active"}',
        channel: 'api',
      }),
    ]);
    await renderTab('en');

    const timeline = await screen.findByRole('list', { name: 'Change history' });
    const [first, second, ...rest] = within(timeline)
      .getAllByRole('listitem')
      .filter((li) => li.parentElement === timeline);
    if (!first || !second) throw new Error('two groups expected');
    expect(rest).toHaveLength(0);
    expect(within(first).getByText('Name (English)')).toBeInTheDocument();
    expect(within(first).getByText('Old plan').tagName).toBe('DEL');
    expect(within(first).getByText('New plan').tagName).toBe('INS');
    expect(within(first).getByText('Status')).toBeInTheDocument();
    expect(within(first).getByText('Web')).toBeInTheDocument();
    expect(within(second).getByText('Created')).toBeInTheDocument();
    expect(within(second).getByText('API')).toBeInTheDocument();
  });

  it('reads in Arabic with translated labels', async () => {
    stubHistory([entry({ field: 'NameEn', oldValue: 'أ', newValue: 'ب' })]);
    await renderTab('ar');

    expect(await screen.findByRole('list', { name: 'تاريخ التغييرات' })).toBeInTheDocument();
    expect(screen.getByText('تعديل')).toBeInTheDocument();
    expect(screen.getByText('الاسم (إنجليزي)')).toBeInTheDocument();
    expect(screen.getByText('الويب')).toBeInTheDocument();
  });

  it('shows an empty state when nothing was recorded', async () => {
    stubHistory([]);
    await renderTab('en');
    expect(await screen.findByText('No changes recorded yet.')).toBeInTheDocument();
  });
});
