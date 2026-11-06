import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type BenchOptions = S['BenchOptionsDto'];
export type BenchStudy = S['BenchStudyDto'];
export type BenchStudyListItem = S['BenchStudyListItemDto'];
export type BenchCandidate = S['BenchCandidateDto'];
export type BenchCandidateInput = S['BenchCandidateInput'];
export type BenchVisit = S['BenchVisitDto'];
export type BenchVisitInput = S['BenchVisitInput'];
export type BenchAnalysis = S['BenchAnalysisDto'];
export type BenchAnalysisInput = S['BenchAnalysisInput'];
export type BenchPracticeInput = S['BenchPracticeInput'];
export type BenchIndicatorInput = S['BenchIndicatorInput'];
export type BenchPlanItemInput = S['BenchPlanItemInput'];
export type BenchComparator = S['BenchComparatorDto'];
export type ComparatorValues = S['ComparatorValues'];
export type BenchBestPractice = S['BenchBestPracticeDto'];
export type BenchPracticeValues = S['BenchPracticeValues'];
export type BenchDashboard = S['BenchDashboardDto'];
export type BenchPlan = S['BenchPlanDto'];
export type BenchSettings = S['BenchSettings'];
export type BenchPerson = S['BenchPersonDto'];
export type BenchUnit = S['BenchUnitDto'];
export type BenchImpact = S['BenchImpactDto'];
export type CapSourceProgress = S['CapSourceProgress'];
export type ComparatorSuggestion = S['ComparatorSuggestionDto'];
export type QuestionnaireDraft = S['QuestionnaireDraftDto'];
export type VisitAnalysis = S['VisitAnalysisDto'];
export type StudySummary = S['StudySummaryDto'];
export type PracticeSearch = S['PracticeSearchDto'];
export type BenchStudyQuery = NonNullable<operations['ListBenchStudies']['parameters']['query']>;

/** Codes shown in the UI (the message-key test proves each has a label in both languages). */
export const studyStages = ['plan', 'search', 'acquire', 'analysis', 'completed'] as const;
export const studyStatuses = ['active', 'completed', 'cancelled'] as const;
export const studyViews = ['all', 'mine', 'my-unit', 'pending', 'active'] as const;
export const comparatorScopes = ['local', 'regional', 'international'] as const;
export const visitMethods = ['site-visit', 'virtual', 'questionnaire'] as const;
export const practiceCategories = ['compared', 'not-applicable', 'outside-scope'] as const;
export const outreachChannels = ['email', 'phone', 'meeting', 'letter', 'other'] as const;
export const outreachResponses = ['pending', 'accepted', 'declined', 'no-reply'] as const;
export const benchProcesses = ['bench.plan', 'bench.search', 'bench.analysis', 'bench.closure'] as const;
export const benchLookups = {
  comparisonType: 'bench.comparison-type',
  purpose: 'bench.purpose',
  sector: 'bench.sector',
} as const;

export const benchKeys = { all: ['bench'] as const };

const path = (id: string) => ({ params: { path: { id } } });

