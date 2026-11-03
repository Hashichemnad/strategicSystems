import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

export type TaskListItem = components['schemas']['TaskListItemDto'];
export type TaskDetail = components['schemas']['TaskDto'];
export type TaskCounts = components['schemas']['TaskCountsDto'];
export type TaskTimeline = components['schemas']['TimelineDto'];
export type TaskComment = components['schemas']['CommentDto'];
export type TaskGroup = components['schemas']['GroupDto'];
export type TaskDashboard = components['schemas']['TaskDashboardDto'];
export type TaskSettingsDto = components['schemas']['TaskSettingsDto'];
export type TaskSettings = components['schemas']['TaskSettings'];
export type CreateTaskInput = components['schemas']['CreateTask'];
export type TaskDraft = components['schemas']['TaskDraftDto'];
export type AssigneeSuggestion = components['schemas']['AssigneeSuggestionDto'];
export type WorkloadInsight = components['schemas']['WorkloadInsightDto'];
export type TaskListQuery = NonNullable<operations['ListTasks']['parameters']['query']>;

/** Lifecycle actions with an intent-named endpoint (docs/design/tasks.md §9). */
export type TaskAction =
  | 'assign'
  | 'accept'
  | 'reject'
  | 'pause'
  | 'start'
  | 'submit'
  | 'complete'
  | 'approve'
  | 'return'
  | 'reassign'
  | 'cancel';

export const taskActions: readonly TaskAction[] = [
  'assign',
  'accept',
  'reject',
  'pause',
  'start',
  'submit',
  'complete',
  'approve',
  'return',
  'reassign',
  'cancel',
];

/** Full search of the My Tasks route (TanStack Router links need every field). */
export const defaultTasksSearch = {
  tab: 'assigned',
  view: 'list',
  page: 1,
  sort: 'dueDate',
  openOnly: true,
} as const;

export const taskKeys = {
  all: ['tasks'] as const,
  list: (query: TaskListQuery) => [...taskKeys.all, 'list', query] as const,
  counts: () => [...taskKeys.all, 'counts'] as const,
  task: (id: string) => [...taskKeys.all, 'task', id] as const,
  timeline: (id: string) => [...taskKeys.all, 'timeline', id] as const,
  comments: (id: string) => [...taskKeys.all, 'comments', id] as const,
  group: (id: string) => [...taskKeys.all, 'group', id] as const,
  dashboard: () => [...taskKeys.all, 'dashboard'] as const,
  workload: () => [...taskKeys.all, 'workload'] as const,
  settings: () => [...taskKeys.all, 'settings'] as const,
};

export function useTasks(query: TaskListQuery, enabled = true) {
  return useQuery({
    queryKey: taskKeys.list(query),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/tasks', { params: { query }, signal })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useTaskCounts() {
  return useQuery({
    queryKey: taskKeys.counts(),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/tasks/counts', { signal })),
  });
}

export function useTask(id: string) {
  return useQuery({
    queryKey: taskKeys.task(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/tasks/{id}', { params: { path: { id } }, signal })),
  });
}

export function useTaskTimeline(id: string) {
  return useQuery({
    queryKey: taskKeys.timeline(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/tasks/{id}/timeline', { params: { path: { id } }, signal })),
  });
}

export function useTaskComments(id: string) {
  return useQuery({
    queryKey: taskKeys.comments(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/tasks/{id}/comments', { params: { path: { id } }, signal })),
  });
}

export function useTaskGroup(id: string) {
  return useQuery({
    queryKey: taskKeys.group(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/tasks/groups/{id}', { params: { path: { id } }, signal })),
  });
}

export function useTaskDashboard() {
  return useQuery({
    queryKey: taskKeys.dashboard(),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/tasks/dashboard', { signal })),
  });
}

export function useWorkloadInsights(enabled: boolean) {
  return useQuery({
    queryKey: taskKeys.workload(),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/tasks/ai/workload', { signal })),
    enabled,
    retry: false,
  });
}

export function useTaskSettings() {
  return useQuery({
    queryKey: taskKeys.settings(),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/tasks/settings', { signal })),
  });
}

/** Every task change refreshes lists, counts, the open task, My Work and the History tab. */
function useInvalidateTasks() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: taskKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['workflow'] }),
      queryClient.invalidateQueries({ queryKey: ['audit'] }),
    ]);
}

export function useCreateTask() {
  const onSuccess = useInvalidateTasks();
  return useMutation({
    mutationFn: async ({ body, idempotencyKey }: { body: CreateTaskInput; idempotencyKey: string }) =>
      unwrap(await api.POST('/api/v1/tasks', { body, headers: { 'Idempotency-Key': idempotencyKey } })),
    onSuccess,
  });
}

export interface TaskActionBody {
  reason?: string | null;
  comment?: string | null;
  feedbackPoint?: number | null;
  assigneeEmployeeId?: string | null;
}

export function useTaskAction() {
  const onSuccess = useInvalidateTasks();
  return useMutation({
    mutationFn: async ({
      task,
      action,
      body,
    }: {
      task: { id: string; version: number };
      action: TaskAction;
      body?: TaskActionBody;
    }) =>
      unwrap(
        await api.POST(`/api/v1/tasks/{id}/${action}` as '/api/v1/tasks/{id}/accept', {
          params: { path: { id: task.id } },
          body: { version: task.version, ...(body ?? {}) },
        }),
      ),
    onSuccess,
  });
}

export function useUpdateTask() {
  const onSuccess = useInvalidateTasks();
  return useMutation({
    mutationFn: async ({ id, ...body }: components['schemas']['UpdateTaskInput'] & { id: string }) =>
      unwrap(await api.PUT('/api/v1/tasks/{id}', { params: { path: { id } }, body })),
    onSuccess,
  });
}

export function useCreateSubtask() {
  const onSuccess = useInvalidateTasks();
  return useMutation({
    mutationFn: async ({ parentId, ...body }: components['schemas']['SubtaskInput'] & { parentId: string }) =>
      unwrap(await api.POST('/api/v1/tasks/{id}/subtasks', { params: { path: { id: parentId } }, body })),
    onSuccess,
  });
}

export function useAddComment(taskId: string) {
  const onSuccess = useInvalidateTasks();
  return useMutation({
    mutationFn: async (body: components['schemas']['CommentInput']) =>
      unwrap(await api.POST('/api/v1/tasks/{id}/comments', { params: { path: { id: taskId } }, body })),
    onSuccess,
  });
}

export function useFollowUps(taskId: string) {
  const onSuccess = useInvalidateTasks();
  const add = useMutation({
    mutationFn: async (body: { employeeId: string; canUpload: boolean }) =>
      unwrap(await api.POST('/api/v1/tasks/{id}/follow-ups', { params: { path: { id: taskId } }, body })),
    onSuccess,
  });
  const remove = useMutation({
    mutationFn: async (employeeId: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/{id}/follow-ups/{employeeId}/remove', {
          params: { path: { id: taskId, employeeId } },
        }),
      ),
    onSuccess,
  });
  return { add, remove };
}

