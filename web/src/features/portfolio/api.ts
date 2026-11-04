import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type ProgramRequest = S['RequestDto'];
export type RequestValues = S['RequestValues'];
export type RequestListItem = S['RequestListItemDto'];
export type RequestFile = S['RequestFileDto'];
export type PortfolioOptions = S['PortfolioOptionsDto'];
export type PortfolioSettingsValue = S['PortfolioSettings'];
export type Project = S['ProjectFullDto'];
export type ProjectListItem = S['ProjectListItemDto'];
export type Assessment = S['AssessmentDto'];
export type RequestListQuery = NonNullable<operations['ListProgramRequests']['parameters']['query']>;
export type ProjectListQuery = NonNullable<operations['ListPortfolioProjects']['parameters']['query']>;

export const requestStatuses = ['draft', 'in-approval', 'approved', 'rejected', 'cancelled'] as const;
export type RequestStatus = (typeof requestStatuses)[number];

export const statusTone: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  draft: 'neutral',
  'in-approval': 'info',
  approved: 'success',
  rejected: 'danger',
  cancelled: 'neutral',
};

export const bandTone: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  high: 'danger',
  medium: 'warning',
  low: 'neutral',
};

/** The request's resource type for the shared engine (approvals, status, links, files). */
export const requestResource = 'portfolio.request';
export const projectResource = 'portfolio.project';
export const requestAiFormKey = 'portfolio.request';

export const portfolioKeys = {
  all: ['portfolio'] as const,
  options: ['portfolio', 'options'] as const,
  requests: (query: RequestListQuery) => ['portfolio', 'requests', query] as const,
  request: (id: string) => ['portfolio', 'request', id] as const,
  files: (id: string) => ['portfolio', 'request', id, 'files'] as const,
  assessments: (id: string) => ['portfolio', 'request', id, 'assessments'] as const,
  projects: (query: ProjectListQuery) => ['portfolio', 'projects', query] as const,
  project: (id: string) => ['portfolio', 'project', id] as const,
  settings: ['portfolio', 'settings'] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

export function usePortfolioOptions() {
  return useQuery({
    queryKey: portfolioKeys.options,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/portfolio/options', { signal })),
    staleTime: 60_000,
  });
}

export function useRequests(query: RequestListQuery) {
  return useQuery({
    queryKey: portfolioKeys.requests(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/requests', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useRequest(id: string | undefined) {
  return useQuery({
    queryKey: portfolioKeys.request(id ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/requests/{id}', { ...path(id ?? ''), signal })),
    enabled: Boolean(id),
  });
}

export function useRequestFiles(id: string) {
  return useQuery({
    queryKey: portfolioKeys.files(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/requests/{id}/files', { ...path(id), signal })),
  });
}

export function useAssessments(id: string) {
  return useQuery({
    queryKey: portfolioKeys.assessments(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/requests/{id}/assessments', { ...path(id), signal })),
  });
}

export function useProjects(query: ProjectListQuery) {
  return useQuery({
    queryKey: portfolioKeys.projects(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useProject(id: string) {
  return useQuery({
    queryKey: portfolioKeys.project(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}', { ...path(id), signal })),
  });
}

export function usePortfolioSettings() {
  return useQuery({
    queryKey: portfolioKeys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/portfolio/settings', { signal })),
  });
}

/** Every request command; the record, its files, assessments and status are refreshed afterwards. */
export const requestApi = {
  create: async (body: { values: RequestValues; source?: string | null }) =>
    unwrap(await api.POST('/api/v1/portfolio/requests', { body })),
  update: async ({ id, values, version }: { id: string; values: RequestValues; version: number }) =>
    unwrap(await api.PUT('/api/v1/portfolio/requests/{id}', { ...path(id), body: { values, version } })),
  submit: async ({ id, version, comment }: { id: string; version: number; comment?: string | null }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/submit', {
        ...path(id),
        body: { version, comment: comment ?? null },
        headers: { 'Idempotency-Key': crypto.randomUUID() },
      }),
    ),
  cancel: async ({ id, version, reason }: { id: string; version: number; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/cancel', { ...path(id), body: { reason, version } }),
    ),
  classify: async ({
    id,
    version,
    value,
    reason,
  }: {
    id: string;
    version: number;
    value: string | null;
    reason: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/classification', {
        ...path(id),
        body: { value, reason, version },
      }),
    ),
  approvedBudget: async ({ id, version, amount }: { id: string; version: number; amount: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/approved-budget', {
        ...path(id),
        body: { amount, version },
      }),
    ),
  handOff: async ({ id, version }: { id: string; version: number }) =>
    unwrap(await api.POST('/api/v1/portfolio/requests/{id}/hand-off', { ...path(id), body: { version } })),
  quotationsUploaded: async ({ id, version }: { id: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/quotations-uploaded', {
        ...path(id),
        body: { version },
      }),
    ),
  upload: async ({ id, kind, file }: { id: string; kind: string; file: File }) => {
    const form = new FormData();
    form.append('file', file);
    return unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/files/{kind}', {
        params: { path: { id, kind } },
        body: form as unknown as { file: string; comment?: string },
        bodySerializer: (body: unknown) => body as FormData,
      }),
    );
  },
  assess: async ({ id }: { id: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/requests/{id}/assessments', { ...path(id) })),
};

export function useRequestMutation<TArgs extends { id: string }, TResult>(
  fn: (args: TArgs) => Promise<TResult>,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async (_result, args) => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: portfolioKeys.request(args.id) }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'requests'] }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
      ]);
    },
  });
}

