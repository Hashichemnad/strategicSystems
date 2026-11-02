import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type StoredFile = components['schemas']['FileDto'];
export type FileVersion = components['schemas']['FileVersionDto'];

export const fileKeys = {
  owner: (ownerType: string, ownerId: string) => ['files', ownerType, ownerId] as const,
  versions: (fileId: string) => ['files', 'versions', fileId] as const,
};

/** Download / preview address (authorised and audited by the server). */
export function contentUrl(fileId: string, versionId?: string, download = false): string {
  const params = new URLSearchParams();
  if (versionId) params.set('versionId', versionId);
  if (download) params.set('download', 'true');
  const query = params.toString();
  return `/api/v1/files/${fileId}/content${query ? `?${query}` : ''}`;
}

export function useAttachments(ownerType: string, ownerId: string) {
  return useQuery({
    queryKey: fileKeys.owner(ownerType, ownerId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/files/owners/{ownerType}/{ownerId}', {
          params: { path: { ownerType, ownerId } },
          signal,
        }),
      ),
  });
}

export function useFileVersions(fileId: string | undefined) {
  return useQuery({
    queryKey: fileKeys.versions(fileId ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/files/{fileId}/versions', {
          params: { path: { fileId: fileId ?? '' } },
          signal,
        }),
      ),
    enabled: Boolean(fileId),
  });
}

function form(file: File, comment: string | undefined): FormData {
  const data = new FormData();
  data.append('file', file);
  if (comment) data.append('comment', comment);
  return data;
}

export function useUploadAttachment(ownerType: string, ownerId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ file, comment, replaces }: { file: File; comment?: string; replaces?: string }) =>
      replaces
        ? unwrap(
            await api.POST('/api/v1/files/{fileId}/versions', {
              params: { path: { fileId: replaces } },
              body: form(file, comment) as unknown as { file: string },
              bodySerializer: (body: unknown) => body as FormData,
            }),
          )
        : unwrap(
            await api.POST('/api/v1/files/owners/{ownerType}/{ownerId}', {
              params: { path: { ownerType, ownerId } },
              body: form(file, comment) as unknown as { file: string },
              bodySerializer: (body: unknown) => body as FormData,
            }),
          ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['files'] }),
  });
}

export function useDeleteFile() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ fileId, reason }: { fileId: string; reason: string }) =>
      unwrap(
        await api.POST('/api/v1/files/{fileId}/delete', { params: { path: { fileId } }, body: { reason } }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['files'] }),
  });
}
