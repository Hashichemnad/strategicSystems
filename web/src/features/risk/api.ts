import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type RiskValues = S['RiskValues'];
export type Risk = S['RiskDto'];
export type RiskListItem = S['RiskListItemDto'];
export type RiskOptions = S['RiskOptionsDto'];
export type RiskOption = S['RiskOptionDto'];
export type RiskCategory = S['RiskCategoryDto'];
export type RiskUnit = S['RiskUnitDto'];
export type RiskPerson = S['RiskPersonDto'];
export type FrameworkVersion = S['FrameworkVersionDto'];
export type FrameworkContent = S['FrameworkContent'];
export type FrameworkSummary = S['FrameworkSummaryDto'];
export type FrameworkPreview = S['FrameworkPreviewDto'];
export type RatingBand = S['RatingBand'];
export type MatrixCell = S['MatrixCell'];
export type LikelihoodLevel = S['LikelihoodLevel'];
export type ImpactLevel = S['ImpactLevel'];
export type ImpactArea = S['ImpactArea'];
export type EffectivenessLevel = S['EffectivenessLevel'];
export type RiskSettingsValue = S['RiskSettings'];
export type RiskDashboard = S['RiskDashboardDto'];
export type HeatCell = S['HeatCellDto'];
export type HeatCellRisks = S['HeatCellRisksDto'];
export type ProjectRiskRow = S['ProjectRiskRowDto'];
export type RiskAssessment = S['RiskAssessmentDto'];
export type CommitteeDecision = S['CommitteeDecisionDto'];
export type Control = S['ControlDto'];
export type ControlValues = S['ControlValues'];
export type TreatmentAction = S['ActionDto'];
export type ActionValues = S['ActionValues'];
export type ActionUpdate = S['ActionUpdateDto'];
export type Kri = S['KriDto'];
export type KriValues = S['KriValues'];
export type RiskEvent = S['EventDto'];
export type EventValues = S['EventValues'];
export type Review = S['ReviewDto'];
export type ReviewValues = S['ReviewValues'];
export type OfficeInput = S['OfficeInput'];
export type ConvertInput = S['ConvertInput'];
export type CategoryInput = S['CategoryInput'];
export type SuggestedRisk = S['SuggestedRiskDto'];
export type StatementCheck = S['StatementCheckDto'];
export type DuplicateCheck = S['RiskDuplicateCheckDto'];
export type TreatmentSuggestion = S['TreatmentSuggestionDto'];
export type ReviewAssist = S['ReviewAssistDto'];
export type ReportDraft = S['RiskReportDraftDto'];
export type RiskListQuery = NonNullable<operations['ListRisks']['parameters']['query']>;
export type RiskFilterQuery = NonNullable<operations['GetRiskDashboard']['parameters']['query']>;

/** Codes shown in the UI (the message-key test proves each has a label in both languages). */
export const riskStatuses = ['draft', 'under_review', 'active', 'closed', 'rejected'] as const;
export const residualStatuses = ['no-issue', 'continuous-review', 'active-management'] as const;
export const actionStatuses = ['not_started', 'in_progress', 'completed', 'cancelled'] as const;
export const controlStatuses = ['active', 'retired'] as const;
export const reviewStatuses = ['draft', 'submitted', 'approved', 'returned', 'cancelled'] as const;
export const kriLevels = ['normal', 'warning', 'breach', 'none'] as const;
export const kriBases = ['value', 'performance'] as const;
export const kriDirections = ['above', 'below'] as const;
export const assessmentKinds = [
  'registration',
  'approval',
  'review',
  'reassessment',
  'recalculation',
  'closure',
  'reopen',
] as const;
export const scoringMethods = ['multiplicative', 'additive', 'custom'] as const;
export const projectResidualRules = ['control-reduction', 'formula'] as const;
export const recalculationModes = ['now', 'next-review'] as const;
export const riskTypes = ['internal', 'external'] as const;
export const registerViews = [
  'all',
  'mine',
  'my-unit',
  'top10',
  'outside-appetite',
  'overdue-review',
  'under-review',
  'escalated',
  'reassessment',
] as const;
export const reportKinds = ['register', 'top10', 'unit-profile', 'action-tracker'] as const;
export const statementFlags = [
  'missing-cause',
  'missing-event',
  'missing-consequence',
  'short-cause',
  'short-event',
  'short-consequence',
  'vague',
  'cause-equals-event',
] as const;
export const reviewFrequencyChoices = [1, 2, 3, 4, 6, 12] as const;

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const statusTone: Record<string, Tone> = {
  draft: 'neutral',
  under_review: 'info',
  active: 'success',
  closed: 'neutral',
  rejected: 'danger',
};

