import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type SearchResults = components['schemas']['SearchResultsDto'];
export type SearchItem = components['schemas']['SearchItemDto'];
export type RecentItem = components['schemas']['RecentItemDto'];
export type SearchType = components['schemas']['SearchTypeDto'];

export const searchKeys = {
  results: (q: string) => ['search', 'results', q] as const,
  recent: ['search', 'recent'] as const,
  types: ['search', 'types'] as const,
};

/** The server's minimum (normalised) length; shorter terms are not sent. */
export const minQueryLength = 2;

/** Global search (ADR 0160): grouped, permission-filtered results; only sent for terms of at least two characters. */
export function useSearch(q: string, limit = 5) {
  const term = q.trim();
  return useQuery({
    queryKey: searchKeys.results(term),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/search', { params: { query: { q: term, limit } }, signal })),
    enabled: term.length >= minQueryLength,
    staleTime: 30_000,
    placeholderData: (previous) => previous,
  });
}

export function useRecentItems(enabled: boolean) {
  return useQuery({
    queryKey: searchKeys.recent,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/search/recent', { signal })),
    enabled,
    staleTime: 15_000,
  });
}

/** Linkable types with their deep-link templates (record visits are recorded centrally). */
export function useSearchTypes() {
  return useQuery({
    queryKey: searchKeys.types,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/search/types', { signal })),
    staleTime: 10 * 60_000,
  });
}

/** Records a visit; a record the user may not see is refused by the server (404) and simply not recorded. */
export function useRecordVisit() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { type: string; id: string }) => {
      const { error, response } = await api.POST('/api/v1/search/recent', { body: input });
      if (error && response.status !== 404) throw new Error(`recent: ${response.status}`);
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: searchKeys.recent }),
  });
}
