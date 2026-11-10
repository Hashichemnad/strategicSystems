import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type ResponsibilityKind = components['schemas']['ResponsibilityKindDto'];
export type ResponsibilityKinds = components['schemas']['ResponsibilityKindsDto'];
export type ResponsibilityRow = components['schemas']['ResponsibilityRowDto'];
export type ResponsibilityOutcome = components['schemas']['ResponsibilityOutcomeDto'];
export type ResponsibilityResult = components['schemas']['ResponsibilityResultDto'];
export type ResponsibilityChange = components['schemas']['ResponsibilityChangeInput'];

export interface ResponsibilityFilters {
  personId?: string;
  unitId?: string;
  status?: string;
  text?: string;
  page: number;
  pageSize: number;
}

const keys = {
  all: ['access', 'responsibilities'] as const,
  kinds: (system: string) => [...keys.all, system] as const,
  list: (system: string, kind: string, filters: ResponsibilityFilters) =>
    [...keys.all, system, kind, filters] as const,
  candidates: (system: string, kind: string, search: string) =>
    [...keys.all, system, kind, 'candidates', search] as const,
};

/** The kinds of responsibility a system offers (403 when the user does not manage that system's responsibilities). */
export function useResponsibilityKinds(system: string | undefined) {
  return useQuery({
    queryKey: keys.kinds(system ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/systems/{code}/responsibilities', {
          params: { path: { code: system ?? '' } },
          signal,
        }),
      ),
    enabled: !!system,
    retry: false,
  });
}

/** One page of records of a kind, filtered on the server within the user's scope. */
export function useResponsibilities(
  system: string | undefined,
  kind: string | undefined,
  filters: ResponsibilityFilters,
) {
  return useQuery({
    queryKey: keys.list(system ?? '', kind ?? '', filters),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/systems/{code}/responsibilities/{kind}', {
          params: {
            path: { code: system ?? '', kind: kind ?? '' },
            query: {
              personId: filters.personId,
              unitId: filters.unitId,
              status: filters.status,
              text: filters.text,
              page: filters.page,
              pageSize: filters.pageSize,
            },
          },
          signal,
        }),
      ),
    enabled: !!system && !!kind,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** Eligible people of a restricted kind (e.g. Audit Office members), filtered by name. */
export function useResponsibilityCandidates(system: string, kind: string, search: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.candidates(system, kind, search),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/systems/{code}/responsibilities/{kind}/candidates', {
          params: { path: { code: system, kind }, query: { search: search || undefined } },
          signal,
        }),
      ),
    enabled,
  });
}

export function usePreviewResponsibilities(system: string, kind: string) {
  return useMutation({
    mutationFn: async (body: ResponsibilityChange) =>
      unwrap(
        await api.POST('/api/v1/access/systems/{code}/responsibilities/{kind}/preview', {
          params: { path: { code: system, kind } },
          body,
        }),
      ),
  });
}

export function useReassignResponsibilities(system: string, kind: string) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: async ({ body, idempotencyKey }: { body: ResponsibilityChange; idempotencyKey: string }) =>
      unwrap(
        await api.POST('/api/v1/access/systems/{code}/responsibilities/{kind}/reassign', {
          params: { path: { code: system, kind } },
          body,
          headers: { 'Idempotency-Key': idempotencyKey },
        }),
      ),
    onSuccess: () => client.invalidateQueries({ queryKey: keys.all }),
  });
}
