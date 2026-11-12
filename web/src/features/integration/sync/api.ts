import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

type Schemas = components['schemas'];
export type SyncConnection = Schemas['SyncConnectionDto'];
export type SyncConnectionInput = Schemas['SyncConnectionInput'];
export type SyncStatus = Schemas['SyncStatusDto'];
export type SyncObjectStatus = Schemas['SyncObjectStatusDto'];
export type SyncRun = Schemas['SyncRunDto'];
export type SyncRunLog = Schemas['SyncRunLogDto'];
export type ObjectRunStats = Schemas['ObjectRunStats'];
export type SyncSchedule = Schemas['SyncScheduleSettings'];
export type SyncScheduleView = Schemas['SyncScheduleDto'];
export type SyncTarget = Schemas['SyncTargetDto'];
export type SyncTargetField = Schemas['SyncTargetFieldDto'];
export type MappingPack = Schemas['MappingPackDto'];
export type MappingPackDocument = Schemas['MappingPackDocument'];
export type ObjectMapping = Schemas['ObjectMappingDto'];
export type ObjectMappingInput = Schemas['ObjectMappingInput'];
export type FieldMapping = Schemas['FieldMapping'];
export type MappingValidation = Schemas['MappingValidationDto'];
export type MappingIssue = Schemas['MappingIssueDto'];
export type SourceDescribe = Schemas['SourceObjectDescribe'];
export type SourceField = Schemas['SourceFieldInfo'];
export type SyncException = Schemas['SyncExceptionDto'];
export type SyncExceptionDetail = Schemas['SyncExceptionDetailDto'];
export type SyncOutage = Schemas['SyncOutageDto'];
export type OutageReport = Schemas['OutageReportDto'];
export type OutageRecordReport = Schemas['OutageRecordReportDto'];
export type OutageChange = Schemas['OutageChangeDto'];
export type OutageConflict = Schemas['OutageConflictDto'];
export type RecordSyncState = Schemas['RecordSyncStateDto'];
export type SourceHistoryEntry = Schemas['SourceHistoryEntryDto'];

/** Permissions of Settings → Integrations → Salesforce sync (docs/design/salesforce-sync.md §12). */
export const SYNC_VIEW = 'integration.sync.view';
export const SYNC_MANAGE = 'integration.sync.manage';
export const SYNC_FAILOVER = 'integration.sync.failover';

/** Source system code of the connection this page manages (v1: Salesforce). */
export const SALESFORCE = 'salesforce';

export const MODES = ['disabled', 'standby', 'primary', 'reconciling'] as const;
export const SCHEDULE_KINDS = ['interval', 'daily', 'weekly'] as const;
export const FIELD_KINDS = ['direct', 'picklist', 'recordType', 'reference', 'person', 'constant'] as const;
export const TRANSFORMS = [
  'trim',
  'upper',
  'lower',
  'percentToRatio',
  'ratio',
  'dateOnly',
  'htmlToText',
  'truncate',
  'yesNo',
] as const;
export const EXCEPTION_REASONS = [
  'unmappedValue',
  'unknownUser',
  'missingParent',
  'invalidValue',
  'requiredMissing',
  'writeFailed',
  'fileRejected',
  'unknownTarget',
] as const;
export const EXCEPTION_STATUSES = ['open', 'resolved', 'ignored'] as const;

const root = ['integration', 'sync'] as const;
export const syncKeys = {
  all: root,
  connections: [...root, 'connections'] as const,
  status: (id: string) => [...root, 'status', id] as const,
  schedule: (id: string) => [...root, 'schedule', id] as const,
  runs: (id: string, page: number) => [...root, 'runs', id, page] as const,
  run: (id: string, runId: string) => [...root, 'run', id, runId] as const,
  targets: [...root, 'targets'] as const,
  packs: [...root, 'packs'] as const,
  mappings: (packId: string) => [...root, 'mappings', packId] as const,
  mapping: (mappingId: string) => [...root, 'mapping', mappingId] as const,
  describe: (id: string, name: string) => [...root, 'describe', id, name] as const,
  exceptions: (id: string, filters: ExceptionFilters, page: number) =>
    [...root, 'exceptions', id, filters, page] as const,
  exception: (exceptionId: string) => [...root, 'exception', exceptionId] as const,
  stepUp: [...root, 'step-up'] as const,
  outages: (id: string) => [...root, 'outages', id] as const,
  report: (outageId: string) => [...root, 'report', outageId] as const,
  conflicts: (outageId: string) => [...root, 'conflicts', outageId] as const,
  syncState: (type: string, id: string) => ['integration', 'sync-state', type, id] as const,
  sourceHistory: (type: string, id: string) => ['integration', 'source-history', type, id] as const,
};

export interface ExceptionFilters {
  status?: string | undefined;
  reason?: string | undefined;
  mapping?: string | undefined;
  search?: string | undefined;
}

/** The Salesforce connection (seeded on first start, ADR 0201); undefined when none exists. */
export function useSalesforceConnection() {
  return useQuery({
    queryKey: syncKeys.connections,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/sync/connections', { signal })),
    select: (connections) => connections.find((c) => c.sourceSystem === SALESFORCE) ?? connections[0],
  });
}

export function useSyncStatus(id: string) {
  return useQuery<SyncStatus>({
    queryKey: syncKeys.status(id),
    refetchInterval: (query) => (query.state.data?.running ? 5_000 : 60_000),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/status', {
          params: { path: { id } },
          signal,
        }),
      ),
  });
}

export function useSyncSchedule(id: string) {
  return useQuery({
    queryKey: syncKeys.schedule(id),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/schedule', {
          params: { path: { id } },
          signal,
        }),
      ),
  });
}

