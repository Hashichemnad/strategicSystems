import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { projectKeys } from '../projectApi';

type S = components['schemas'];
export type AgileView = S['AgileViewDto'];
export type AgilePermissions = S['AgilePermissionsDto'];
export type AgileMetrics = S['AgileMetricsDto'];
export type Story = S['StoryDto'];
export type Sprint = S['SprintDto'];
export type Epic = S['EpicDto'];
export type Release = S['ReleaseDto'];
export type BoardConfig = S['BoardConfig'];
export type BoardColumn = S['BoardColumn'];
export type Burndown = S['BurndownDto'];
export type PlanDraft = S['PlanDraftDto'];
export type DraftMilestone = S['DraftMilestoneDto'];
export type DraftEpic = S['DraftEpicDto'];
export type DraftStory = S['DraftStoryDto'];
export type ReleaseValues = S['ReleaseValues'];
export type EpicValues = S['EpicValues'];
export type StoryValues = S['StoryValues'];
export type SprintValues = S['SprintValues'];
export type SprintReviewValues = S['SprintReviewValues'];
export type ImpedimentInput = S['ImpedimentInput'];
export type MethodologyInput = S['MethodologyInput'];

export type Methodology = 'waterfall' | 'agile' | 'hybrid';
export type ApprovalMode = 'sprint' | 'monthly';
export type Priority = 'must' | 'should' | 'could' | 'wont';
export type Category = 'todo' | 'doing' | 'done';
export type Swimlane = 'none' | 'epic' | 'assignee';

export const methodologies: readonly Methodology[] = ['waterfall', 'agile', 'hybrid'];
export const priorities: readonly Priority[] = ['must', 'should', 'could', 'wont'];
export const categories: readonly Category[] = ['todo', 'doing', 'done'];
export const swimlanes: readonly Swimlane[] = ['none', 'epic', 'assignee'];

export const agileKeys = {
  view: (id: string) => [...projectKeys.project(id), 'agile'] as const,
  burndown: (sid: string) => ['portfolio', 'sprint', sid, 'burndown'] as const,
};

const idem = () => ({ 'Idempotency-Key': crypto.randomUUID() });
const projectPath = (id: string) => ({ params: { path: { id } } });
const itemPath = (id: string, itemId: string) => ({ params: { path: { id, itemId } } });
const sprintPath = (sid: string) => ({ params: { path: { sid } } });

/** The whole agile view of a project (backlog, board, sprints, metrics, what the caller may do). */
export function useAgile(id: string, enabled = true) {
  return useQuery({
    queryKey: agileKeys.view(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/agile', { ...projectPath(id), signal })),
    enabled,
  });
}

export function useBurndown(sid: string | undefined) {
  return useQuery({
    queryKey: agileKeys.burndown(sid ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/sprints/{sid}/burndown', { ...sprintPath(sid ?? ''), signal })),
    enabled: Boolean(sid),
  });
}

/** Every agile command; each answers the fresh agile view. */
export const agileApi = {
  setMethodology: async ({ id, ...body }: MethodologyInput & { id: string }) =>
    unwrap(await api.PUT('/api/v1/portfolio/projects/{id}/methodology', { ...projectPath(id), body })),
  setBoard: async ({ id, board, version }: { id: string; board: BoardConfig; version: number }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/board', {
        ...projectPath(id),
        body: { board, version },
      }),
    ),
  saveRelease: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: ReleaseValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/releases/{itemId}', {
            ...itemPath(id, itemId),
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/portfolio/projects/{id}/releases', {
            ...projectPath(id),
            body: { values },
          }),
        ),
  saveEpic: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: EpicValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/epics/{itemId}', {
            ...itemPath(id, itemId),
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/portfolio/projects/{id}/epics', { ...projectPath(id), body: { values } }),
        ),
  cancelEpic: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/epics/{itemId}/cancel', itemPath(id, itemId))),
  saveStory: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: StoryValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/stories/{itemId}', {
            ...itemPath(id, itemId),
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/portfolio/projects/{id}/stories', { ...projectPath(id), body: { values } }),
        ),
  moveStory: async ({
    sid,
    column,
    rank,
    version,
  }: {
    sid: string;
    column: string;
    rank: number;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/stories/{sid}/move', {
        ...sprintPath(sid),
        body: { column, rank, version },
      }),
    ),
  cancelStory: async ({ sid }: { sid: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/stories/{sid}/cancel', sprintPath(sid))),
  saveSprint: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: SprintValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/sprints/{itemId}', {
            ...itemPath(id, itemId),
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/portfolio/projects/{id}/sprints', { ...projectPath(id), body: { values } }),
        ),
  startSprint: async ({ sid, version }: { sid: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/sprints/{sid}/start', {
        ...sprintPath(sid),
        body: { version },
        headers: idem(),
      }),
    ),
  closeSprint: async ({
    sid,
    values,
    version,
  }: {
    sid: string;
    values: SprintReviewValues;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/sprints/{sid}/close', {
        ...sprintPath(sid),
        body: { values, version },
      }),
    ),
  submitSprint: async ({ sid, version, comment }: { sid: string; version: number; comment: string | null }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/sprints/{sid}/submit', {
        ...sprintPath(sid),
        body: { version, comment },
        headers: idem(),
      }),
    ),
  cancelSprint: async ({ sid, version }: { sid: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/sprints/{sid}/cancel', { ...sprintPath(sid), body: { version } }),
    ),
  raiseImpediment: async ({ sid, ...body }: ImpedimentInput & { sid: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/sprints/{sid}/impediments', { ...sprintPath(sid), body })),
  /** A plan DRAFT from the AI plan generator; nothing is saved (CLAUDE.md rule 6). */
  draftPlan: async ({ id }: { id: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/ai/plan', projectPath(id))),
};

/**
 * An agile command: the answered view goes into the cache; the project record (progress, gates, status banner), its other
 * tabs, sprint burndowns and approvals are refreshed.
 */
export function useAgileMutation<TArgs>(projectId: string, fn: (args: TArgs) => Promise<AgileView>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async (view) => {
      queryClient.setQueryData(agileKeys.view(projectId), view);
      await Promise.all([
        queryClient.invalidateQueries({
          queryKey: projectKeys.project(projectId),
          predicate: (query) => query.queryKey[3] !== 'agile',
        }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'sprint'] }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'projects'] }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
      ]);
    },
  });
}
