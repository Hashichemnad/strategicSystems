import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

export type AuditEntry = components['schemas']['AuditEntryDto'];
export type AuditPage = components['schemas']['PagedResultOfAuditEntryDto'];
export type RecordSnapshot = components['schemas']['RecordSnapshotDto'];
export type VerificationStatus = components['schemas']['VerificationStatusDto'];
export type VerificationRun = components['schemas']['VerificationRunDto'];
export type AuditSearch = NonNullable<operations['SearchAuditEntries']['parameters']['query']>;

/** Records that have a History tab. Each maps to its owner's audit endpoint, which checks access to the record first. */
export type AuditedRecord =
  | 'sample'
  | 'employee'
  | 'unit'
  | 'role'
  | 'task'
  | 'team'
  | 'request'
  | 'project'
  | 'kpiCard'
  | 'kpiResult'
  | 'risk'
  | 'serviceRequest'
  | 'document'
  | 'ipRecord'
  | 'capSource'
  | 'capRemark'
  | 'capAction'
  | 'benchStudy'
  | 'auditReport'
  | 'auditFinding';

interface HistoryQuery {
  page?: number;
  pageSize?: number;
  field?: string;
  userId?: string;
}

async function fetchHistory(record: AuditedRecord, id: string, query: HistoryQuery, signal: AbortSignal) {
  const params = { path: { id }, query };
  switch (record) {
    case 'sample':
      return unwrap(await api.GET('/api/v1/sample/items/{id}/audit', { params, signal }));
    case 'employee':
      return unwrap(await api.GET('/api/v1/org/employees/{id}/audit', { params, signal }));
    case 'unit':
      return unwrap(await api.GET('/api/v1/org/units/{id}/audit', { params, signal }));
    case 'role':
      return unwrap(await api.GET('/api/v1/access/roles/{id}/audit', { params, signal }));
    case 'task':
      return unwrap(await api.GET('/api/v1/tasks/{id}/audit', { params, signal }));
    case 'team':
      return unwrap(await api.GET('/api/v1/teams/{id}/audit', { params, signal }));
    case 'request':
      return unwrap(await api.GET('/api/v1/portfolio/requests/{id}/audit', { params, signal }));
    case 'project':
      return unwrap(await api.GET('/api/v1/portfolio/projects/{id}/audit', { params, signal }));
    case 'kpiCard':
      return unwrap(await api.GET('/api/v1/kpi/cards/{id}/audit', { params, signal }));
    case 'kpiResult':
      return unwrap(await api.GET('/api/v1/kpi/results/{id}/audit', { params, signal }));
    case 'risk':
      return unwrap(await api.GET('/api/v1/risk/risks/{id}/audit', { params, signal }));
    case 'serviceRequest':
      return unwrap(await api.GET('/api/v1/requests/{id}/audit', { params, signal }));
    case 'document':
      return unwrap(await api.GET('/api/v1/docreg/documents/{id}/audit', { params, signal }));
    case 'ipRecord':
      return unwrap(await api.GET('/api/v1/ip/records/{id}/audit', { params, signal }));
    case 'capSource':
      return unwrap(await api.GET('/api/v1/cap/sources/{id}/audit', { params, signal }));
    case 'capRemark':
      return unwrap(await api.GET('/api/v1/cap/remarks/{id}/audit', { params, signal }));
    case 'capAction':
      return unwrap(await api.GET('/api/v1/cap/actions/{id}/audit', { params, signal }));
    case 'benchStudy':
      return unwrap(await api.GET('/api/v1/bench/studies/{id}/audit', { params, signal }));
    case 'auditReport':
      return unwrap(await api.GET('/api/v1/auditmgmt/reports/{id}/audit', { params, signal }));
    case 'auditFinding':
      return unwrap(await api.GET('/api/v1/auditmgmt/findings/{id}/audit', { params, signal }));
  }
}