export function useSyncRuns(id: string, page: number) {
  return useQuery({
    queryKey: syncKeys.runs(id, page),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/runs', {
          params: { path: { id }, query: { page, pageSize: 25 } },
          signal,
        }),
      ),
  });
}

export function useSyncRun(id: string, runId: string | null) {
  return useQuery({
    queryKey: syncKeys.run(id, runId ?? ''),
    enabled: !!runId,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/runs/{runId}', {
          params: { path: { id, runId: runId ?? '' } },
          signal,
        }),
      ),
  });
}

export function useSyncTargets() {
  return useQuery({
    queryKey: syncKeys.targets,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/sync/targets', { signal })),
  });
}

export function useMappingPacks() {
  return useQuery({
    queryKey: syncKeys.packs,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/integration/sync/packs', { signal })),
  });
}

export function useObjectMappings(packId: string | null | undefined) {
  return useQuery({
    queryKey: syncKeys.mappings(packId ?? ''),
    enabled: !!packId,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/packs/{packId}/mappings', {
          params: { path: { packId: packId ?? '' } },
          signal,
        }),
      ),
  });
}

export function useObjectMapping(mappingId: string) {
  return useQuery({
    queryKey: syncKeys.mapping(mappingId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/mappings/{mappingId}', {
          params: { path: { mappingId } },
          signal,
        }),
      ),
  });
}

/** Live describe of a Salesforce object (fields, types, picklist values) for the mapping editor. */
export function useSourceDescribe(id: string, name: string | undefined) {
  return useQuery({
    queryKey: syncKeys.describe(id, name ?? ''),
    enabled: !!name,
    retry: false,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/source/objects/{name}', {
          params: { path: { id, name: name ?? '' } },
          signal,
        }),
      ),
  });
}

export function useSyncExceptions(id: string, filters: ExceptionFilters, page: number) {
  return useQuery({
    queryKey: syncKeys.exceptions(id, filters, page),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/exceptions', {
          params: { path: { id }, query: { ...filters, page, pageSize: 25 } },
          signal,
        }),
      ),
  });
}

export function useSyncException(exceptionId: string | null) {
  return useQuery({
    queryKey: syncKeys.exception(exceptionId ?? ''),
    enabled: !!exceptionId,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/exceptions/{exceptionId}', {
          params: { path: { exceptionId: exceptionId ?? '' } },
          signal,
        }),
      ),
  });
}

export function useStepUpMethods(enabled: boolean) {
  return useQuery({
    queryKey: syncKeys.stepUp,
    enabled,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/integration/sync/step-up/methods', { signal })),
  });
}

export function useOutages(id: string, enabled: boolean) {
  return useQuery({
    queryKey: syncKeys.outages(id),
    enabled,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/connections/{id}/outages', {
          params: { path: { id }, query: { page: 1, pageSize: 10 } },
          signal,
        }),
      ),
  });
}

export function useOutageReport(outageId: string | undefined) {
  return useQuery({
    queryKey: syncKeys.report(outageId ?? ''),
    enabled: !!outageId,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/outages/{outageId}/report', {
          params: { path: { outageId: outageId ?? '' } },
          signal,
        }),
      ),
  });
}

export function useOutageConflicts(outageId: string | undefined) {
  return useQuery({
    queryKey: syncKeys.conflicts(outageId ?? ''),
    enabled: !!outageId,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/sync/outages/{outageId}/conflicts', {
          params: { path: { outageId: outageId ?? '' } },
          signal,
        }),
      ),
  });
}

/** Standby badges of a record (ADR 0206): 404 for a record the user may not read → nothing is shown. */
export function useRecordSyncState(type: string, id: string | undefined) {
  return useQuery({
    queryKey: syncKeys.syncState(type, id ?? ''),
    enabled: !!id,
    retry: false,
    staleTime: 60_000,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/references/{type}/{id}/sync-state', {
          params: { path: { type, id: id ?? '' } },
          signal,
        }),
      ),
  });
}

export function useSourceHistory(type: string, id: string | undefined, enabled: boolean) {
  return useQuery({
    queryKey: syncKeys.sourceHistory(type, id ?? ''),
    enabled: !!id && enabled,
    retry: false,
    staleTime: 60_000,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/references/{type}/{id}/source-history', {
          params: { path: { type, id: id ?? '' } },
          signal,
        }),
      ),
  });
}

/** A mutation that refreshes every sync query when it succeeds. */
export function useSyncMutation<TInput, TResult>(mutationFn: (input: TInput) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: syncKeys.all }),
  });
}

/** Downloads the outage changes report (Excel or PDF) in the given language; exporting acknowledges the report. */
export async function downloadOutageReport(outageId: string, format: 'xlsx' | 'pdf', lang: string) {
  const options = { params: { path: { outageId }, query: { lang } }, parseAs: 'blob' as const };
  const { data, response } =
    format === 'xlsx'
      ? await api.GET('/api/v1/integration/sync/outages/{outageId}/report.xlsx', options)
      : await api.GET('/api/v1/integration/sync/outages/{outageId}/report.pdf', options);
  if (!response.ok || !(data instanceof Blob)) throw new Error(String(response.status));
  const url = URL.createObjectURL(data);
  const link = document.createElement('a');
  link.href = url;
  link.download = `outage-changes-${outageId}.${format}`;
  link.click();
  URL.revokeObjectURL(url);
}

/** Values of a Settings list, for the Map value fix of a lookup target field (any signed-in user may read them). */
export function useLookupCodes(type: string | null | undefined) {
  return useQuery({
    queryKey: ['settings', 'lookups', type ?? ''],
    enabled: !!type,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/settings/lookups/{type}/values', {
          params: { path: { type: type ?? '' } },
          signal,
        }),
      ),
  });
}