export function useCreateRequest() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: requestApi.create,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['portfolio', 'requests'] });
    },
  });
}

export function useSavePortfolioSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { settings: PortfolioSettingsValue; version: number }) =>
      unwrap(await api.PUT('/api/v1/portfolio/settings', { body })),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: portfolioKeys.all });
    },
  });
}

export function useUploadTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return unwrap(
        await api.POST('/api/v1/portfolio/settings/template', {
          body: form as unknown as { file: string },
          bodySerializer: (body: unknown) => body as FormData,
        }),
      );
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: portfolioKeys.all });
    },
  });
}

/** Sum of the Year 1–4 costs (two decimals, avoiding floating noise); null when none is entered. */
export function yearTotal(
  values: Pick<RequestValues, 'year1Cost' | 'year2Cost' | 'year3Cost' | 'year4Cost'>,
): number | null {
  const parts = [values.year1Cost, values.year2Cost, values.year3Cost, values.year4Cost];
  if (parts.every((p) => p === null || p === undefined)) return null;
  return Math.round(parts.reduce<number>((sum, p) => sum + Math.round((p ?? 0) * 100), 0)) / 100;
}

/** Evaluation score (ratio) the way the server computes it, for the live indicator; null until every criterion is scored. */
export function liveScore(
  scores: Record<string, number> | null | undefined,
  criteria: readonly { code: string; weight: number; active?: boolean }[],
): number | null {
  const active = criteria.filter((c) => c.active !== false && c.weight > 0);
  if (active.length === 0 || !scores) return null;
  let weighted = 0;
  let weights = 0;
  for (const c of active) {
    const s = scores[c.code];
    if (s === undefined || s < 1 || s > 5) return null;
    weighted += s * c.weight;
    weights += c.weight;
  }
  return Math.round((weighted / weights / 5) * 10000) / 10000;
}

export function bandOf(
  score: number | null,
  bands: readonly { code: string; minimum: number }[],
): string | null {
  if (score === null) return null;
  return [...bands].sort((a, b) => b.minimum - a.minimum).find((b) => b.minimum <= score)?.code ?? null;
}

/** Q-P5 as on the server: every screening answer Yes and the budget above the threshold. */
export function liveClassification(values: RequestValues, threshold: number): 'project' | 'initiative' {
  const answers = [
    values.screenCoreWork,
    values.screenOneTime,
    values.screenTimeBound,
    values.screenUniqueOutput,
    values.screenMinCost,
    values.screenOperationalCosts,
  ];
  const allYes = answers.every((a) => a === true);
  const over = values.requiresBudget === true && (values.estimatedBudget ?? 0) > threshold;
  return allYes && over ? 'project' : 'initiative';
}
