import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { RecordReferences } from '../api';
import { ExternalReferencesSection, OriginBadge } from '../ExternalReferences';
import type { RecordSyncState, SourceHistoryEntry } from './api';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (path: string, ...args: unknown[]) => Promise<unknown> };

const references: RecordReferences = {
  entityType: 'portfolio.project',
  recordId: 'p-1',
  references: [
    {
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
    },
  ],
  origin: null,
};
references.origin = references.references[0] ?? null;

const history: SourceHistoryEntry[] = [
  {
    id: 'h-1',
    kind: 'approval',
    occurredAt: '2026-10-01T08:00:00Z',
    actorName: 'Fatima Al Mansoori',
    processName: 'Project charter approval',
    processInstanceId: 'pi-1',
    processStatus: 'Approved',
    stepStatus: 'Approved',
    comments: 'Looks good',
    field: null,
    oldValue: null,
    newValue: null,
  },
  {
    id: 'h-2',
    kind: 'field',
    occurredAt: '2026-10-02T08:00:00Z',
    actorName: 'Omar Haddad',
    processName: null,
    processInstanceId: null,
    processStatus: null,
    stepStatus: null,
    comments: null,
    field: 'Project_Stage__c',
    oldValue: 'Initiation',
    newValue: 'Planning',
  },
];

function state(overrides: Partial<RecordSyncState>): RecordSyncState {
  return {
    readOnly: true,
    sourceSystem: 'salesforce',
    sourceNameAr: 'سيلزفورس',
    sourceNameEn: 'Salesforce',
    deletedInSource: false,
    resubmitRequired: false,
    sourceHistoryCount: 2,
    ...overrides,
  };
}

function stub(syncState: RecordSyncState) {
  const ok = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) });
  return vi.spyOn(api as unknown as LooseGetApi, 'GET').mockImplementation((path: string) => {
    if (path.endsWith('/sync-state')) return Promise.resolve(ok(syncState));
    if (path.endsWith('/source-history')) return Promise.resolve(ok(history));
    return Promise.resolve(ok(references));
  });
}

async function renderRecord(language: 'ar' | 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <OriginBadge type="portfolio.project" id="p-1" />
      <ExternalReferencesSection type="portfolio.project" id="p-1" />
    </QueryClientProvider>,
    { language },
  );
}

describe('Record sync state (standby badges and source history)', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the read-only badge and banner, and the source history panel (English)', async () => {
    const get = stub(state({}));
    await renderRecord('en');
    expect(await screen.findByTestId('sync-read-only')).toHaveTextContent(
      'Synced from Salesforce — read-only',
    );
    expect(await screen.findByTestId('sync-read-only-banner')).toHaveTextContent(
      'Salesforce is the primary system: change this record in Salesforce',
    );
    const panel = await screen.findByTestId('source-history');
    expect(within(panel).getByRole('heading', { name: 'History from Salesforce' })).toBeInTheDocument();
    const approvals = within(panel).getByTestId('source-approvals');
    expect(within(approvals).getByText('Project charter approval')).toBeInTheDocument();
    expect(within(approvals).getAllByText('Approved').length).toBeGreaterThan(0);
    expect(within(approvals).getByText('Looks good')).toBeInTheDocument();
    const fields = within(panel).getByTestId('source-field-changes');
    expect(within(fields).getByText('Project_Stage__c')).toHaveAttribute('dir', 'ltr');
    expect(within(fields).getByText('Initiation → Planning')).toBeInTheDocument();
    expect(get).toHaveBeenCalledWith(
      '/api/v1/integration/references/{type}/{id}/source-history',
      expect.objectContaining({ params: { path: { type: 'portfolio.project', id: 'p-1' } } }),
    );
  });

  it('shows "Deleted in Salesforce" and "Resubmit required" in Arabic', async () => {
    stub(state({ readOnly: false, deletedInSource: true, resubmitRequired: true, sourceHistoryCount: 0 }));
    await renderRecord('ar');
    expect(await screen.findByText('محذوف في سيلزفورس')).toBeInTheDocument();
    expect((await screen.findAllByText('يتطلب إعادة التقديم')).length).toBeGreaterThan(0);
    expect(screen.queryByTestId('sync-read-only')).toBeNull();
    expect(screen.getByText('من سيلزفورس')).toBeInTheDocument();
  });

  it('does not load the source history when the record has none', async () => {
    const get = stub(state({ readOnly: false, sourceHistoryCount: 0 }));
    await renderRecord('en');
    expect(await screen.findByTestId('external-references')).toBeInTheDocument();
    await new Promise((resolve) => setTimeout(resolve, 20));
    expect(screen.queryByTestId('source-history')).toBeNull();
    expect(get).not.toHaveBeenCalledWith(
      '/api/v1/integration/references/{type}/{id}/source-history',
      expect.anything(),
    );
  });
});
