import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type ProcessSummary = components['schemas']['ProcessSummaryDto'];
export type ProcessDetail = components['schemas']['ProcessDetailDto'];
export type StepDefinition = components['schemas']['ApprovalStepDefinition'];
export type ApproverRule = components['schemas']['ApproverRule'];
export type CustomAction = components['schemas']['CustomApprovalAction'];
export type Preview = components['schemas']['PreviewDto'];
export type MyWork = components['schemas']['MyWorkDto'];
export type MyWorkTask = components['schemas']['MyWorkTaskDto'];
export type SubjectApprovals = components['schemas']['SubjectApprovalsDto'];
export type Instance = components['schemas']['InstanceDto'];
export type StepState = components['schemas']['StepStateDto'];
export type History = components['schemas']['HistoryDto'];
export type MyTask = components['schemas']['MyTaskDto'];
export type PersonName = components['schemas']['PersonName'];
export type CurrentStatus = components['schemas']['CurrentStatus'];
export type ApprovalSettings = components['schemas']['ApprovalSettingsDto'];

export const workflowKeys = {
  all: ['workflow'] as const,
  processes: ['workflow', 'processes'] as const,
  process: (key: string, version?: number) => ['workflow', 'processes', key, version ?? 'current'] as const,
  settings: ['workflow', 'settings'] as const,
  myWork: ['workflow', 'my-work'] as const,
  subject: (type: string, id: string) => ['workflow', 'subject', type, id] as const,
  status: (type: string, id: string) => ['workflow', 'status', type, id] as const,
};

/** A fresh idempotency key (CLAUDE.md §11): one per decision dialog, so a retried click replays instead of acting twice. */
export function newIdempotencyKey(): string {
  return crypto.randomUUID();
}

export function useProcesses() {
  return useQuery({
    queryKey: workflowKeys.processes,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/workflow/processes', { signal })),
  });
}

export function useProcess(key: string, version?: number) {
  return useQuery({
    queryKey: workflowKeys.process(key, version),
    queryFn: async ({ signal }) =>
      version
        ? unwrap(
            await api.GET('/api/v1/workflow/processes/{key}/versions/{versionNo}', {
              params: { path: { key, versionNo: version } },
              signal,
            }),
          )
        : unwrap(await api.GET('/api/v1/workflow/processes/{key}', { params: { path: { key } }, signal })),
  });
}

export function useSaveProcess(key: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { steps: StepDefinition[]; note: string | null; baseVersionNo: number }) =>
      unwrap(await api.PUT('/api/v1/workflow/processes/{key}', { params: { path: { key } }, body })),
    onSuccess: (saved) => {
      queryClient.setQueryData(workflowKeys.process(key), saved);
      void queryClient.invalidateQueries({ queryKey: workflowKeys.processes });
    },
  });
}

export function usePreview(key: string) {
  return useMutation({
    mutationFn: async (body: {
      steps: StepDefinition[];
      submitterEmployeeId: string | null;
      unitId: string | null;
      variables: Record<string, unknown>;
    }) =>
      unwrap(
        await api.POST('/api/v1/workflow/processes/{key}/preview', {
          params: { path: { key } },
          // Variable values are sent as typed JSON values; the server reads them as each variable's type.
          body: body as never,
        }),
      ),
  });
}

export function useConditionCheck(key: string) {
  return useMutation({
    mutationFn: async (condition: string) =>
      unwrap(
        await api.POST('/api/v1/workflow/processes/{key}/validate-condition', {
          params: { path: { key } },
          body: { condition },
        }),
      ),
  });
}

export function useApprovalSettings() {
  return useQuery({
    queryKey: workflowKeys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/workflow/settings', { signal })),
  });
}

export function useSaveApprovalSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { sectionLevelCode: string; departmentLevelCode: string; version: number }) =>
      unwrap(await api.PUT('/api/v1/workflow/settings', { body })),
    onSuccess: (saved) => queryClient.setQueryData(workflowKeys.settings, saved),
  });
}

export function useMyWork() {
  return useQuery({
    queryKey: workflowKeys.myWork,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/workflow/my-work', { signal })),
    refetchInterval: 60_000,
  });
}

export function useSubjectApprovals(resourceType: string, resourceId: string) {
  return useQuery({
    queryKey: workflowKeys.subject(resourceType, resourceId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/workflow/subjects/{resourceType}/{resourceId}', {
          params: { path: { resourceType, resourceId } },
          signal,
        }),
      ),
  });
}

export function useCurrentStatus(resourceType: string, resourceId: string) {
  return useQuery({
    queryKey: workflowKeys.status(resourceType, resourceId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/workflow/status/{resourceType}/{resourceId}', {
          params: { path: { resourceType, resourceId } },
          signal,
        }),
      ),
  });
}

/** Refreshes everything an approval decision can change (panel, banner, My Work, the record itself, history). */
export function useInvalidateApprovals() {
  const queryClient = useQueryClient();
  return () =>
    Promise.all([
      queryClient.invalidateQueries({ queryKey: workflowKeys.all }),
      queryClient.invalidateQueries({ queryKey: ['sample'] }),
      queryClient.invalidateQueries({ queryKey: ['audit'] }),
      queryClient.invalidateQueries({ queryKey: ['notifications'] }),
    ]);
}

export function useAct() {
  const invalidate = useInvalidateApprovals();
  return useMutation({
    mutationFn: async (input: {
      taskId: string;
      action: string;
      comment: string | null;
      onBehalfOfUserId: string | null;
      idempotencyKey: string;
    }) =>
      unwrap(
        await api.POST('/api/v1/workflow/tasks/{taskId}/act', {
          params: { path: { taskId: input.taskId } },
          headers: { 'Idempotency-Key': input.idempotencyKey },
          body: {
            action: input.action,
            comment: input.comment,
            onBehalfOfUserId: input.onBehalfOfUserId,
            version: null,
          },
        }),
      ),
    onSuccess: invalidate,
  });
}