async function fetchAsOf(record: AuditedRecord, id: string, at: string, signal: AbortSignal) {
  const params = { path: { id }, query: { at } };
  switch (record) {
    case 'sample':
      return unwrap(await api.GET('/api/v1/sample/items/{id}/audit/as-of', { params, signal }));
    case 'employee':
      return unwrap(await api.GET('/api/v1/org/employees/{id}/audit/as-of', { params, signal }));
    case 'unit':
      return unwrap(await api.GET('/api/v1/org/units/{id}/audit/as-of', { params, signal }));
    case 'role':
      return unwrap(await api.GET('/api/v1/access/roles/{id}/audit/as-of', { params, signal }));
    case 'task':
      return unwrap(await api.GET('/api/v1/tasks/{id}/audit/as-of', { params, signal }));
    case 'team':
      return unwrap(await api.GET('/api/v1/teams/{id}/audit/as-of', { params, signal }));
    case 'request':
      return unwrap(await api.GET('/api/v1/portfolio/requests/{id}/audit/as-of', { params, signal }));
    case 'project':
      return unwrap(await api.GET('/api/v1/portfolio/projects/{id}/audit/as-of', { params, signal }));
    case 'kpiCard':
      return unwrap(await api.GET('/api/v1/kpi/cards/{id}/audit/as-of', { params, signal }));
    case 'kpiResult':
      return unwrap(await api.GET('/api/v1/kpi/results/{id}/audit/as-of', { params, signal }));
    case 'risk':
      return unwrap(await api.GET('/api/v1/risk/risks/{id}/audit/as-of', { params, signal }));
    case 'serviceRequest':
      return unwrap(await api.GET('/api/v1/requests/{id}/audit/as-of', { params, signal }));
    case 'document':
      return unwrap(await api.GET('/api/v1/docreg/documents/{id}/audit/as-of', { params, signal }));
    case 'ipRecord':
      return unwrap(await api.GET('/api/v1/ip/records/{id}/audit/as-of', { params, signal }));
    case 'capSource':
      return unwrap(await api.GET('/api/v1/cap/sources/{id}/audit/as-of', { params, signal }));
    case 'capRemark':
      return unwrap(await api.GET('/api/v1/cap/remarks/{id}/audit/as-of', { params, signal }));
    case 'capAction':
      return unwrap(await api.GET('/api/v1/cap/actions/{id}/audit/as-of', { params, signal }));
    case 'benchStudy':
      return unwrap(await api.GET('/api/v1/bench/studies/{id}/audit/as-of', { params, signal }));
    case 'auditReport':
      return unwrap(await api.GET('/api/v1/auditmgmt/reports/{id}/audit/as-of', { params, signal }));
    case 'auditFinding':
      return unwrap(await api.GET('/api/v1/auditmgmt/findings/{id}/audit/as-of', { params, signal }));
  }
}

export const auditKeys = {
  all: ['audit'] as const,
  history: (record: AuditedRecord, id: string, query: HistoryQuery) =>
    ['audit', 'history', record, id, query] as const,
  asOf: (record: AuditedRecord, id: string, at: string) => ['audit', 'asOf', record, id, at] as const,
  search: (query: AuditSearch) => ['audit', 'search', query] as const,
  verification: ['audit', 'verification'] as const,
  entityTypes: ['audit', 'entityTypes'] as const,
};

export function useRecordHistory(record: AuditedRecord, id: string, query: HistoryQuery) {
  return useQuery({
    queryKey: auditKeys.history(record, id, query),
    queryFn: ({ signal }) => fetchHistory(record, id, query, signal),
    placeholderData: keepPreviousData,
  });
}

export function useRecordAsOf(record: AuditedRecord, id: string, at: string | undefined) {
  return useQuery({
    queryKey: auditKeys.asOf(record, id, at ?? ''),
    queryFn: ({ signal }) => fetchAsOf(record, id, at ?? '', signal),
    enabled: Boolean(at),
  });
}

export function useAuditSearch(query: AuditSearch) {
  return useQuery({
    queryKey: auditKeys.search(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/audit/entries', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useAuditEntityTypes() {
  return useQuery({
    queryKey: auditKeys.entityTypes,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/audit/entity-types', { signal })),
    staleTime: 60_000,
  });
}

export function useVerification() {
  return useQuery({
    queryKey: auditKeys.verification,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/audit/verification', { signal })),
  });
}

export function useVerifyChain() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (mode: 'full' | 'incremental') =>
      unwrap(await api.POST('/api/v1/audit/verification', { body: { mode } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: auditKeys.verification }),
  });
}

/** Query string for the export link (a plain download, so the browser saves the file with the server's name). */
export function exportUrl(query: AuditSearch, format: 'xlsx' | 'pdf', language: string): string {
  const params = new URLSearchParams({ format, lang: language });
  for (const [key, value] of Object.entries(query) as [string, string | number | undefined][]) {
    const text = value === undefined ? '' : String(value);
    if (text !== '' && key !== 'page' && key !== 'pageSize') params.set(key, text);
  }
  return `/api/v1/audit/entries/export?${params.toString()}`;
}
