import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

type S = components['schemas'];
export type ProjectFull = S['ProjectFullDto'];
export type CharterValues = S['ProjectCharterValues'];
export type StageTemplateValue = S['StageTemplate'];
export type GateItem = S['GateItem'];
export type ProjectKpi = S['ProjectKpiDto'];
export type ProjectRisk = S['ProjectRiskDto'];
export type Stakeholder = S['StakeholderDto'];
export type ProposedMember = S['ProposedMemberDto'];
export type PlanningItem = S['PlanningItemDto'];
export type Challenge = S['ChallengeDto'];
export type Lesson = S['LessonDto'];
export type ProjectMeeting = S['ProjectMeetingDto'];
export type Contract = S['ContractDto'];
export type Milestone = S['MilestoneDto'];
export type MilestonePlan = S['MilestonePlanDto'];
export type MilestoneValues = S['MilestoneValues'];
export type MilestoneProgressValues = S['MilestoneProgressValues'];
export type MonthlyView = S['MonthlyViewDto'];
export type MonthlyUpdate = S['MonthlyUpdateDto'];
export type ChangeRequest = S['ChangeRequestDto'];
export type ChangeRequestValues = S['ChangeRequestValues'];
export type ChangeApplyValues = S['ChangeApplyValues'];
export type ProjectFile = S['ProjectFileDto'];

/** Project rows with their list / create / update routes (docs/design/portfolio.md §26). */
export const itemKinds = [
  'kpis',
  'risks',
  'stakeholders',
  'members',
  'planning-items',
  'challenges',
  'lessons',
  'meetings',
  'contracts',
] as const;
export type ItemKind = (typeof itemKinds)[number];

export const projectKeys = {
  project: (id: string) => ['portfolio', 'project', id] as const,
  items: (id: string, kind: ItemKind) => ['portfolio', 'project', id, 'items', kind] as const,
  milestones: (id: string) => ['portfolio', 'project', id, 'milestones'] as const,
  monthly: (id: string) => ['portfolio', 'project', id, 'monthly'] as const,
  changes: (id: string) => ['portfolio', 'project', id, 'changes'] as const,
  files: (id: string) => ['portfolio', 'project', id, 'files'] as const,
  evidence: (mid: string) => ['portfolio', 'milestone', mid, 'evidence'] as const,
  kpiCards: (search: string) => ['portfolio', 'kpi-cards', search] as const,
};

const idem = () => ({ 'Idempotency-Key': crypto.randomUUID() });

export function useProjectFull(id: string) {
  return useQuery({
    queryKey: projectKeys.project(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}', { params: { path: { id } }, signal })),
  });
}

/** Rows of one kind; the server answers 404 outside the read scope. */
export function useProjectItems<T>(id: string, kind: ItemKind) {
  return useQuery({
    queryKey: projectKeys.items(id, kind),
    queryFn: async ({ signal }) => {
      // The route is chosen at run time: the typed client has one path per kind, the payloads are the kind's DTOs.
      const get = api.GET as unknown as (
        path: string,
        init: { params: { path: { id: string } }; signal: AbortSignal },
      ) => Promise<{ data?: unknown; error?: unknown; response: Response }>;
      const response = await get(`/api/v1/portfolio/projects/{id}/${kind}`, {
        params: { path: { id } },
        signal,
      });
      return unwrap<T[]>(response as never);
    },
  });
}

export async function saveItem(id: string, kind: ItemKind, itemId: string | undefined, body: unknown) {
  const send = (itemId ? api.PUT : api.POST) as unknown as (
    path: string,
    init: { params: { path: Record<string, string> }; body: unknown },
  ) => Promise<never>;
  return unwrap(
    await send(
      itemId ? `/api/v1/portfolio/projects/{id}/${kind}/{itemId}` : `/api/v1/portfolio/projects/{id}/${kind}`,
      { params: { path: itemId ? { id, itemId } : { id } }, body },
    ),
  );
}

export async function deactivateItem(
  id: string,
  kind: ItemKind,
  itemId: string,
  version: number,
  reason: string,
) {
  const response = await api.POST('/api/v1/portfolio/projects/{id}/{kind}/{itemId}/deactivate', {
    params: { path: { id, kind, itemId } },
    body: { reason, version },
  });
  if (response.error) unwrap(response as never);
}

export function useMilestones(id: string) {
  return useQuery({
    queryKey: projectKeys.milestones(id),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/milestones', { params: { path: { id } }, signal }),
      ),
  });
}

export function useMonthly(id: string) {
  return useQuery({
    queryKey: projectKeys.monthly(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/monthly', { params: { path: { id } }, signal })),
  });
}

export function useChangeRequests(id: string) {
  return useQuery({
    queryKey: projectKeys.changes(id),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/change-requests', {
          params: { path: { id } },
          signal,
        }),
      ),
  });
}

