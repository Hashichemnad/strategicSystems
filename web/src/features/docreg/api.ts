import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, ApiError, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';
import { useAiCapabilities } from '@/features/ai/api';

type S = components['schemas'];
export type Library = S['LibraryDto'];
export type DocumentListItem = S['DocumentListItemDto'];
export type DocumentDetail = S['DocumentDto'];
export type DocumentVersion = S['VersionDto'];
export type ValidityEvent = S['ValidityEventDto'];
export type Facet = S['FacetDto'];
export type UnitFacet = S['UnitFacetDto'];
export type DocDashboard = S['DashboardDto'];
export type Compare = S['CompareDto'];
export type SummaryDraft = S['SummaryDraftDto'];
export type Answer = S['AnswerDto'];
export type DocRegSettings = S['DocRegSettings'];
export type DocTypeRule = S['DocTypeRule'];
export type SensitivityRule = S['SensitivityRule'];
export type LibraryQuery = NonNullable<operations['GetDocumentLibrary']['parameters']['query']>;

export const documentStatuses = [
  'submitted',
  'under_review',
  'returned',
  'rejected',
  'approved',
  'published',
  'expired',
  'withdrawn',
] as const;
export const versionStatuses = [
  'submitted',
  'under_review',
  'returned',
  'rejected',
  'approved',
  'current',
  'superseded',
  'withdrawn',
  'cancelled',
] as const;
export const validityKinds = ['reminder', 'confirmed', 'expired', 'expiry-changed'] as const;

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const statusTone: Record<string, Tone> = {
  submitted: 'info',
  under_review: 'info',
  returned: 'warning',
  rejected: 'danger',
  approved: 'info',
  published: 'success',
  expired: 'danger',
  withdrawn: 'neutral',
};

export const versionTone: Record<string, Tone> = {
  submitted: 'info',
  under_review: 'info',
  returned: 'warning',
  rejected: 'danger',
  approved: 'info',
  current: 'success',
  superseded: 'neutral',
  withdrawn: 'neutral',
  cancelled: 'neutral',
};

/** Seeded sensitivity codes; other (admin-added) codes are neutral. */
export const sensitivityTone: Record<string, Tone> = {
  public: 'success',
  internal: 'info',
  confidential: 'warning',
  secret: 'danger',
};
export const lockedSensitivities = new Set(['confidential', 'secret']);

/** Days within which a published document counts as "expires soon" (server: DocumentService.SoonDays). */
export const SOON_DAYS = 90;