export function useBenchOptions() {
  return useQuery({
    queryKey: [...benchKeys.all, 'options'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/bench/options', { signal })),
    staleTime: 60_000,
  });
}

export function useBenchDashboard(query: { year?: number; unitId?: string; from?: string; to?: string }) {
  return useQuery({
    queryKey: [...benchKeys.all, 'dashboard', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/dashboard', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useBenchPlan(year: number) {
  return useQuery({
    queryKey: [...benchKeys.all, 'plan', year],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/plan', { params: { query: { year } }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useBenchStudies(query: BenchStudyQuery) {
  return useQuery({
    queryKey: [...benchKeys.all, 'studies', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useBenchStudy(id: string) {
  return useQuery({
    queryKey: [...benchKeys.all, 'study', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useStudySearch(id: string) {
  return useQuery({
    queryKey: [...benchKeys.all, 'study', id, 'search'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies/{id}/search', { ...path(id), signal })),
  });
}

export function useStudyVisits(id: string) {
  return useQuery({
    queryKey: [...benchKeys.all, 'study', id, 'visits'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies/{id}/visits', { ...path(id), signal })),
  });
}

export function useStudyAnalysis(id: string) {
  return useQuery({
    queryKey: [...benchKeys.all, 'study', id, 'analysis'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies/{id}/analysis', { ...path(id), signal })),
  });
}

export function useStudyActions(id: string, enabled: boolean) {
  return useQuery({
    queryKey: [...benchKeys.all, 'study', id, 'actions'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies/{id}/actions', { ...path(id), signal })),
    enabled,
  });
}

export function useStudyImpact(id: string) {
  return useQuery({
    queryKey: [...benchKeys.all, 'study', id, 'impact'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/studies/{id}/impact', { ...path(id), signal })),
  });
}

export function useComparators(query: {
  search?: string;
  scope?: string;
  sector?: string;
  page?: number;
  pageSize?: number;
}) {
  return useQuery({
    queryKey: [...benchKeys.all, 'comparators', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/comparators', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useComparator(id: string | undefined) {
  return useQuery({
    queryKey: [...benchKeys.all, 'comparator', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/comparators/{id}', { ...path(id ?? ''), signal })),
    enabled: !!id,
  });
}

export function usePractices(query: {
  search?: string;
  topic?: string;
  sector?: string;
  page?: number;
  pageSize?: number;
}) {
  return useQuery({
    queryKey: [...benchKeys.all, 'practices', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/bench/practices', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useBenchSettings() {
  return useQuery({
    queryKey: [...benchKeys.all, 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/bench/settings', { signal })),
  });
}

export function useBenchMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: benchKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['workflow'] });
    },
  });
}

export const benchApi = {
  circulate: async (year: number) =>
    unwrap(await api.POST('/api/v1/bench/plan/{year}/circulate', { params: { path: { year } } })),
  saveSearch: async ({ id, candidates }: { id: string; candidates: BenchCandidateInput[] }) =>
    unwrap(await api.PUT('/api/v1/bench/studies/{id}/search', { ...path(id), body: { candidates } })),
  submitSearch: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/search/submit', { ...path(id), body: { comment } })),
  addOutreach: async ({ id, ...body }: { id: string } & S['BenchOutreachInput']) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/outreach', { ...path(id), body })),
  rate: async ({ id, ...body }: { id: string } & S['BenchRatingInput']) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/ratings', { ...path(id), body })),
  saveVisits: async ({ id, visits }: { id: string; visits: BenchVisitInput[] }) =>
    unwrap(await api.PUT('/api/v1/bench/studies/{id}/visits', { ...path(id), body: { visits } })),
  sendToAnalysis: async (id: string) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/send-to-analysis', path(id))),
  saveAnalysis: async ({ id, analysis }: { id: string; analysis: BenchAnalysisInput }) =>
    unwrap(await api.PUT('/api/v1/bench/studies/{id}/analysis', { ...path(id), body: analysis })),
  submitAnalysis: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/analysis/submit', { ...path(id), body: { comment } })),
  requestClosure: async ({ id, comment }: { id: string; comment: string | null }) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/request-closure', { ...path(id), body: { comment } })),
  cancel: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/cancel', { ...path(id), body: { reason } })),
  linkTeam: async ({ id, teamId }: { id: string; teamId: string | null }) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/team', { ...path(id), body: { teamId } })),
  recordImpact: async ({ id, ...body }: { id: string } & S['BenchImpactInput']) =>
    unwrap(await api.POST('/api/v1/bench/studies/{id}/impact', { ...path(id), body })),
  createComparator: async (values: ComparatorValues) =>
    unwrap(await api.POST('/api/v1/bench/comparators', { body: { values, version: null } })),
  updateComparator: async ({
    id,
    values,
    version,
  }: {
    id: string;
    values: ComparatorValues;
    version: number;
  }) => unwrap(await api.PUT('/api/v1/bench/comparators/{id}', { ...path(id), body: { values, version } })),
  createPractice: async (values: BenchPracticeValues) =>
    unwrap(await api.POST('/api/v1/bench/practices', { body: { values, version: null } })),
  updatePractice: async ({
    id,
    values,
    version,
  }: {
    id: string;
    values: BenchPracticeValues;
    version: number;
  }) => unwrap(await api.PUT('/api/v1/bench/practices/{id}', { ...path(id), body: { values, version } })),
  saveSettings: async ({ settings, version }: { settings: BenchSettings; version: number }) =>
    unwrap(await api.PUT('/api/v1/bench/settings', { body: { settings, version } })),
  aiComparators: async (id: string) =>
    unwrap(await api.POST('/api/v1/bench/ai/studies/{id}/comparators', path(id))),
  aiQuestionnaire: async ({ id, comparatorId }: { id: string; comparatorId: string | null }) =>
    unwrap(
      await api.POST('/api/v1/bench/ai/studies/{id}/questionnaire', { ...path(id), body: { comparatorId } }),
    ),
  aiAnalysis: async ({ id, notes }: { id: string; notes: string | null }) =>
    unwrap(await api.POST('/api/v1/bench/ai/studies/{id}/analysis', { ...path(id), body: { notes } })),
  aiSummary: async (id: string) => unwrap(await api.POST('/api/v1/bench/ai/studies/{id}/summary', path(id))),
  aiSearch: async (question: string) =>
    unwrap(await api.POST('/api/v1/bench/ai/practices/search', { body: { question } })),
};
