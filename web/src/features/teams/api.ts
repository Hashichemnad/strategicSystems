import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type TeamListItem = S['TeamListItemDto'];
export type Team = S['TeamDto'];
export type TeamMember = S['MemberDto'];
export type TeamInput = S['TeamInput'];
export type MemberInput = S['MemberInput'];
export type TeamOptions = S['TeamOptionsDto'];
export type TeamType = S['TeamTypeDto'];
export type MemberRole = S['MemberRoleDto'];
export type Meeting = S['MeetingDto'];
export type Recommendation = S['RecommendationDto'];
export type TeamReport = S['ReportDto'];
export type EvaluationSummary = S['EvaluationSummaryDto'];
export type Evaluation = S['EvaluationDto'];
export type MemberEvaluation = S['MemberEvaluationDto'];
export type CriterionResult = S['CriterionResult'];
export type TemplateContent = S['TemplateContent'];
export type Membership = S['MembershipDto'];
export type LinkedTeams = S['LinkedTeamsDto'];
export type TeamDashboard = S['TeamDashboardDto'];
export type TeamConfiguration = S['TeamConfigurationDto'];
export type TeamSettingsValue = S['TeamSettings'];
export type TemplateDto = S['EvaluationTemplateDto'];
export type RewardMatrixDto = S['RewardMatrixDto'];
export type RewardMatrixContent = S['RewardMatrixContent'];
export type MinutesDraft = S['MinutesDraftDto'];
export type FormationDraft = S['FormationDraftDto'];
export type MemberSuggestion = S['MemberSuggestionDto'];
export type EvaluationAssistant = S['EvaluationAssistantDto'];
export type TeamListQuery = NonNullable<operations['ListTeams']['parameters']['query']>;

export const teamStatuses = [
  'draft',
  'pending_approval',
  'active',
  'inactive',
  'pending_evaluation',
  'evaluation_waiting_approval',
  'closed',
  'replaced',
  'cancelled',
] as const;
export type TeamStatus = (typeof teamStatuses)[number];