export function useProjectFiles(id: string) {
  return useQuery({
    queryKey: projectKeys.files(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/files', { params: { path: { id } }, signal })),
  });
}

export function useEvidence(mid: string | undefined) {
  return useQuery({
    queryKey: projectKeys.evidence(mid ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/milestones/{mid}/evidence', {
          params: { path: { mid: mid ?? '' } },
          signal,
        }),
      ),
    enabled: Boolean(mid),
  });
}

export function useKpiCardSearch(search: string, enabled: boolean) {
  return useQuery({
    queryKey: projectKeys.kpiCards(search),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/kpi-cards', { params: { query: { search } }, signal })),
    enabled,
  });
}

const path = (id: string) => ({ params: { path: { id } } });

function upload(file: File) {
  const form = new FormData();
  form.append('file', file);
  return {
    body: form as unknown as { file: string; comment?: string },
    bodySerializer: (body: unknown) => body as FormData,
  };
}

/** Every project command. Each returns the server's fresh record; the caller refreshes the project's queries. */
export const projectApi = {
  takeOwnership: async ({ id, version }: { id: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/take-ownership', {
        ...path(id),
        body: { version },
        headers: idem(),
      }),
    ),
  charter: async ({ id, version, values }: { id: string; version: number; values: CharterValues }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/charter', { ...path(id), body: { values, version } }),
    ),
  submit: async ({
    id,
    version,
    stage,
    comment,
  }: {
    id: string;
    version: number;
    stage: 'charter' | 'planning' | 'closure' | 'impact';
    comment?: string | null;
  }) => {
    const body = { version, comment: comment ?? null };
    const init = { ...path(id), body, headers: idem() };
    switch (stage) {
      case 'charter':
        return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/submit-charter', init));
      case 'planning':
        return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/submit-planning', init));
      case 'closure':
        return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/submit-closure', init));
      default:
        return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/submit-impact', init));
    }
  },
  lpoUploaded: async ({ id, version }: { id: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/lpo-uploaded', { ...path(id), body: { version } }),
    ),
  confirmCompletion: async ({ id, version }: { id: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/confirm-completion', {
        ...path(id),
        body: { version },
        headers: idem(),
      }),
    ),
  status: async ({
    id,
    version,
    action,
    reason,
  }: {
    id: string;
    version: number;
    action: 'hold' | 'resume' | 'cancel';
    reason: string | null;
  }) => {
    const init = { ...path(id), body: { reason, version } };
    if (action === 'hold') return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/hold', init));
    if (action === 'resume') return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/resume', init));
    return unwrap(await api.POST('/api/v1/portfolio/projects/{id}/cancel', init));
  },
  closure: async ({
    id,
    version,
    summaryOfAchievement,
    mainDeliverables,
  }: {
    id: string;
    version: number;
    summaryOfAchievement: string | null;
    mainDeliverables: string | null;
  }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/closure', {
        ...path(id),
        body: { summaryOfAchievement, mainDeliverables, version },
      }),
    ),
  impact: async ({ id, ...body }: S['ImpactInput'] & { id: string }) =>
    unwrap(await api.PUT('/api/v1/portfolio/projects/{id}/impact', { ...path(id), body })),
  uploadFile: async ({ id, kind, file }: { id: string; kind: 'document' | 'lpo' | 'closure'; file: File }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/files/{kind}', {
        params: { path: { id, kind } },
        ...upload(file),
      }),
    ),
  createMilestone: async ({ id, values }: { id: string; values: MilestoneValues }) =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/milestones', { ...path(id), body: { values } })),
  updateMilestone: async ({
    mid,
    values,
    version,
  }: {
    mid: string;
    values: MilestoneValues;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/milestones/{mid}', {
        params: { path: { mid } },
        body: { values, version },
      }),
    ),
  cancelMilestone: async ({ mid, version, reason }: { mid: string; version: number; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/milestones/{mid}/cancel', {
        params: { path: { mid } },
        body: { reason, version },
      }),
    ),
  progress: async ({
    mid,
    values,
    version,
  }: {
    mid: string;
    values: MilestoneProgressValues;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/milestones/{mid}/progress', {
        params: { path: { mid } },
        body: { values, version },
      }),
    ),
  evidence: async ({ mid, file }: { mid: string; file: File }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/milestones/{mid}/evidence', {
        params: { path: { mid } },
        ...upload(file),
      }),
    ),
  openWindow: async ({ id }: { id: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/monthly/open', { ...path(id) })),
  submitMonthly: async ({ id, version, comment }: { id: string; version: number; comment: string | null }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/monthly/submit', {
        ...path(id),
        body: { version, comment },
        headers: idem(),
      }),
    ),
  createChange: async ({ id, values }: { id: string; values: ChangeRequestValues }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/change-requests', { ...path(id), body: { values } }),
    ),
  updateChange: async ({
    cid,
    values,
    version,
  }: {
    cid: string;
    values: ChangeRequestValues;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/change-requests/{cid}', {
        params: { path: { cid } },
        body: { values, version },
      }),
    ),
  submitChange: async ({ cid, version }: { cid: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/change-requests/{cid}/submit', {
        params: { path: { cid } },
        body: { version, comment: null },
        headers: idem(),
      }),
    ),
  cancelChange: async ({ cid, version, reason }: { cid: string; version: number; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/change-requests/{cid}/cancel', {
        params: { path: { cid } },
        body: { reason, version },
      }),
    ),
  applyChange: async ({
    cid,
    version,
    values,
  }: {
    cid: string;
    version: number;
    values: ChangeApplyValues;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/change-requests/{cid}/apply', {
        params: { path: { cid } },
        body: { values, version },
        headers: idem(),
      }),
    ),
};

