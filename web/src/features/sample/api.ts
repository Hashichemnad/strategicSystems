import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

export type SampleItem = components['schemas']['SampleItemDto'];
export type SampleItemInput = components['schemas']['SampleItemInput'];
export type SampleListQuery = NonNullable<operations['ListSampleItems']['parameters']['query']>;

export const sampleKeys = {
  all: ['sample', 'items'] as const,
  list: (query: SampleListQuery) => [...sampleKeys.all, 'list', query] as const,
};

export function useSampleItems(query: SampleListQuery) {
  return useQuery({
    queryKey: sampleKeys.list(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/sample/items', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

function useInvalidateOnSuccess() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: sampleKeys.all });
}

export function useCreateSampleItem() {
  const onSuccess = useInvalidateOnSuccess();
  return useMutation({
    mutationFn: async (body: SampleItemInput) => unwrap(await api.POST('/api/v1/sample/items', { body })),
    onSuccess,
  });
}

export function useUpdateSampleItem() {
  const onSuccess = useInvalidateOnSuccess();
  return useMutation({
    mutationFn: async ({ id, ...body }: SampleItemInput & { id: string; version: number }) =>
      unwrap(await api.PUT('/api/v1/sample/items/{id}', { params: { path: { id } }, body })),
    onSuccess,
  });
}

export function useChangeSampleItemStatus() {
  const onSuccess = useInvalidateOnSuccess();
  return useMutation({
    mutationFn: async ({ item, action }: { item: SampleItem; action: 'activate' | 'deactivate' }) =>
      unwrap(
        await api.POST(
          action === 'activate'
            ? '/api/v1/sample/items/{id}/activate'
            : '/api/v1/sample/items/{id}/deactivate',
          {
            params: { path: { id: item.id } },
            body: { version: item.version },
          },
        ),
      ),
    onSuccess,
  });
}