export const actionTone: Record<string, Tone> = {
  not_started: 'neutral',
  in_progress: 'info',
  completed: 'success',
  cancelled: 'neutral',
};

export const kriTone: Record<string, Tone> = {
  normal: 'success',
  warning: 'warning',
  breach: 'danger',
  none: 'neutral',
};

export const reviewTone: Record<string, Tone> = {
  draft: 'neutral',
  submitted: 'info',
  approved: 'success',
  returned: 'warning',
  cancelled: 'neutral',
};

export const residualStatusTone: Record<string, Tone> = {
  'no-issue': 'success',
  'continuous-review': 'warning',
  'active-management': 'danger',
};

export const riskKeys = {
  all: ['risk'] as const,
  risks: (query: RiskListQuery) => [...riskKeys.all, 'risks', query] as const,
  risk: (id: string) => [...riskKeys.all, 'risk', id] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

export function useRiskOptions() {
  return useQuery({
    queryKey: [...riskKeys.all, 'options'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/risk/options', { signal })),
    staleTime: 60_000,
  });
}

export function useRisks(query: RiskListQuery) {
  return useQuery({
    queryKey: riskKeys.risks(query),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/risk/risks', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useRisk(id: string | undefined) {
  return useQuery({
    queryKey: riskKeys.risk(id ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/risk/risks/{id}', { ...path(id ?? ''), signal })),
    enabled: !!id,
    retry: false,
  });
}

function useRiskChild<T>(id: string, key: string, load: (signal: AbortSignal) => Promise<T>) {
  return useQuery({
    queryKey: [...riskKeys.risk(id), key],
    queryFn: ({ signal }) => load(signal),
  });
}

export const useRiskAssessments = (id: string) =>
  useRiskChild(id, 'assessments', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/risks/{id}/assessments', { ...path(id), signal })),
  );
export const useCommitteeDecisions = (id: string) =>
  useRiskChild(id, 'decisions', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/risks/{id}/committee-decisions', { ...path(id), signal })),
  );
export const useControls = (id: string) =>
  useRiskChild(id, 'controls', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/risks/{id}/controls', { ...path(id), signal })),
  );
export const useActions = (id: string) =>
  useRiskChild(id, 'actions', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/risks/{id}/actions', { ...path(id), signal })),
  );
export const useKris = (id: string) =>
  useRiskChild(id, 'kris', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/risks/{id}/kris', { ...path(id), signal })),
  );
export const useReviews = (id: string) =>
  useRiskChild(id, 'reviews', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/risks/{id}/reviews', { ...path(id), signal })),
  );
export const useRiskEvents = (id: string) =>
  useRiskChild(id, 'events', async (signal) =>
    unwrap(await api.GET('/api/v1/risk/events', { params: { query: { riskId: id } }, signal })),
  );

export function useActionUpdates(actionId: string) {
  return useQuery({
    queryKey: [...riskKeys.all, 'action-updates', actionId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/risk/actions/{id}/updates', { ...path(actionId), signal })),
  });
}

export function useMyActions(includeCompleted: boolean) {
  return useQuery({
    queryKey: [...riskKeys.all, 'my-actions', includeCompleted],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/risk/my-actions', { params: { query: { includeCompleted } }, signal })),
  });
}

export function useRiskDashboard(filter: RiskFilterQuery, enabled = true) {
  return useQuery({
    queryKey: [...riskKeys.all, 'dashboard', filter],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/risk/dashboard', { params: { query: filter }, signal })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useHeatCell(
  cell: { kind: 'inherent' | 'residual'; likelihood: number; impact: number } | undefined,
  filter: RiskFilterQuery,
) {
  return useQuery({
    queryKey: [...riskKeys.all, 'heat', cell, filter],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/risk/heatmap', {
          params: {
            query: {
              kind: cell?.kind ?? 'inherent',
              likelihood: cell?.likelihood ?? 1,
              impact: cell?.impact ?? 1,
              ...filter,
            },
          },
          signal,
        }),
      ),
    enabled: !!cell,
  });
}

export function useProjectRisks(
  query: { band?: string; search?: string; projectId?: string },
  enabled = true,
) {
  return useQuery({
    queryKey: [...riskKeys.all, 'project-risks', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/risk/project-risks', { params: { query }, signal })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useMechanism() {
  return useQuery({
    queryKey: [...riskKeys.all, 'mechanism'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/risk/mechanism', { signal })),
  });
}

export function useFramework() {
  return useQuery({
    queryKey: [...riskKeys.all, 'framework'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/risk/framework', { signal })),
  });
}

export function useFrameworkVersions() {
  return useQuery({
    queryKey: [...riskKeys.all, 'framework', 'versions'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/risk/framework/versions', { signal })),
  });
}

export function useRiskCategories(includeInactive: boolean) {
  return useQuery({
    queryKey: [...riskKeys.all, 'categories', includeInactive],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/risk/categories', { params: { query: { includeInactive } }, signal })),
  });
}

export function useRiskSettings() {
  return useQuery({
    queryKey: [...riskKeys.all, 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/risk/settings', { signal })),
  });
}

export function useFrameworkPreview(content: FrameworkContent | undefined) {
  return useQuery({
    queryKey: [...riskKeys.all, 'framework', 'preview', content],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.POST('/api/v1/risk/framework/preview', {
          body: { content: content ?? ({} as FrameworkContent) },
          signal,
        }),
      ),
    enabled: !!content,
    placeholderData: keepPreviousData,
    retry: false,
  });
}

/** A mutation that refreshes every risk query (and workflow / current-status panels) afterwards. */
export function useRiskMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: riskKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['workflow'] });
    },
  });
}

export const riskApi = {
  create: async (values: RiskValues) => unwrap(await api.POST('/api/v1/risk/risks', { body: values })),
  update: async ({ id, values, version }: { id: string; values: RiskValues; version: number }) =>
    unwrap(await api.PUT('/api/v1/risk/risks/{id}', { ...path(id), body: { values, version } })),
  submit: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/risk/risks/{id}/submit', { ...path(id), body: { comment } })),
  recall: async (id: string) => unwrap(await api.POST('/api/v1/risk/risks/{id}/recall', path(id))),
  office: async ({ id, input }: { id: string; input: OfficeInput }) =>
    unwrap(await api.POST('/api/v1/risk/risks/{id}/office-update', { ...path(id), body: input })),
  requestClosure: async ({ id, reasonCode, note }: { id: string; reasonCode: string; note: string | null }) =>
    unwrap(
      await api.POST('/api/v1/risk/risks/{id}/request-closure', { ...path(id), body: { reasonCode, note } }),
    ),
  reopen: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/risk/risks/{id}/reopen', { ...path(id), body: { reason } })),
  escalate: async ({ id, escalated, note }: { id: string; escalated: boolean; note: string | null }) =>
    unwrap(await api.POST('/api/v1/risk/risks/{id}/escalation', { ...path(id), body: { escalated, note } })),
  decide: async ({
    id,
    decisionCode,
    note,
    decidedOn,
  }: {
    id: string;
    decisionCode: string;
    note: string | null;
    decidedOn: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/risk/risks/{id}/committee-decisions', {
        ...path(id),
        body: { decisionCode, note, decidedOn },
      }),
    ),
  discard: async (id: string) => unwrap(await api.POST('/api/v1/risk/risks/{id}/discard', path(id))),
  saveControl: async ({
    riskId,
    controlId,
    values,
    version,
  }: {
    riskId: string;
    controlId?: string;
    values: ControlValues;
    version: number | null;
  }) =>
    controlId
      ? unwrap(
          await api.PUT('/api/v1/risk/risks/{id}/controls/{controlId}', {
            params: { path: { id: riskId, controlId } },
            body: { values, version },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/risk/risks/{id}/controls', {
            ...path(riskId),
            body: { values, version: null },
          }),
        ),
  rateControl: async ({
    id,
    effectivenessCode,
    version,
  }: {
    id: string;
    effectivenessCode: string | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/risk/controls/{id}/rate', {
        ...path(id),
        body: { effectivenessCode, version },
      }),
    ),
  retireControl: async (id: string) => unwrap(await api.POST('/api/v1/risk/controls/{id}/retire', path(id))),
  saveAction: async ({
    riskId,
    actionId,
    values,
    version,
  }: {
    riskId: string;
    actionId?: string;
    values: ActionValues;
    version: number | null;
  }) =>
    actionId
      ? unwrap(
          await api.PUT('/api/v1/risk/risks/{id}/actions/{actionId}', {
            params: { path: { id: riskId, actionId } },
            body: { values, version },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/risk/risks/{id}/actions', {
            ...path(riskId),
            body: { values, version: null },
          }),
        ),
  cancelAction: async (id: string) => unwrap(await api.POST('/api/v1/risk/actions/{id}/cancel', path(id))),
  progress: async ({
    id,
    completionPct,
    note,
    actualStart,
    actualEnd,
    version,
  }: {
    id: string;
    completionPct: number;
    note: string | null;
    actualStart: string | null;
    actualEnd: string | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/risk/actions/{id}/progress', {
        ...path(id),
        body: { completionPct, note, actualStart, actualEnd, version, fromMcp: false },
      }),
    ),
  convert: async ({ id, input }: { id: string; input: ConvertInput }) =>
    unwrap(await api.POST('/api/v1/risk/actions/{id}/convert-to-control', { ...path(id), body: input })),
  saveKri: async ({
    riskId,
    kriId,
    values,
    version,
  }: {
    riskId: string;
    kriId?: string;
    values: KriValues;
    version: number | null;
  }) =>
    kriId
      ? unwrap(
          await api.PUT('/api/v1/risk/risks/{id}/kris/{kriId}', {
            params: { path: { id: riskId, kriId } },
            body: { values, version },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/risk/risks/{id}/kris', {
            ...path(riskId),
            body: { values, version: null },
          }),
        ),
  removeKri: async (id: string) => unwrap(await api.POST('/api/v1/risk/kris/{id}/remove', path(id))),
  logEvent: async (values: EventValues) =>
    unwrap(await api.POST('/api/v1/risk/events', { body: { values, version: null } })),
  startReview: async (riskId: string) =>
    unwrap(await api.POST('/api/v1/risk/risks/{id}/reviews', path(riskId))),
  saveReview: async ({ id, values, version }: { id: string; values: ReviewValues; version: number }) =>
    unwrap(await api.PUT('/api/v1/risk/reviews/{id}', { ...path(id), body: { values, version } })),
  submitReview: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/risk/reviews/{id}/submit', { ...path(id), body: { comment } })),
  previewFramework: async (content: FrameworkContent) =>
    unwrap(await api.POST('/api/v1/risk/framework/preview', { body: { content } })),
  saveFramework: async ({
    content,
    reason,
    recalculation,
  }: {
    content: FrameworkContent;
    reason: string;
    recalculation: string;
  }) => unwrap(await api.POST('/api/v1/risk/framework', { body: { content, reason, recalculation } })),
  saveCategory: async ({ id, input }: { id?: string; input: CategoryInput }) =>
    id
      ? unwrap(await api.PUT('/api/v1/risk/categories/{id}', { ...path(id), body: input }))
      : unwrap(await api.POST('/api/v1/risk/categories', { body: input })),
  saveSettings: async ({ settings, version }: { settings: RiskSettingsValue; version: number }) =>
    unwrap(await api.PUT('/api/v1/risk/settings', { body: { settings, version } })),
  // AI: drafts and advice only (CLAUDE.md §12)
  identify: async (description: string) =>
    unwrap(await api.POST('/api/v1/risk/ai/identify', { body: { description } })),
  identifyFromDocument: async ({ file, description }: { file: File; description: string | null }) => {
    const form = new FormData();
    form.append('file', file);
    return unwrap(
      await api.POST('/api/v1/risk/ai/identify/document', {
        params: { query: description ? { description } : {} },
        body: form as unknown as { file: string },
      }),
    );
  },
  statementCheck: async (input: {
    cause: string | null;
    eventText: string | null;
    consequence: string | null;
  }) => unwrap(await api.POST('/api/v1/risk/ai/statement-check', { body: input })),
  duplicates: async ({ text, excludeId }: { text: string; excludeId: string | null }) =>
    unwrap(await api.POST('/api/v1/risk/ai/duplicates', { body: { text, excludeId } })),
  suggestions: async (riskId: string) =>
    unwrap(await api.POST('/api/v1/risk/ai/risks/{id}/suggestions', path(riskId))),
  reviewAssistant: async (riskId: string) =>
    unwrap(await api.POST('/api/v1/risk/ai/risks/{id}/review-assistant', path(riskId))),
  report: async ({ unitId, year }: { unitId: string | null; year: number | null }) =>
    unwrap(await api.POST('/api/v1/risk/ai/report', { body: { unitId, year } })),
};

export function reportUrl(kind: string, format: 'pdf' | 'xlsx', language: string, unitId?: string): string {
  const query = new URLSearchParams({ format, language, ...(unitId ? { unitId } : {}) });
  return `/api/v1/risk/reports/${encodeURIComponent(kind)}?${query.toString()}`;
}

export function mechanismPdfUrl(language: string): string {
  return `/api/v1/risk/mechanism/pdf?${new URLSearchParams({ language }).toString()}`;
}

/** Key under which the register's AI suggestions hand a chosen risk to the wizard. */
export const IDENTIFIED_RISK_KEY = 'risk.identified-draft';

export const todayIso = (): string => new Date().toISOString().slice(0, 10);
