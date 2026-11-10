import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type RecordReferences = components['schemas']['RecordReferencesDto'];
export type RecordReference = components['schemas']['RecordReferenceDto'];
export type SourceSystem = components['schemas']['SourceSystemDto'];
export type SourceSystemInput = components['schemas']['SourceSystemInput'];

/** Picklist of source-system types (Settings → Lists). */
export const SOURCE_TYPE_LOOKUP = 'integration.source_type';

export const integrationKeys = {
  sources: (includeInactive: boolean) => ['integration', 'source-systems', includeInactive] as const,
  references: (type: string, id: string) => ['integration', 'references', type, id] as const,
};

/**
 * The external references of a record (ADR 0164). The server answers 404 for a record the user may not read; the
 * components then show nothing.
 */
export function useRecordReferences(type: string, id: string | undefined) {
  return useQuery({
    queryKey: integrationKeys.references(type, id ?? ''),
    enabled: !!id,
    retry: false,
    staleTime: 60_000,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/references/{type}/{id}', {
          params: { path: { type, id: id ?? '' } },
          signal,
        }),
      ),
  });
}

export function useSourceSystems(includeInactive = false) {
  return useQuery({
    queryKey: integrationKeys.sources(includeInactive),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/integration/source-systems', {
          params: { query: { includeInactive } },
          signal,
        }),
      ),
  });
}
