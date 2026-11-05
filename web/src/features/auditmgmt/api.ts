import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type AuditOptions = S['AuditOptionsDto'];
export type AuditReport = S['AuditReportDto'];
export type AuditReportValues = S['AuditReportValues'];
export type AuditReportListItem = S['AuditReportListItemDto'];
export type AuditFinding = S['AuditFindingDto'];
export type AuditFindingValues = S['FindingValues'];
export type AuditFindingListItem = S['AuditFindingListItemDto'];
export type AuditRecommendation = S['AuditRecommendationDto'];
export type AuditRecommendationValues = S['RecommendationValues'];
export type AuditFollowUp = S['AuditFollowUpDto'];
export type AuditFollowUpValues = S['FollowUpValues'];
export type AuditDashboard = S['AuditDashboardDto'];
export type AuditCount = S['AuditCountDto'];
export type AuditDepartmentView = S['AuditDepartmentViewDto'];
export type AuditCalendarItem = S['AuditCalendarItemDto'];
export type AuditSettings = S['AuditMgmtSettings'];
export type AuditPerson = S['AuditPersonDto'];
export type AuditUnit = S['AuditUnitDto'];
export type AuditImportReport = S['AuditImportReportDto'];
export type FindingExtraction = S['FindingExtractionDto'];
export type ExtractedFinding = S['ExtractedFindingDto'];
export type EvidenceReview = S['EvidenceReviewDto'];
export type RepeatCheck = S['RepeatCheckDto'];
export type AuditSummaryDraft = S['AuditSummaryDraftDto'];
export type AuditEntity = S['AuditEntityDto'];
export type AuditEntityValues = S['AuditableEntityValues'];
export type AuditPlan = S['AuditPlanDto'];
export type AuditEngagement = S['AuditEngagementDto'];
export type AuditEngagementValues = S['EngagementValues'];
export type AuditEngagementListItem = S['AuditEngagementListItemDto'];
export type AuditWorkPaper = S['AuditWorkPaperDto'];
export type AuditWorkPaperValues = S['WorkPaperValues'];
export type AuditRiskOption = S['AuditRiskOptionDto'];
export type AuditControlOption = S['AuditControlOptionDto'];
export type AuditDocumentOption = S['AuditDocumentOptionDto'];
export type NewFindingInput = S['NewFindingInput'];
export type AuditFindingQuery = NonNullable<operations['ListAuditFindings']['parameters']['query']>;
export type AuditReportQuery = NonNullable<operations['ListAuditReports']['parameters']['query']>;

/** Codes shown in the UI (the message-key test proves each has a label in both languages). */
export const findingStatuses = ['open', 'late', 'closed', 'risk_accepted', 'cancelled'] as const;
export const recommendationStatuses = [
  'not_started',
  'continuing',
  'late',
  'completed',
  'risk_accepted',
  'cancelled',
] as const;
export const reportStatuses = ['draft', 'issued', 'under_follow_up', 'closed', 'cancelled'] as const;
export const findingViews = [
  'all',
  'open',
  'late',
  'closed',
  'risk-accepted',
  'mine',
  'my-unit',
  'repeat',
  'due-soon',
] as const;
export const engagementStages = ['planning', 'fieldwork', 'reporting', 'follow_up'] as const;
export const engagementAllStages = [
  'planned',
  'planning',
  'fieldwork',
  'reporting',
  'follow_up',
  'closed',
  'cancelled',
] as const;
export const engagementActions = [
  'start',
  'to-fieldwork',
  'to-reporting',
  'to-follow-up',
  'close',
  'cancel',
] as const;
export const planStatuses = ['draft', 'submitted', 'approved', 'returned'] as const;
export const workPaperStatuses = ['draft', 'prepared', 'reviewed'] as const;
export const logKinds = ['due-revision', 'risk-acceptance', 'cancellation', 'reopen'] as const;
export const reviewStatuses = ['none', 'pending', 'approved', 'returned'] as const;
export const exportKinds = ['register', 'department', 'revenue', 'committee'] as const;
export const departmentScopes = ['section', 'department'] as const;
export const evidenceSupport = ['yes', 'partly', 'no'] as const;

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const findingTone: Record<string, Tone> = {
  open: 'info',
  late: 'danger',
  closed: 'success',
  risk_accepted: 'warning',
  cancelled: 'neutral',
};

export const recommendationTone: Record<string, Tone> = {
  not_started: 'neutral',
  continuing: 'info',
  late: 'danger',
  completed: 'success',
  risk_accepted: 'warning',
  cancelled: 'neutral',
};

