import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { RecordReference, RecordReferences } from './api';
import { ExternalReferencesSection, OriginBadge } from './ExternalReferences';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };

function reference(overrides: Partial<RecordReference>): RecordReference {
  return {
    sourceSystem: 'salesforce',
    sourceNameAr: 'سيلزفورس',
    sourceNameEn: 'Salesforce',
    sourceTypeCode: 'salesforce',
    sourceActive: true,
    externalId: 'a0B5g00000XyZ1',
    isOrigin: true,
    firstSeenAt: '2026-11-01T09:30:00Z',
    lastSyncedAt: '2026-11-02T10:00:00Z',
    sourceModifiedAt: null,
    syncStatus: 'synced',
    ...overrides,
  };
}

function stub(references: RecordReference[], status = 200) {
  const data: RecordReferences = {
    entityType: 'risk.risk',
    recordId: 'r-1',
    references,
    origin: references.find((r) => r.isOrigin) ?? null,
  };
  return vi.spyOn(api as unknown as LooseGetApi, 'GET').mockResolvedValue(
    status === 200
      ? { data, error: undefined, response: new Response(null, { status }) }
      : {
          data: undefined,
          error: { code: 'record.notFound', messageKey: 'errors.notFound', status },
          response: new Response(null, { status }),
        },
  );
}

function renderBoth(language: 'ar' | 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <OriginBadge type="risk.risk" id="r-1" />
      <ExternalReferencesSection type="risk.risk" id="r-1" />
    </QueryClientProvider>,
    { language },
  );
}

describe('External references', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the origin badge and every reference with its sync status (English)', async () => {
    const get = stub([
      reference({}),
      reference({
        sourceSystem: 'sap',
        sourceNameAr: 'ساب',
        sourceNameEn: 'SAP',
        externalId: 'R-77',
        isOrigin: false,
        syncStatus: 'error',
      }),
    ]);
    await renderBoth('en');
    expect(await screen.findByText('from Salesforce')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'External references' })).toBeInTheDocument();
    expect(screen.getByText('a0B5g00000XyZ1')).toHaveAttribute('dir', 'ltr');
    expect(screen.getByText('R-77')).toBeInTheDocument();
    expect(screen.getByText('Sync error')).toBeInTheDocument();
    expect(get).toHaveBeenCalledWith(
      '/api/v1/integration/references/{type}/{id}',
      expect.objectContaining({ params: { path: { type: 'risk.risk', id: 'r-1' } } }),
    );
  });

  it('is in Arabic with the Arabic source name', async () => {
    stub([reference({})]);
    await renderBoth('ar');
    expect(await screen.findByText('من سيلزفورس')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'المراجع الخارجية' })).toBeInTheDocument();
    expect(screen.getByText('متزامن')).toBeInTheDocument();
  });

  it('renders nothing without references, or when the record is not visible (404)', async () => {
    stub([]);
    await renderBoth('en');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByTestId('external-references')).toBeNull();
    expect(screen.queryByTestId('origin-badge')).toBeNull();

    vi.restoreAllMocks();
    stub([], 404);
    const hidden = await renderBoth('en');
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByTestId('external-references')).toBeNull();
    expect(hidden.container.querySelector('[data-testid="origin-badge"]')).toBeNull();
  });

  it('shows no origin badge for a reference that did not create the record', async () => {
    stub([reference({ isOrigin: false })]);
    await renderBoth('en');
    expect(await screen.findByTestId('external-references')).toBeInTheDocument();
    expect(screen.queryByTestId('origin-badge')).toBeNull();
  });
});