export const docregKeys = {
  all: ['docreg'] as const,
  library: (query: LibraryQuery) => ['docreg', 'library', query] as const,
  document: (id: string) => ['docreg', 'document', id] as const,
  compare: (id: string, from: string, to: string) => ['docreg', 'compare', id, from, to] as const,
  dashboard: ['docreg', 'dashboard'] as const,
  settings: ['docreg', 'settings'] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

export function useLibrary(query: LibraryQuery) {
  return useQuery({
    queryKey: docregKeys.library(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/docreg/library', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useDocument(id: string) {
  return useQuery({
    queryKey: docregKeys.document(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/docreg/documents/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useCompare(id: string, from: string | undefined, to: string | undefined) {
  return useQuery({
    queryKey: docregKeys.compare(id, from ?? '', to ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/docreg/documents/{id}/compare', {
          params: { path: { id }, query: { from: from ?? '', to: to ?? '' } },
          signal,
        }),
      ),
    enabled: !!from && !!to && from !== to,
    retry: false,
  });
}

export function useDocDashboard(range?: { from: string; to: string }) {
  return useQuery({
    queryKey: [...docregKeys.dashboard, range ?? null],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/docreg/dashboard', { params: { query: range ?? {} }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useDocRegSettings() {
  return useQuery({
    queryKey: docregKeys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/docreg/settings', { signal })),
  });
}

/** Roles as `<system>.<code>` keys for the sensitivity clearance editor (needs the role-read permission; empty otherwise). */
export function useRoleOptions() {
  return useQuery({
    queryKey: ['access', 'roles'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/roles', { signal })),
    retry: false,
    staleTime: 60_000,
  });
}

/** A mutation that refreshes every Document Registry query afterwards. */
export function useDocRegMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: docregKeys.all });
    },
  });
}

export const docregApi = {
  confirmValid: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/docreg/documents/{id}/confirm-valid', { ...path(id), body: { comment } })),
  setExpiry: async ({
    id,
    expiryDate,
    comment,
    version,
  }: {
    id: string;
    expiryDate: string | null;
    comment: string | null;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/docreg/documents/{id}/expiry', {
        ...path(id),
        body: { expiryDate, comment, version },
      }),
    ),
  acknowledge: async (id: string) =>
    unwrap(await api.POST('/api/v1/docreg/documents/{id}/acknowledge', path(id))),
  summary: async (id: string) => unwrap(await api.POST('/api/v1/docreg/ai/documents/{id}/summary', path(id))),
  whatChanged: async ({ id, from, to }: { id: string; from: string; to: string }) =>
    unwrap(
      await api.POST('/api/v1/docreg/ai/documents/{id}/what-changed', {
        params: { path: { id }, query: { from, to } },
      }),
    ),
  ask: async (question: string) => unwrap(await api.POST('/api/v1/docreg/ai/ask', { body: { question } })),
  saveSettings: async ({ settings, version }: { settings: DocRegSettings; version: number }) =>
    unwrap(await api.PUT('/api/v1/docreg/settings', { body: { settings, version } })),
};

/** Stamped ("uncontrolled when printed") copy of the current or a chosen published version; a GET the browser downloads itself. */
export function downloadUrl(id: string, versionId?: string | null): string {
  const base = `/api/v1/docreg/documents/${encodeURIComponent(id)}/download`;
  return versionId ? `${base}?versionId=${encodeURIComponent(versionId)}` : base;
}

export type DocRegAiFeature =
  'docreg-summary' | 'docreg-what-changed' | 'docreg-qa' | 'docreg-classify' | 'docreg-duplicates';

/** True when AI is on and this Document Registry feature is available to the user (CLAUDE.md §12). */
export function useDocRegAi(feature: DocRegAiFeature): boolean {
  const capabilities = useAiCapabilities();
  return capabilities.data?.enabled === true && capabilities.data.features[feature] === true;
}

/** AI unavailable (switched off, not permitted or no provider): the AI box hides instead of showing an error. */
export function isAiUnavailable(error: unknown): boolean {
  return (
    error instanceof ApiError &&
    (error.status === 403 || error.status === 404 || error.problem.code.startsWith('ai.'))
  );
}

export function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409;
}

/** Whole days from today (local calendar) to an ISO date; negative when past. */
export function daysUntil(date: string, today = new Date()): number {
  const [y = 0, m = 1, d = 1] = date.split('-').map(Number);
  const target = Date.UTC(y, m - 1, d);
  const now = Date.UTC(today.getFullYear(), today.getMonth(), today.getDate());
  return Math.round((target - now) / 86_400_000);
}

/** Link of the generic request wizard for a new version of a document, prefilled (docs/design/requests.md §3.3). */
export function newVersionValues(document: DocumentDetail): string {
  return JSON.stringify({
    isNewVersion: true,
    existingDocument: document.id,
    nameAr: document.nameAr,
    nameEn: document.nameEn,
    ...(document.ownerUnit ? { ownerUnit: document.ownerUnit.id } : {}),
  });
}

export function withdrawalValues(documentId: string): string {
  return JSON.stringify({ document: documentId });
}

/** Default compare pair: the version before the current one → the current one (versions arrive newest first). */
export function defaultPair(versions: readonly DocumentVersion[]): { from?: string; to?: string } {
  const currentIndex = versions.findIndex((v) => v.status === 'current');
  const toIndex = currentIndex >= 0 ? currentIndex : 0;
  return { to: versions[toIndex]?.id, from: versions[toIndex + 1]?.id ?? versions[toIndex - 1]?.id };
}
