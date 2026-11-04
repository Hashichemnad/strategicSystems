import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type AiCapabilities = components['schemas']['AiCapabilitiesDto'];
export type AiSettings = components['schemas']['AiSettings'];
export type AiSettingsDocument = components['schemas']['AiSettingsDto'];
export type AiProvider = components['schemas']['AiProviderDto'];
export type AiProviderInput = components['schemas']['AiProviderInput'];
export type AiProviderTest = components['schemas']['AiProviderTestDto'];
export type ExtractionResult = components['schemas']['ExtractionResultDto'];
export type ExtractedField = components['schemas']['ExtractedFieldDto'];
export type ApproverBrief = components['schemas']['ApproverBriefDto'];
export type AskAnswer = components['schemas']['AskAnswerDto'];
export type AskTurn = components['schemas']['AskTurn'];
export type RecordSearchHit = components['schemas']['RecordSearchHit'];
export type AiFeatureKey =
  | 'extract'
  | 'brief'
  | 'translate'
  | 'similar'
  | 'ask'
  | 'tasks-from-text'
  | 'task-summary'
  | 'task-assignee'
  | 'task-workload'
  | 'teams-formation'
  | 'teams-members'
  | 'teams-evaluation'
  | 'teams-minutes'
  | 'portfolio-assessment'
  | 'kpi-evidence-check'
  | 'kpi-anomalies'
  | 'kpi-causality'
  | 'kpi-card-builder'
  | 'kpi-target-suggestion'
  | 'kpi-narrative'
  | 'risk-identify'
  | 'risk-quality'
  | 'risk-suggest'
  | 'risk-duplicates'
  | 'risk-review-assistant'
  | 'risk-report';

export const aiKeys = {
  all: ['ai'] as const,
  capabilities: ['ai', 'capabilities'] as const,
  settings: ['ai', 'settings'] as const,
  providers: ['ai', 'providers'] as const,
  usage: (query: object) => ['ai', 'usage', query] as const,
  summary: ['ai', 'usage', 'summary'] as const,
};

/**
 * What the current user may use now (master switch, feature switch, role permission, provider). When AI is off nothing is
 * available and every AI action stays hidden (CLAUDE.md §12); errors also count as "off", so AI never blocks the page.
 */
export function useAiCapabilities() {
  return useQuery({
    queryKey: aiKeys.capabilities,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ai/capabilities', { signal })),
    staleTime: 60_000,
    retry: false,
  });
}

export function useAiFeature(feature: AiFeatureKey): boolean {
  const capabilities = useAiCapabilities();
  return capabilities.data?.enabled === true && capabilities.data.features[feature] === true;
}

// ---- Features (every result is a draft the user accepts) ----

export function useExtractForm(formKey: string) {
  return useMutation({
    mutationFn: async (file: File) => {
      const form = new FormData();
      form.append('file', file);
      return unwrap(
        await api.POST('/api/v1/ai/forms/{formKey}/extract', {
          params: { path: { formKey } },
          body: form as unknown as { file: string },
          bodySerializer: (body: unknown) => body as FormData,
        }),
      );
    },
  });
}

export function useApproverBrief() {
  return useMutation({
    mutationFn: async ({ resourceType, resourceId }: { resourceType: string; resourceId: string }) =>
      unwrap(
        await api.POST('/api/v1/ai/brief/{resourceType}/{resourceId}', {
          params: { path: { resourceType, resourceId } },
        }),
      ),
  });
}

export function useTranslate() {
  return useMutation({
    mutationFn: async (body: { text: string; from: 'ar' | 'en'; to: 'ar' | 'en'; systemCode?: string }) =>
      unwrap(await api.POST('/api/v1/ai/translate', { body: { systemCode: null, ...body } })),
  });
}

export function useSimilarRecords(
  resourceType: string,
  text: string,
  excludeId: string | null,
  enabled: boolean,
) {
  return useQuery({
    queryKey: ['ai', 'similar', resourceType, text, excludeId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.POST('/api/v1/ai/similar/{resourceType}', {
          params: { path: { resourceType } },
          body: { text, excludeId },
          signal,
        }),
      ),
    enabled: enabled && text.trim().length >= 3,
    placeholderData: keepPreviousData,
    staleTime: 30_000,
    retry: false,
  });
}

export function useAsk() {
  return useMutation({
    mutationFn: async (body: { question: string; history: AskTurn[] }) =>
      unwrap(await api.POST('/api/v1/ai/ask', { body })),
  });
}

// ---- Settings → AI ----

export function useAiSettings() {
  return useQuery({
    queryKey: aiKeys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ai/settings', { signal })),
  });
}

export function useAiProviders() {
  return useQuery({
    queryKey: aiKeys.providers,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ai/providers', { signal })),
  });
}

export function useSaveAiSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: { settings: AiSettings; version: number }) =>
      unwrap(await api.PUT('/api/v1/ai/settings', { body })),
    onSuccess: (saved) => {
      queryClient.setQueryData(aiKeys.settings, saved);
      void queryClient.invalidateQueries({ queryKey: aiKeys.capabilities });
    },
  });
}

export function useSaveAiProvider() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, body }: { id: string | null; body: AiProviderInput }) =>
      id
        ? unwrap(await api.PUT('/api/v1/ai/providers/{id}', { params: { path: { id } }, body }))
        : unwrap(await api.POST('/api/v1/ai/providers', { body })),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: aiKeys.providers }),
        queryClient.invalidateQueries({ queryKey: aiKeys.capabilities }),
      ]),
  });
}

export function useTestAiProvider() {
  return useMutation({
    mutationFn: async ({
      id,
      apiKey,
      model,
    }: {
      id: string;
      apiKey?: string | null;
      model?: string | null;
    }) =>
      unwrap(
        await api.POST('/api/v1/ai/providers/{id}/test', {
          params: { path: { id } },
          body: { apiKey: apiKey ?? null, endpoint: null, model: model ?? null },
        }),
      ),
  });
}

export function useFetchAiModels() {
  return useMutation({
    mutationFn: async ({ id, apiKey }: { id: string; apiKey?: string | null }) =>
      unwrap(
        await api.POST('/api/v1/ai/providers/{id}/models', {
          params: { path: { id } },
          body: { apiKey: apiKey ?? null, endpoint: null, model: null },
        }),
      ),
  });
}

export function useAiUsage(query: { page: number; feature?: string; success?: boolean }) {
  return useQuery({
    queryKey: aiKeys.usage(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/ai/usage', { params: { query: { ...query, pageSize: 25 } }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useAiUsageSummary() {
  return useQuery({
    queryKey: aiKeys.summary,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/ai/usage/summary', { signal })),
  });
}
