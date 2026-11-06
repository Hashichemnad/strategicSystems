import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type CapOptions = S['CapOptionsDto'];
export type CapSource = S['CapSourceDto'];
export type CapSourceValues = S['CapSourceValues'];
export type CapSourceListItem = S['CapSourceListItemDto'];
export type CapRemark = S['CapRemarkDto'];
export type CapRemarkValues = S['CapRemarkValues'];
export type CapRemarkListItem = S['CapRemarkListItemDto'];
export type CapRootCauseValues = S['CapRootCauseValues'];
export type CapAction = S['CapActionDto'];
export type CapActionValues = S['CapActionValues'];
export type CapActionListItem = S['CapActionListItemDto'];
export type CapAchievementUpdate = S['CapAchievementUpdateDto'];
export type CapExtension = S['CapExtensionDto'];
export type CapValidationView = S['CapValidationViewDto'];
export type CapDashboard = S['CapDashboardDto'];
export type CapCount = S['CapCountDto'];
export type CapSuggestion = S['CapSuggestionDto'];
export type CapSettings = S['CapSettings'];
export type CapPerson = S['CapPersonDto'];
export type CapUnit = S['CapUnitDto'];
export type CapKpiTrend = S['CapKpiTrendDto'];
export type CapSourceProgress = S['CapSourceProgress'];
export type CapRemarkProgress = S['CapRemarkProgress'];
export type RemarkExtraction = S['RemarkExtractionDto'];
export type RootCauseDraft = S['RootCauseDraftDto'];
export type ActionSuggestion = S['ActionSuggestionDto'];
export type ActionQuality = S['ActionQualityDto'];
export type CapSummaryDraft = S['CapSummaryDraftDto'];
export type CapRemarkQuery = NonNullable<operations['ListCapRemarks']['parameters']['query']>;
export type CapActionQuery = NonNullable<operations['ListCapActions']['parameters']['query']>;
export type CapSourceQuery = NonNullable<operations['ListCapSources']['parameters']['query']>;

/** Codes shown in the UI (the message-key test proves each has a label in both languages). */
export const remarkStatuses = [
  'assigned',
  'rejection_pending',
  'rejected',
  'planning',
  'validation',
  'correction',
  'implementation',
  'verification',
  'completed',
  'cancelled',
] as const;
export const remarkStages = [
  'analysis',
  'corrective-action',
  'implementation',
  'verification',
  'closed',
] as const;
export const actionStatuses = ['not_started', 'in_progress', 'late', 'completed', 'cancelled'] as const;
export const validationStatuses = ['draft', 'pending', 'approved', 'rejected', 'returned'] as const;
export const priorities = ['critical', 'high', 'medium', 'low'] as const;
export const rootCauseMethods = ['five-whys', 'fishbone', 'text'] as const;
export const fishboneCategories = [
  'people',
  'process',
  'equipment',
  'materials',
  'environment',
  'management',
] as const;
export const remarkViews = ['all', 'mine', 'to-accept', 'my-unit', 'overdue', 'to-confirm', 'open'] as const;
export const actionViews = [
  'mine',
  'all',
  'to-validate',
  'late',
  'budget',
  'pending-completion',
  'open',
] as const;
export const suggestionKinds = ['kpi-miss', 'risk-event', 'risk-control'] as const;
export const raiseModes = ['off', 'manual', 'automatic'] as const;
export const qualityFlags = [
  'vague',
  'no-assignee',
  'no-dates',
  'end-before-start',
  'end-in-past',
  'after-remark-due',
  'budget-without-cost',
  'too-short',
] as const;
export const extensionStatuses = ['pending', 'approved', 'rejected', 'cancelled'] as const;

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const remarkTone: Record<string, Tone> = {
  assigned: 'info',
  rejection_pending: 'warning',
  rejected: 'neutral',
  planning: 'info',
  validation: 'info',
  correction: 'warning',
  implementation: 'info',
  verification: 'info',
  completed: 'success',
  cancelled: 'neutral',
};

export const actionTone: Record<string, Tone> = {
  not_started: 'neutral',
  in_progress: 'info',
  late: 'danger',
  completed: 'success',
  cancelled: 'neutral',
};

export const validationTone: Record<string, Tone> = {
  draft: 'neutral',
  pending: 'info',
  approved: 'success',
  rejected: 'danger',
  returned: 'warning',
};

export const priorityTone: Record<string, Tone> = {
  critical: 'danger',
  high: 'warning',
  medium: 'info',
  low: 'neutral',
};

export const capKeys = { all: ['cap'] as const };