/** A project mutation that refreshes everything about the project afterwards (record, rows, plan, status banner, approvals). */
export function useProjectMutation<TArgs, TResult>(projectId: string, fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'project', projectId] }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'projects'] }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'milestone'] }),
      ]);
    },
  });
}

// ---- pure helpers (unit-tested) ---------------------------------------------------------------------------------------------------

/** API ratios (1.0 = 100 %) shown as percentages with up to two decimals. */
export function toPercent(ratio: number | null | undefined): number | null {
  if (ratio === null || ratio === undefined) return null;
  return Math.round(ratio * 10000) / 100;
}

/** A percentage typed by the user as the API ratio (4 decimals); null for empty or non-numeric input. */
export function fromPercent(text: string): number | null {
  const trimmed = text.trim().replace(',', '.');
  if (trimmed === '' || !/^-?\d+(\.\d+)?$/u.test(trimmed)) return null;
  return Math.round(Number(trimmed) * 100) / 10000;
}

/** Weight totals of the two plans over active leaf milestones (phases carry no weight of their own). */
export function weightTotals(
  milestones: readonly Pick<Milestone, 'weight' | 'isHeader' | 'highLevel' | 'status'>[],
) {
  let detailed = 0;
  let highLevel = 0;
  for (const m of milestones) {
    if (m.isHeader || m.status === 'cancelled') continue;
    if (m.highLevel) highLevel += Math.round(m.weight * 10000);
    else detailed += Math.round(m.weight * 10000);
  }
  return { detailed: detailed / 10000, highLevel: highLevel / 10000 };
}

/** The tabs a project shows: initiatives (no charter / closure / impact in their template) get the simplified set. */
export const allProjectTabs = [
  'overview',
  'kpis',
  'risks',
  'stakeholders',
  'team',
  'scope',
  'assumptions',
  'values',
  'milestones',
  'monthly',
  'tasks',
  'challenges',
  'meetings',
  'lessons',
  'contracts',
  'changes',
  'closure',
  'impact',
  'approvals',
  'documents',
  'history',
] as const;
export type ProjectTab = (typeof allProjectTabs)[number];

export function visibleTabs(template: StageTemplateValue): ProjectTab[] {
  return allProjectTabs.filter((tab) => {
    if (tab === 'contracts') return template.charter || template.lpo;
    if (tab === 'closure') return template.closure;
    if (tab === 'impact') return template.closure && template.impact;
    return true;
  });
}

/** A milestone is delayed when it is past its planned end below its weight (the server's rule, for the live dialog). */
export function isDelayed(
  achieveRatio: number,
  weight: number,
  plannedEnd: string | null | undefined,
  today: string,
) {
  return Boolean(plannedEnd) && today > (plannedEnd ?? '') && achieveRatio < weight;
}

/** Phases first, each followed by its children; top-level milestones without a phase in code order. */
export function milestoneTree<T extends Pick<Milestone, 'id' | 'parentId' | 'code' | 'sort'>>(
  rows: readonly T[],
) {
  const byOrder = (a: T, b: T) =>
    a.sort - b.sort || a.code.localeCompare(b.code, undefined, { numeric: true });
  const top = rows.filter((r) => !r.parentId || !rows.some((p) => p.id === r.parentId)).sort(byOrder);
  return top.flatMap((r) => [
    { row: r, depth: 0 },
    ...rows
      .filter((c) => c.parentId === r.id)
      .sort(byOrder)
      .map((c) => ({ row: c, depth: 1 })),
  ]);
}
