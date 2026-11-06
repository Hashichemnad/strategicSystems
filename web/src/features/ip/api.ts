import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';
import type { Tone } from '@/ui';

type S = components['schemas'];
export type IpListItem = S['IpListItemDto'];
export type IpRecord = S['IpRecordDto'];
export type IpDashboard = S['IpDashboardDto'];
export type IpContributor = S['ContributorDto'];
export type IpContributorInput = S['ContributorInput'];
export type IpPeriod = S['PeriodDto'];
export type IpSettingsValue = S['IpSettings'];
export type ProtectionRule = S['ProtectionRule'];
export type IpSimilarItem = S['SimilarItemDto'];
export type IpMoetDraft = S['MoetDraftDto'];
export type IpListQuery = NonNullable<operations['ListIpRecords']['parameters']['query']>;

/** IP statuses (SPEC §7.2, docs/design/requests.md §4.2). */
export const ipStatuses = [
  'submitted',
  'under_review',
  'returned',
  'rejected',
  'approved_to_file',
  'filed',
  'registered',
  'rejected_by_moet',
  'expired',
  'renewed',
  'withdrawn',
] as const;

export const ipSources = ['request', 'document'] as const;
export const signatureStatuses = ['pending', 'signed', 'uploaded'] as const;
export const periodKinds = ['initial', 'renewal', 'internal'] as const;

export const statusTone: Record<string, Tone> = {
  submitted: 'info',
  under_review: 'info',
  returned: 'warning',
  rejected: 'danger',
  approved_to_file: 'info',
  filed: 'info',
  registered: 'success',
  rejected_by_moet: 'danger',
  expired: 'warning',
  renewed: 'success',
  withdrawn: 'neutral',
};

/** Lookup types of the IP Registry (Settings → Lists). */
export const ipLookups = {
  propertyType: 'ip.property-type',
  protectionType: 'ip.protection-type',
  contributorRole: 'ip.contributor-role',
} as const;

/** Owner type of the record's attachments (certificates and supporting files). */
export const IP_OWNER_TYPE = 'ip.record';

export const ipKeys = {
  all: ['ip'] as const,
  list: (query: IpListQuery) => ['ip', 'records', query] as const,
  record: (id: string) => ['ip', 'record', id] as const,
  dashboard: ['ip', 'dashboard'] as const,
  settings: ['ip', 'settings'] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

export function useIpRecords(query: IpListQuery) {
  return useQuery({
    queryKey: ipKeys.list(query),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ip/records', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useIpRecord(id: string) {
  return useQuery({
    queryKey: ipKeys.record(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ip/records/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useIpDashboard(range?: { from: string; to: string }) {
  return useQuery({
    queryKey: [...ipKeys.dashboard, range ?? null],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/ip/dashboard', { params: { query: range ?? {} }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useIpSettings() {
  return useQuery({
    queryKey: ipKeys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ip/settings', { signal })),
  });
}

export function useIpAudit(id: string, page: number, pageSize: number) {
  return useQuery({
    queryKey: [...ipKeys.record(id), 'audit', page, pageSize],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/ip/records/{id}/audit', {
          params: { path: { id }, query: { page, pageSize } },
          signal,
        }),
      ),
    placeholderData: keepPreviousData,
  });
}

/** The AI similarity body is `{ title, description }` on the server (the generated schema shares a name with Tasks' input). */
interface SimilarityBody {
  title: string | null;
  description: string | null;
  unitId: null;
}

/** Record actions and settings. Each returns the updated record (or settings document). */
export const ipApi = {
  file: async (id: string, body: S['FilingInput']) =>
    unwrap(await api.POST('/api/v1/ip/records/{id}/filing', { ...path(id), body })),
  registerInternally: async (id: string, body: S['InternalInput']) =>
    unwrap(await api.POST('/api/v1/ip/records/{id}/register-internally', { ...path(id), body })),
  register: async (id: string, body: S['RegistrationInput']) =>
    unwrap(await api.POST('/api/v1/ip/records/{id}/registration', { ...path(id), body })),
  moetRejection: async (id: string, body: S['MoetRejectionInput']) =>
    unwrap(await api.POST('/api/v1/ip/records/{id}/moet-rejection', { ...path(id), body })),
  close: async (id: string, body: S['InternalInput']) =>
    unwrap(await api.POST('/api/v1/ip/records/{id}/close', { ...path(id), body })),
  renew: async (id: string, body: S['RenewalInput']) =>
    unwrap(await api.POST('/api/v1/ip/records/{id}/renewal', { ...path(id), body })),
  setContributors: async (id: string, body: S['ContributorsInput']) =>
    unwrap(await api.PUT('/api/v1/ip/records/{id}/contributors', { ...path(id), body })),
  setMoetDescription: async (id: string, body: S['MoetDescriptionInput']) =>
    unwrap(await api.PUT('/api/v1/ip/records/{id}/moet-description', { ...path(id), body })),
  revealEmiratesId: async (id: string, contributorId: string) =>
    unwrap(
      await api.GET('/api/v1/ip/records/{id}/contributors/{contributorId}/emirates-id', {
        params: { path: { id, contributorId } },
      }),
    ),
  draftMoetDescription: async (id: string) =>
    unwrap(await api.POST('/api/v1/ip/ai/records/{id}/moet-description', { ...path(id) })),
  similar: async (title: string | null, description: string | null, excludeId?: string) => {
    const body: SimilarityBody = { title, description, unitId: null };
    return unwrap(
      await api.POST('/api/v1/ip/ai/similar', {
        params: { query: excludeId ? { excludeId } : {} },
        body,
      }),
    );
  },
  saveSettings: async (body: S['IpSettingsInput']) => unwrap(await api.PUT('/api/v1/ip/settings', { body })),
};

/** A record action: the server's answer replaces the cached record; lists and the dashboard refresh. */
export function useIpRecordMutation<TInput>(
  id: string,
  fn: (id: string, input: TInput) => Promise<IpRecord>,
) {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (input: TInput) => fn(id, input),
    onSuccess: (record) => {
      client.setQueryData(ipKeys.record(id), record);
      void client.invalidateQueries({ queryKey: [...ipKeys.all, 'records'] });
      void client.invalidateQueries({ queryKey: ipKeys.dashboard });
      void client.invalidateQueries({ queryKey: [...ipKeys.record(id), 'audit'] });
    },
  });
}

export function useSaveIpSettings() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: ipApi.saveSettings,
    onSuccess: (doc) => client.setQueryData(ipKeys.settings, doc),
  });
}

/** Download links (GET with the session cookie; the server checks access and audits). */
export const ipDownloads = {
  moetForm: (id: string) => `/api/v1/ip/records/${encodeURIComponent(id)}/moet-form`,
  certificate: (id: string) => `/api/v1/ip/records/${encodeURIComponent(id)}/certificate`,
};