export const reportTone: Record<string, Tone> = {
  draft: 'neutral',
  issued: 'info',
  under_follow_up: 'info',
  closed: 'success',
  cancelled: 'neutral',
};

export const priorityTone: Record<string, Tone> = {
  critical: 'danger',
  high: 'warning',
  medium: 'info',
  low: 'neutral',
};

export const lookupTypes = {
  reportType: 'auditmgmt.report-type',
  priority: 'auditmgmt.priority',
  findingType: 'auditmgmt.finding-type',
  response: 'auditmgmt.management-response',
  entityKind: 'auditmgmt.entity-kind',
} as const;

export const auditKeys = { all: ['auditmgmt'] as const };
const base = '/api/v1/auditmgmt';
const path = (id: string) => ({ params: { path: { id } } });

export function useAuditOptions() {
  return useQuery({
    queryKey: [...auditKeys.all, 'options'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/auditmgmt/options', { signal })),
    staleTime: 60_000,
  });
}

export function useAuditDashboard(query: {
  unitId?: string;
  year?: number;
  priority?: string;
  reportType?: string;
}) {
  return useQuery({
    queryKey: [...auditKeys.all, 'dashboard', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/dashboard', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useDepartmentView() {
  return useQuery({
    queryKey: [...auditKeys.all, 'department-view'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/auditmgmt/department-view', { signal })),
  });
}

export function useAuditCalendar(query: { from: string; to: string; auditorEmployeeId?: string }) {
  return useQuery({
    queryKey: [...auditKeys.all, 'calendar', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/calendar', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useAuditReports(query: AuditReportQuery) {
  return useQuery({
    queryKey: [...auditKeys.all, 'reports', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/reports', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useAuditReport(id: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'report', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/reports/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useAuditFindings(query: AuditFindingQuery, enabled = true) {
  return useQuery({
    queryKey: [...auditKeys.all, 'findings', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/findings', { params: { query }, signal })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useAuditFinding(id: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'finding', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/findings/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useRecommendations(findingId: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'finding', findingId, 'recommendations'],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/auditmgmt/findings/{id}/recommendations', { ...path(findingId), signal }),
      ),
  });
}

export function useRecommendation(id: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'recommendation', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/recommendations/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useSimilarFindings(id: string, enabled: boolean) {
  return useQuery({
    queryKey: [...auditKeys.all, 'finding', id, 'similar'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/findings/{id}/similar', { ...path(id), signal })),
    enabled,
  });
}

export function useAuditSettings(enabled = true) {
  return useQuery({
    queryKey: [...auditKeys.all, 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/auditmgmt/settings', { signal })),
    enabled,
  });
}

export function useRiskOptions(search: string, enabled: boolean) {
  return useQuery({
    queryKey: [...auditKeys.all, 'risk-options', search],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/lookups/risks', { params: { query: { search } }, signal })),
    enabled,
  });
}

export function useControlOptions(riskId: string | undefined) {
  return useQuery({
    queryKey: [...auditKeys.all, 'control-options', riskId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/auditmgmt/lookups/risks/{riskId}/controls', {
          params: { path: { riskId: riskId ?? '' } },
          signal,
        }),
      ),
    enabled: !!riskId,
  });
}

export function useDocumentOptions(search: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'document-options', search],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/lookups/documents', { params: { query: { search } }, signal })),
    enabled: search.trim().length >= 2,
  });
}

// ---- Layer 2 ----

export function useUniverse(enabled: boolean) {
  return useQuery({
    queryKey: [...auditKeys.all, 'universe'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/auditmgmt/lifecycle/universe', { signal })),
    enabled,
  });
}

export function usePlans(enabled: boolean) {
  return useQuery({
    queryKey: [...auditKeys.all, 'plans'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/auditmgmt/lifecycle/plans', { signal })),
    enabled,
  });
}

export function usePlan(id: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'plan', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/lifecycle/plans/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useEngagement(id: string) {
  return useQuery({
    queryKey: [...auditKeys.all, 'engagement', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/auditmgmt/lifecycle/engagements/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useWorkPapers(engagementId: string, enabled: boolean) {
  return useQuery({
    queryKey: [...auditKeys.all, 'engagement', engagementId, 'work-papers'],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/auditmgmt/lifecycle/engagements/{id}/work-papers', {
          ...path(engagementId),
          signal,
        }),
      ),
    enabled,
  });
}

export function useAuditMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: auditKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['workflow'] });
    },
  });
}

/** Multipart body for the typed client (the client middleware still adds antiforgery, correlation and channel). */
function form(fields: Record<string, string | Blob | null | undefined>) {
  const data = new FormData();
  for (const [key, value] of Object.entries(fields)) {
    if (value != null) data.append(key, value);
  }

  return { body: data as never, bodySerializer: (body: unknown) => body as FormData };
}

export const auditApi = {
  createReport: async (values: AuditReportValues) =>
    unwrap(await api.POST('/api/v1/auditmgmt/reports', { body: values })),
  updateReport: async ({ id, values, version }: { id: string; values: AuditReportValues; version: number }) =>
    unwrap(await api.PUT('/api/v1/auditmgmt/reports/{id}', { ...path(id), body: { values, version } })),
  issueReport: async (id: string) => unwrap(await api.POST('/api/v1/auditmgmt/reports/{id}/issue', path(id))),
  cancelReport: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/auditmgmt/reports/{id}/cancel', { ...path(id), body: { reason } })),
  createFinding: async ({ reportId, input }: { reportId: string; input: NewFindingInput }) =>
    unwrap(await api.POST('/api/v1/auditmgmt/reports/{id}/findings', { ...path(reportId), body: input })),
  createFindings: async ({ reportId, findings }: { reportId: string; findings: NewFindingInput[] }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/reports/{id}/findings/batch', {
        ...path(reportId),
        body: { findings },
      }),
    ),
  updateFinding: async ({
    id,
    values,
    version,
  }: {
    id: string;
    values: AuditFindingValues;
    version: number;
  }) => unwrap(await api.PUT('/api/v1/auditmgmt/findings/{id}', { ...path(id), body: { values, version } })),
  cancelFinding: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/auditmgmt/findings/{id}/cancel', { ...path(id), body: { reason } })),
  linkRisk: async ({
    id,
    riskId,
    controlId,
    flagForReassessment,
    lowerControl,
  }: {
    id: string;
    riskId: string;
    controlId: string | null;
    flagForReassessment: boolean;
    lowerControl: boolean;
  }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/findings/{id}/risks', {
        ...path(id),
        body: { riskId, controlId, flagForReassessment, lowerControl },
      }),
    ),
  unlinkRisk: async ({ id, linkId }: { id: string; linkId: string }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/findings/{id}/risks/{linkId}/remove', {
        params: { path: { id, linkId } },
      }),
    ),
  sendToCap: async (id: string) =>
    unwrap(await api.POST('/api/v1/auditmgmt/findings/{id}/send-to-cap', path(id))),
  createRecommendation: async ({
    findingId,
    values,
  }: {
    findingId: string;
    values: AuditRecommendationValues;
  }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/findings/{id}/recommendations', { ...path(findingId), body: values }),
    ),
  updateRecommendation: async ({
    id,
    values,
    version,
  }: {
    id: string;
    values: AuditRecommendationValues;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/auditmgmt/recommendations/{id}', { ...path(id), body: { values, version } }),
    ),
  revise: async ({ id, newDueOn, reason }: { id: string; newDueOn: string; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/recommendations/{id}/revise', {
        ...path(id),
        body: { newDueOn, reason },
      }),
    ),
  acceptRisk: async ({
    id,
    justification,
    reference,
  }: {
    id: string;
    justification: string;
    reference: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/recommendations/{id}/accept-risk', {
        ...path(id),
        body: { justification, reference },
      }),
    ),
  reopen: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/recommendations/{id}/reopen', { ...path(id), body: { reason } }),
    ),
  cancelRecommendation: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/recommendations/{id}/cancel', { ...path(id), body: { reason } }),
    ),
  recordFollowUp: async ({ id, values }: { id: string; values: AuditFollowUpValues }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/recommendations/{id}/follow-ups', { ...path(id), body: values }),
    ),
  saveSettings: async ({ settings, version }: { settings: AuditSettings; version: number }) =>
    unwrap(await api.PUT('/api/v1/auditmgmt/settings', { body: { settings, version } })),
  importFindings: async ({
    reportId,
    file,
    mode,
  }: {
    reportId: string;
    file: File;
    mode: 'validate' | 'commit';
  }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/reports/{id}/import', {
        params: { path: { id: reportId }, query: { mode } },
        ...form({ file }),
      }),
    ),
  importReport: async ({ reportId, file }: { reportId: string; file: File }) => {
    const { data } = await api.POST('/api/v1/auditmgmt/reports/{id}/import/report', {
      ...path(reportId),
      ...form({ file }),
      parseAs: 'blob',
    });
    if (data) save(data, 'audit-findings-validation-report.xlsx');
  },
  extract: async ({ reportId, text }: { reportId: string; text: string }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/ai/reports/{id}/extract', { ...path(reportId), body: { text } }),
    ),
  extractDocument: async ({ reportId, file }: { reportId: string; file: File }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/ai/reports/{id}/extract/document', {
        ...path(reportId),
        ...form({ file }),
      }),
    ),
  evidenceReview: async ({
    id,
    pct,
    actionsImplemented,
    evidenceText,
    file,
  }: {
    id: string;
    pct: number;
    actionsImplemented: string;
    evidenceText: string;
    file?: File | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/ai/recommendations/{id}/evidence-review', {
        ...path(id),
        ...form({ pct: String(pct), actionsImplemented, evidenceText, file }),
      }),
    ),
  repeatCheck: async (id: string) =>
    unwrap(await api.POST('/api/v1/auditmgmt/ai/findings/{id}/repeat', path(id))),
  summary: async (query: { unitId: string | null; year: number | null }) =>
    unwrap(await api.POST('/api/v1/auditmgmt/ai/summary', { body: query })),
  // Layer 2
  syncUniverse: async () => unwrap(await api.POST('/api/v1/auditmgmt/lifecycle/universe/sync')),
  createEntity: async (values: AuditEntityValues) =>
    unwrap(await api.POST('/api/v1/auditmgmt/lifecycle/universe', { body: values })),
  createPlan: async (input: { year: number; titleAr: string | null; titleEn: string | null }) =>
    unwrap(await api.POST('/api/v1/auditmgmt/lifecycle/plans', { body: input })),
  submitPlan: async ({ id, reason }: { id: string; reason: string | null }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/lifecycle/plans/{id}/submit', { ...path(id), body: { reason } }),
    ),
  createEngagement: async ({ planId, values }: { planId: string | null; values: AuditEngagementValues }) =>
    unwrap(await api.POST('/api/v1/auditmgmt/lifecycle/engagements', { body: { planId, values } })),
  updateEngagement: async ({
    id,
    values,
    version,
  }: {
    id: string;
    values: AuditEngagementValues;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/auditmgmt/lifecycle/engagements/{id}', {
        ...path(id),
        body: { values, version },
      }),
    ),
  transition: async ({ id, action, reason }: { id: string; action: string; reason: string | null }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/lifecycle/engagements/{id}/transition', {
        ...path(id),
        body: { action, reason },
      }),
    ),
  survey: async ({ id, score, comment }: { id: string; score: number; comment: string | null }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/lifecycle/engagements/{id}/survey', {
        ...path(id),
        body: { score, comment },
      }),
    ),
  createWorkPaper: async ({ engagementId, values }: { engagementId: string; values: AuditWorkPaperValues }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/lifecycle/engagements/{id}/work-papers', {
        ...path(engagementId),
        body: values,
      }),
    ),
  workPaperAction: async ({
    engagementId,
    paperId,
    action,
  }: {
    engagementId: string;
    paperId: string;
    action: string;
  }) =>
    unwrap(
      await api.POST('/api/v1/auditmgmt/lifecycle/engagements/{id}/work-papers/{paperId}/action', {
        params: { path: { id: engagementId, paperId } },
        body: { action },
      }),
    ),
};

function save(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

/** Downloads a bilingual report (PDF / Excel); the server checks the export permission, the scope and audits the export. */
export async function downloadExport(
  kind: (typeof exportKinds)[number],
  input: {
    format: 'pdf' | 'xlsx';
    language: string;
    reportId?: string | null;
    unitId?: string | null;
    year?: number | null;
    summaryAr?: string | null;
    summaryEn?: string | null;
  },
) {
  const { data } = await api.POST('/api/v1/auditmgmt/exports/{kind}', {
    params: { path: { kind } },
    body: {
      format: input.format,
      language: input.language,
      reportId: input.reportId ?? null,
      unitId: input.unitId ?? null,
      year: input.year ?? null,
      summaryAr: input.summaryAr ?? null,
      summaryEn: input.summaryEn ?? null,
    },
    parseAs: 'blob',
  });
  if (data) save(data, `audit-${kind}.${input.format}`);
}

export const auditTemplateUrl = `${base}/import/template`;