export function useChecklist(taskId: string) {
  const onSuccess = useInvalidateTasks();
  const add = useMutation({
    mutationFn: async (text: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/{id}/checklist', { params: { path: { id: taskId } }, body: { text } }),
      ),
    onSuccess,
  });
  const update = useMutation({
    mutationFn: async ({ itemId, done }: { itemId: string; done: boolean }) =>
      unwrap(
        await api.PUT('/api/v1/tasks/{id}/checklist/{itemId}', {
          params: { path: { id: taskId, itemId } },
          body: { done, text: null },
        }),
      ),
    onSuccess,
  });
  const remove = useMutation({
    mutationFn: async (itemId: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/{id}/checklist/{itemId}/remove', {
          params: { path: { id: taskId, itemId } },
        }),
      ),
    onSuccess,
  });
  return { add, update, remove };
}

export function useDependencies(taskId: string) {
  const onSuccess = useInvalidateTasks();
  const add = useMutation({
    mutationFn: async (dependsOnTaskId: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/{id}/dependencies', {
          params: { path: { id: taskId } },
          body: { dependsOnTaskId },
        }),
      ),
    onSuccess,
  });
  const remove = useMutation({
    mutationFn: async (dependsOnId: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/{id}/dependencies/{dependsOnId}/remove', {
          params: { path: { id: taskId, dependsOnId } },
        }),
      ),
    onSuccess,
  });
  return { add, remove };
}

export function useGroupActions(groupId: string) {
  const onSuccess = useInvalidateTasks();
  const message = useMutation({
    mutationFn: async (text: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/groups/{id}/message', {
          params: { path: { id: groupId } },
          body: { text },
        }),
      ),
    onSuccess,
  });
  const extend = useMutation({
    mutationFn: async (dueDate: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/groups/{id}/extend-due', {
          params: { path: { id: groupId } },
          body: { dueDate },
        }),
      ),
    onSuccess,
  });
  const cancel = useMutation({
    mutationFn: async (reason: string) =>
      unwrap(
        await api.POST('/api/v1/tasks/groups/{id}/cancel', {
          params: { path: { id: groupId } },
          body: { reason },
        }),
      ),
    onSuccess,
  });
  return { message, extend, cancel };
}

export function useSaveTaskSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { value: TaskSettings; version: number }) =>
      unwrap(await api.PUT('/api/v1/tasks/settings', { body })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: taskKeys.settings() }),
  });
}

export function useDraftTasksFromText() {
  return useMutation({
    mutationFn: async (text: string) =>
      unwrap(await api.POST('/api/v1/tasks/ai/from-text', { body: { text } })),
  });
}

export function useSuggestAssignees() {
  return useMutation({
    mutationFn: async (title: string) =>
      unwrap(await api.POST('/api/v1/tasks/ai/suggest-assignees', { body: { title, unitId: null } })),
  });
}

export function useSummariseTask(taskId: string) {
  return useMutation({
    mutationFn: async (mode: 'summary' | 'submission' | 'return') =>
      unwrap(
        await api.POST('/api/v1/tasks/{id}/ai/summary', { params: { path: { id: taskId } }, body: { mode } }),
      ),
  });
}

/** `HH:MM` from seconds (timesheet durations). */
export function formatDuration(seconds: number | null | undefined): string {
  if (seconds === null || seconds === undefined) return '—';
  const minutes = Math.round(seconds / 60);
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  if (hours >= 24) {
    const days = Math.floor(hours / 24);
    return `${days}d ${hours % 24}h`;
  }
  return `${hours}:${String(rest).padStart(2, '0')}`;
}