const path = (id: string) => ({ params: { path: { id } } });

export function useCapOptions() {
  return useQuery({
    queryKey: [...capKeys.all, 'options'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/cap/options', { signal })),
    staleTime: 60_000,
  });
}

export function useCapDashboard(query: { unitId?: string; year?: number; from?: string; to?: string }) {
  return useQuery({
    queryKey: [...capKeys.all, 'dashboard', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/dashboard', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useCapSources(query: CapSourceQuery) {
  return useQuery({
    queryKey: [...capKeys.all, 'sources', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/sources', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useCapSource(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'source', id],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/cap/sources/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useCapRemarks(query: CapRemarkQuery, enabled = true) {
  return useQuery({
    queryKey: [...capKeys.all, 'remarks', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/remarks', { params: { query }, signal })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useCapRemark(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'remark', id],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/cap/remarks/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useRemarkActions(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'remark', id, 'actions'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/remarks/{id}/actions', { ...path(id), signal })),
  });
}

export function useKpiTrend(id: string, enabled: boolean) {
  return useQuery({
    queryKey: [...capKeys.all, 'remark', id, 'kpi-trend'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/remarks/{id}/kpi-trend', { ...path(id), signal })),
    enabled,
  });
}

export function useLinkableActions(id: string, search: string, enabled: boolean) {
  return useQuery({
    queryKey: [...capKeys.all, 'remark', id, 'linkable', search],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/cap/remarks/{id}/linkable-actions', {
          params: { path: { id }, query: { search: search || undefined } },
          signal,
        }),
      ),
    enabled,
  });
}

export function useValidationView(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'remark', id, 'validation'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/remarks/{id}/validation', { ...path(id), signal })),
    retry: false,
  });
}

export function useCapActions(query: CapActionQuery) {
  return useQuery({
    queryKey: [...capKeys.all, 'actions', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/actions', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useCapAction(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'action', id],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/cap/actions/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useActionUpdates(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'action', id, 'updates'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/actions/{id}/updates', { ...path(id), signal })),
  });
}

export function useActionExtensions(id: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'action', id, 'extensions'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/cap/actions/{id}/extensions', { ...path(id), signal })),
  });
}

export function useCapProjects(search: string, enabled: boolean) {
  return useQuery({
    queryKey: [...capKeys.all, 'projects', search],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/cap/projects', { params: { query: { search: search || undefined } }, signal }),
      ),
    enabled,
  });
}

export function useCapSuggestions(status: string) {
  return useQuery({
    queryKey: [...capKeys.all, 'suggestions', status],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/cap/suggestions', { params: { query: { status, pageSize: 100 } }, signal }),
      ),
  });
}

export function useCapSettings(enabled: boolean) {
  return useQuery({
    queryKey: [...capKeys.all, 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/cap/settings', { signal })),
    enabled,
  });
}

export function useCapMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: capKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['workflow'] });
    },
  });
}