export const teamKeys = {
  all: ['teams'] as const,
  list: (query: TeamListQuery) => [...teamKeys.all, 'list', query] as const,
  team: (id: string) => [...teamKeys.all, 'team', id] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

export function useTeams(query: TeamListQuery) {
  return useQuery({
    queryKey: teamKeys.list(query),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/teams', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useTeamCounts() {
  return useQuery({
    queryKey: [...teamKeys.all, 'counts'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/teams/counts', { signal })),
  });
}

export function useTeamOptions() {
  return useQuery({
    queryKey: [...teamKeys.all, 'options'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/teams/options', { signal })),
    staleTime: 60_000,
  });
}

export function useTeam(id: string | undefined) {
  return useQuery({
    queryKey: teamKeys.team(id ?? ''),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/teams/{id}', { ...path(id ?? ''), signal })),
    enabled: !!id,
  });
}

export function useTeamPart(
  id: string,
  part: 'members' | 'meetings' | 'recommendations' | 'reports' | 'evaluations',
  history = false,
) {
  return useQuery({
    queryKey: [...teamKeys.team(id), part, history],
    queryFn: async ({ signal }) => {
      switch (part) {
        case 'members':
          return unwrap(
            await api.GET('/api/v1/teams/{id}/members', {
              params: { path: { id }, query: { history } },
              signal,
            }),
          );
        case 'meetings':
          return unwrap(await api.GET('/api/v1/teams/{id}/meetings', { ...path(id), signal }));
        case 'recommendations':
          return unwrap(await api.GET('/api/v1/teams/{id}/recommendations', { ...path(id), signal }));
        case 'reports':
          return unwrap(await api.GET('/api/v1/teams/{id}/reports', { ...path(id), signal }));
        default:
          return unwrap(await api.GET('/api/v1/teams/{id}/evaluations', { ...path(id), signal }));
      }
    },
  });
}

export function useMembers(id: string, history = false) {
  return useTeamPart(id, 'members', history) as ReturnType<typeof useQuery<TeamMember[]>>;
}

export function useMeetings(id: string) {
  return useTeamPart(id, 'meetings') as ReturnType<typeof useQuery<Meeting[]>>;
}

export function useRecommendations(id: string) {
  return useTeamPart(id, 'recommendations') as ReturnType<typeof useQuery<Recommendation[]>>;
}

export function useReports(id: string) {
  return useTeamPart(id, 'reports') as ReturnType<typeof useQuery<TeamReport[]>>;
}

export function useEvaluations(id: string) {
  return useTeamPart(id, 'evaluations') as ReturnType<typeof useQuery<EvaluationSummary[]>>;
}

export function useMeeting(id: string) {
  return useQuery({
    queryKey: [...teamKeys.all, 'meeting', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/teams/meetings/{id}', { ...path(id), signal })),
  });
}

export function useRecommendation(id: string) {
  return useQuery({
    queryKey: [...teamKeys.all, 'recommendation', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/teams/recommendations/{id}', { ...path(id), signal })),
  });
}

export function useEvaluation(id: string) {
  return useQuery({
    queryKey: [...teamKeys.all, 'evaluation', id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/teams/evaluations/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useMemberships(employeeId: string) {
  return useQuery({
    queryKey: [...teamKeys.all, 'memberships', employeeId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/teams/employees/{employeeId}/memberships', {
          params: { path: { employeeId } },
          signal,
        }),
      ),
    retry: false,
  });
}

export function useLinkedTeams(type: string, id: string) {
  return useQuery({
    queryKey: [...teamKeys.all, 'linked', type, id],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/teams/linked', { params: { query: { type, id } }, signal })),
  });
}

export function useTeamDashboard(range?: { from: string; to: string }) {
  return useQuery({
    queryKey: [...teamKeys.all, 'dashboard', range ?? null],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/teams/dashboard', { params: { query: range ?? {} }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useTeamConfiguration() {
  return useQuery({
    queryKey: [...teamKeys.all, 'configuration'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/teams/settings', { signal })),
  });
}

/** A mutation that refreshes every Teams query afterwards (lists, team page, evaluations…). */
export function useTeamMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: teamKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['workflow'] });
    },
  });
}

export const teamApi = {
  create: async (body: TeamInput) => unwrap(await api.POST('/api/v1/teams', { body })),
  update: async ({ id, team, version }: { id: string; team: TeamInput; version: number }) =>
    unwrap(await api.PUT('/api/v1/teams/{id}', { ...path(id), body: { team, version } })),
  submit: async ({ id, version, comment }: { id: string; version: number; comment?: string | null }) =>
    unwrap(
      await api.POST('/api/v1/teams/{id}/submit', {
        ...path(id),
        body: { version, comment: comment ?? null, reason: null },
      }),
    ),
  status: async ({
    id,
    action,
    version,
    reason,
  }: {
    id: string;
    action: 'cancel' | 'deactivate' | 'reactivate' | 'close';
    version: number;
    reason?: string | null;
  }) => {
    const body = { version, reason: reason ?? null, comment: null };
    switch (action) {
      case 'cancel':
        return unwrap(await api.POST('/api/v1/teams/{id}/cancel', { ...path(id), body }));
      case 'deactivate':
        return unwrap(await api.POST('/api/v1/teams/{id}/deactivate', { ...path(id), body }));
      case 'reactivate':
        return unwrap(await api.POST('/api/v1/teams/{id}/reactivate', { ...path(id), body }));
      default:
        return unwrap(await api.POST('/api/v1/teams/{id}/close', { ...path(id), body }));
    }
  },
  externalMemo: async (input: {
    id: string;
    number: string;
    date: string;
    fileId: string;
    version: number;
  }) => unwrap(await api.POST('/api/v1/teams/{id}/memo', { ...path(input.id), body: input })),
  regenerateMemo: async (id: string) =>
    unwrap(await api.POST('/api/v1/teams/{id}/memo/regenerate', path(id))),
  link: async ({ id, type, recordId }: { id: string; type: string; recordId: string }) =>
    unwrap(
      await api.POST('/api/v1/teams/{id}/links', { ...path(id), body: { type, recordId, reason: null } }),
    ),
  addMember: async ({ id, member }: { id: string; member: MemberInput }) =>
    unwrap(await api.POST('/api/v1/teams/{id}/members', { ...path(id), body: member })),
  updateMember: async (input: {
    id: string;
    memberId: string;
    roleCode: string;
    responsibilities: string | null;
    requiredSkills: string | null;
    allocationPercent: number | null;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/teams/{id}/members/{memberId}', {
        params: { path: { id: input.id, memberId: input.memberId } },
        body: input,
      }),
    ),
  endMember: async (input: {
    id: string;
    memberId: string;
    on: string | null;
    reason: string;
    successor: MemberInput | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/teams/{id}/members/{memberId}/end', {
        params: { path: { id: input.id, memberId: input.memberId } },
        body: input,
      }),
    ),
  createMeeting: async (input: {
    teamId: string;
    title: string;
    scheduledAt: string;
    endsAt: string;
    location: string | null;
    agenda: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/teams/{id}/meetings', {
        ...path(input.teamId),
        body: { ...input, version: 0 },
      }),
    ),
  saveMinutes: async (input: {
    id: string;
    minutes: string | null;
    decisions: string | null;
    attendance: { memberId: string; status: string; note: string | null }[] | null;
    aiDrafted: boolean;
    version: number;
  }) => unwrap(await api.PUT('/api/v1/teams/meetings/{id}/minutes', { ...path(input.id), body: input })),
  submitMinutes: async ({ id, version }: { id: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/teams/meetings/{id}/submit-minutes', {
        ...path(id),
        body: { version, comment: null, reason: null },
      }),
    ),
  createRecommendation: async (input: {
    teamId: string;
    meetingId: string | null;
    kind: string | null;
    text: string;
    ownerEmployeeId: string | null;
    dueDate: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/teams/{id}/recommendations', {
        ...path(input.teamId),
        body: { ...input, version: 0 },
      }),
    ),
  setRecommendationStatus: async (input: {
    id: string;
    status: string;
    note: string | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/teams/recommendations/{id}/status', {
        ...path(input.id),
        body: { ...input, evidenceFileIds: null },
      }),
    ),
  submitReport: async ({ id, summary, version }: { id: string; summary: string; version: number }) =>
    unwrap(await api.POST('/api/v1/teams/reports/{id}/submit', { ...path(id), body: { summary, version } })),
  openEvaluation: async ({ teamId, year }: { teamId: string; year: number | null }) =>
    unwrap(await api.POST('/api/v1/teams/{id}/evaluations', { ...path(teamId), body: { year } })),
  saveEvaluation: async (input: {
    id: string;
    criteria:
      | { code: string; level: string | null; comment: string | null; overrideJustification: string | null }[]
      | null;
    workload: string | null;
    evidenceFileIds: string[] | null;
    members: { id: string; answers: Record<string, number> | null; comment: string | null }[] | null;
    version: number;
  }) => unwrap(await api.PUT('/api/v1/teams/evaluations/{id}', { ...path(input.id), body: input })),
  submitEvaluation: async ({
    id,
    version,
    comment,
  }: {
    id: string;
    version: number;
    comment: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/teams/evaluations/{id}/submit', {
        ...path(id),
        body: { version, comment, reason: null },
      }),
    ),
  saveRewards: async (input: {
    id: string;
    rewards: {
      id: string;
      approvedReward: number;
      adjustmentReason: string | null;
      certificate: boolean;
      financialReward: boolean;
    }[];
    version: number;
  }) => unwrap(await api.PUT('/api/v1/teams/evaluations/{id}/rewards', { ...path(input.id), body: input })),
  submitRewards: async ({ id, version }: { id: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/teams/evaluations/{id}/rewards/submit', {
        ...path(id),
        body: { version, comment: null, reason: null },
      }),
    ),
  aiMinutes: async ({ id, notes }: { id: string; notes: string }) =>
    unwrap(await api.POST('/api/v1/teams/meetings/{id}/ai/minutes', { ...path(id), body: { notes } })),
  aiFormation: async (body: S['FormationAiInput']) =>
    unwrap(await api.POST('/api/v1/teams/ai/formation', { body })),
  aiMembers: async (body: S['MembersAiInput']) =>
    unwrap(await api.POST('/api/v1/teams/ai/members', { body })),
  aiEvaluation: async (id: string) =>
    unwrap(await api.POST('/api/v1/teams/evaluations/{id}/ai/summary', path(id))),
  saveSettings: async (body: S['SettingsInput']) => unwrap(await api.PUT('/api/v1/teams/settings', { body })),
  saveType: async (body: S['TeamTypeInput']) =>
    unwrap(await api.POST('/api/v1/teams/settings/types', { body })),
  saveRole: async (body: S['MemberRoleInput']) =>
    unwrap(await api.POST('/api/v1/teams/settings/roles', { body })),
  saveTemplate: async (body: S['EvaluationTemplateInput']) =>
    unwrap(await api.POST('/api/v1/teams/settings/templates', { body })),
  publishTemplate: async (id: string) =>
    unwrap(await api.POST('/api/v1/teams/settings/templates/{id}/publish', path(id))),
  saveMatrix: async (body: S['RewardMatrixInput']) =>
    unwrap(await api.POST('/api/v1/teams/settings/reward-matrices', { body })),
  publishMatrix: async (id: string) =>
    unwrap(await api.POST('/api/v1/teams/settings/reward-matrices/{id}/publish', path(id))),
};

export function exportUrl(kind: string, format: 'xlsx' | 'pdf', language: string): string {
  return `/api/v1/teams/export?kind=${kind}&format=${format}&language=${language}`;
}

export const statusTone: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  draft: 'neutral',
  pending_approval: 'info',
  active: 'success',
  inactive: 'warning',
  pending_evaluation: 'warning',
  evaluation_waiting_approval: 'info',
  closed: 'neutral',
  replaced: 'neutral',
  cancelled: 'danger',
};
