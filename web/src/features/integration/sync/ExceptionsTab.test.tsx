import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { SyncExceptionDetail, SyncTarget } from './api';
import { ExceptionDialog } from './ExceptionsTab';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseApi = {
  GET: (path: string, ...args: unknown[]) => Promise<unknown>;
  POST: (path: string, ...args: unknown[]) => Promise<unknown>;
};

const ok = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) });

const detail: SyncExceptionDetail = {
  exception: {
    id: 'x-1',
    mappingId: 'm-1',
    mappingKey: 'project',
    sourceObject: 'PMS_Project__c',
    externalId: 'a0B000000000001',
    reason: 'unmappedValue',
    field: 'stage',
    value: 'On Hold',
    messageKey: 'integration.sync.exceptions.unmappedValue',
    status: 'open',
    attempts: 1,
    createdAt: '2026-11-10T08:00:00Z',
    updatedAt: '2026-11-10T08:00:00Z',
    resolvedAt: null,
  },
  payload: { Id: 'a0B000000000001', Name: 'Digital transformation', Project_Stage__c: 'On Hold' },
  targetType: 'portfolio.project',
};

const targets: SyncTarget[] = [
  {
    code: 'portfolio.project',
    systemCode: 'portfolio',
    nameAr: 'مشروع',
    nameEn: 'Project',
    fields: [
      { code: 'stage', kind: 'text', required: false, lookupType: null, referenceType: null, choices: null },
    ],
  },
];

async function renderDialog(language: 'ar' | 'en' = 'en') {
  vi.spyOn(api as unknown as LooseApi, 'GET').mockImplementation((path: string) =>
    Promise.resolve(ok(path === '/api/v1/integration/sync/targets' ? targets : detail)),
  );
  const post = vi
    .spyOn(api as unknown as LooseApi, 'POST')
    .mockResolvedValue(ok({ ...detail.exception, status: 'resolved' }));
  const onClose = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <ExceptionDialog exceptionId="x-1" canManage onClose={onClose} />
    </QueryClientProvider>,
    { language },
  );
  return { post, onClose };
}

describe('Sync exception: Map value', () => {
  afterEach(() => vi.restoreAllMocks());

  it('maps the unmapped value to a code and re-processes the record', async () => {
    const { post, onClose } = await renderDialog();
    expect(await screen.findByTestId('exception-detail')).toBeInTheDocument();
    expect(screen.getByRole('dialog', { name: 'Unmapped value' })).toHaveAccessibleDescription(
      'The value “On Hold” of stage has no code in the value map.',
    );
    expect(screen.getByText('Digital transformation')).toBeInTheDocument();
    const map = screen.getByRole('button', { name: 'Map and re-process' });
    expect(map).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox', { name: 'Code' }), 'on_hold');
    await userEvent.click(map);
    expect(post).toHaveBeenCalledWith(
      '/api/v1/integration/sync/exceptions/{exceptionId}/map-value',
      expect.objectContaining({
        params: { path: { exceptionId: 'x-1' } },
        body: { value: 'On Hold', code: 'on_hold' },
      }),
    );
    expect(onClose).toHaveBeenCalled();
    expect(await screen.findByText('The exception is resolved')).toBeInTheDocument();
  });

  it('is in Arabic', async () => {
    await renderDialog('ar');
    expect(await screen.findByRole('dialog', { name: 'قيمة غير مطابَقة' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'مطابقة وإعادة معالجة' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'تجاهل' })).toBeEnabled();
  });
});