export const capApi = {
  createSource: async (values: CapSourceValues) =>
    unwrap(await api.POST('/api/v1/cap/sources', { body: values })),
  createRemark: async ({ sourceId, values }: { sourceId: string; values: CapRemarkValues }) =>
    unwrap(await api.POST('/api/v1/cap/sources/{id}/remarks', { ...path(sourceId), body: values })),
  updateRemark: async ({ id, values, version }: { id: string; values: CapRemarkValues; version: number }) =>
    unwrap(await api.PUT('/api/v1/cap/remarks/{id}', { ...path(id), body: { values, version } })),
  accept: async (id: string) => unwrap(await api.POST('/api/v1/cap/remarks/{id}/accept', path(id))),
  reject: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/reject', { ...path(id), body: { reason } })),
  confirmRejection: async ({ id, confirm, note }: { id: string; confirm: boolean; note: string | null }) =>
    unwrap(
      await api.POST('/api/v1/cap/remarks/{id}/confirm-rejection', { ...path(id), body: { confirm, note } }),
    ),
  reassign: async ({ id, ownerEmployeeId }: { id: string; ownerEmployeeId: string }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/reassign', { ...path(id), body: { ownerEmployeeId } })),
  saveRootCause: async ({
    id,
    values,
    version,
  }: {
    id: string;
    values: CapRootCauseValues;
    version: number;
  }) =>
    unwrap(await api.PUT('/api/v1/cap/remarks/{id}/root-cause', { ...path(id), body: { values, version } })),
  submit: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/submit', { ...path(id), body: { comment } })),
  cancelRemark: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/cancel', { ...path(id), body: { reason } })),
  verify: async ({ id, effective, comment }: { id: string; effective: boolean; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/verify', { ...path(id), body: { effective, comment } })),
  createAction: async ({ remarkId, values }: { remarkId: string; values: CapActionValues }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/actions', { ...path(remarkId), body: values })),
  updateAction: async ({ id, values, version }: { id: string; values: CapActionValues; version: number }) =>
    unwrap(await api.PUT('/api/v1/cap/actions/{id}', { ...path(id), body: { values, version } })),
  linkAction: async ({ remarkId, actionId }: { remarkId: string; actionId: string }) =>
    unwrap(await api.POST('/api/v1/cap/remarks/{id}/link-action', { ...path(remarkId), body: { actionId } })),
  unlinkAction: async ({ remarkId, actionId }: { remarkId: string; actionId: string }) =>
    unwrap(
      await api.POST('/api/v1/cap/remarks/{id}/unlink-action', { ...path(remarkId), body: { actionId } }),
    ),
  validate: async ({
    id,
    decision,
    comment,
    projectId,
  }: {
    id: string;
    decision: string;
    comment: string | null;
    projectId: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/cap/actions/{id}/validate', {
        ...path(id),
        body: { decision, comment, projectId },
      }),
    ),
  linkProject: async ({ id, projectId }: { id: string; projectId: string | null }) =>
    unwrap(await api.POST('/api/v1/cap/actions/{id}/link-project', { ...path(id), body: { projectId } })),
  progress: async ({
    id,
    pct,
    note,
    actualStart,
    actualEnd,
    version,
  }: {
    id: string;
    pct: number;
    note: string | null;
    actualStart: string | null;
    actualEnd: string | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/cap/actions/{id}/progress', {
        ...path(id),
        body: { pct, note, actualStart, actualEnd, version, fromMcp: false },
      }),
    ),
  confirmDraft: async ({ id, updateId }: { id: string; updateId: string }) =>
    unwrap(
      await api.POST('/api/v1/cap/actions/{id}/updates/{updateId}/confirm', {
        params: { path: { id, updateId } },
      }),
    ),
  requestCompletion: async ({ id, note }: { id: string; note: string | null }) =>
    unwrap(await api.POST('/api/v1/cap/actions/{id}/request-completion', { ...path(id), body: { note } })),
  requestExtension: async ({
    id,
    requestedEnd,
    justification,
  }: {
    id: string;
    requestedEnd: string;
    justification: string;
  }) =>
    unwrap(
      await api.POST('/api/v1/cap/actions/{id}/extensions', {
        ...path(id),
        body: { requestedEnd, justification },
      }),
    ),
  cancelAction: async ({ id, reason }: { id: string; reason: string | null }) =>
    unwrap(await api.POST('/api/v1/cap/actions/{id}/cancel', { ...path(id), body: { reason } })),
  raise: async ({ id, unitId }: { id: string; unitId: string | null }) =>
    unwrap(await api.POST('/api/v1/cap/suggestions/{id}/raise', { ...path(id), body: { unitId } })),
  dismiss: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/cap/suggestions/{id}/dismiss', { ...path(id), body: { reason } })),
  saveSettings: async ({ settings, version }: { settings: CapSettings; version: number }) =>
    unwrap(await api.PUT('/api/v1/cap/settings', { body: { settings, version } })),
  extract: async ({ sourceId, text }: { sourceId: string; text: string }) =>
    unwrap(await api.POST('/api/v1/cap/ai/sources/{id}/extract', { ...path(sourceId), body: { text } })),
  rootCauseDraft: async (remarkId: string) =>
    unwrap(await api.POST('/api/v1/cap/ai/remarks/{id}/root-cause', path(remarkId))),
  suggestActions: async (remarkId: string) =>
    unwrap(await api.POST('/api/v1/cap/ai/remarks/{id}/suggestions', path(remarkId))),
  quality: async (actionId: string) =>
    unwrap(await api.POST('/api/v1/cap/ai/actions/{id}/quality', path(actionId))),
  summary: async (query: { unitId: string | null; year: number | null }) =>
    unwrap(await api.POST('/api/v1/cap/ai/summary', { body: query })),
};

/** Report export URL (PDF / Excel, bilingual, audited on the server). */
export function capReportUrl(
  kind: string,
  format: 'pdf' | 'xlsx',
  language: string,
  query: Record<string, string> = {},
) {
  const params = new URLSearchParams({ format, language, ...query });
  return `/api/v1/cap/reports/${kind}?${params.toString()}`;
}
