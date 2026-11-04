import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { useAiCapabilities } from '@/features/ai/api';

type S = components['schemas'];
export type Health = S['HealthDto'];
export type HealthValue = S['HealthValue'];
export type Variance = S['VarianceDto'];
export type VarianceRow = S['VarianceRow'];
export type Baseline = S['BaselineDto'];
export type Evm = S['EvmDto'];
export type RaidLog = S['RaidLogDto'];
export type RaidItem = S['RaidItemDto'];
export type RaidRisk = S['RaidRiskDto'];
export type RaidValues = S['RaidValues'];
export type RiskMatrix = S['RiskMatrixDto'];
export type RaciMatrix = S['RaciMatrixDto'];
export type RaciEntry = S['RaciEntryDto'];
export type RaciValues = S['RaciValues'];
export type Communication = S['CommunicationDto'];
export type CommunicationValues = S['CommunicationValues'];
export type Benefit = S['BenefitDto'];
export type BenefitValues = S['BenefitValues'];
export type BenefitMeasurement = S['BenefitMeasurement'];
export type ContractExtras = S['ContractExtrasDto'];
export type Payment = S['PaymentDto'];
export type PaymentValues = S['PaymentValues'];
export type ProjectTemplate = Pick<
  S['ProjectTemplateDto'],
  'id' | 'code' | 'nameAr' | 'nameEn' | 'methodology' | 'status' | 'version'
>;
export type CharterSuggestions = S['CharterSuggestionsDto'];
export type UpdateDraft = S['UpdateDraftDto'];
export type StatusNarrative = S['StatusNarrativeDto'];
export type ChangeImpact = S['ChangeImpactDto'];
export type LessonHit = S['LessonHitDto'];
export type HealthDimension = 'schedule' | 'cost' | 'scope' | 'risk' | 'overall';
export type Gate = 'charter' | 'planning' | 'closure';

/**
 * The project's stage-gate checklist (server `ChecklistDto` of the portfolio module). The generated schema has a Tasks type of the same
 * name, so the portfolio shape is written out here from StandardsServices.cs.
 */
export interface GateChecklistItem {
  gate: string;
  code: string;
  labelAr: string | null;
  labelEn: string | null;
  required: boolean;
  checked: boolean;
  at: string | null;
  note: string | null;
}
export interface GateChecklist {
  items: GateChecklistItem[];
  canTick: boolean;
  version: number;
}

/** Every key lives under the project key, so useProjectMutation's refresh reaches them. */
export const standardsKeys = {
  health: (id: string) => ['portfolio', 'project', id, 'health'] as const,
  baselines: (id: string) => ['portfolio', 'project', id, 'baselines'] as const,
  variance: (id: string, version: number | undefined) =>
    ['portfolio', 'project', id, 'variance', version ?? 'latest'] as const,
  evm: (id: string) => ['portfolio', 'project', id, 'evm'] as const,
  raid: (id: string) => ['portfolio', 'project', id, 'raid'] as const,
  riskMatrix: (id: string) => ['portfolio', 'project', id, 'risk-matrix'] as const,
  raci: (id: string) => ['portfolio', 'project', id, 'raci'] as const,
  communications: (id: string) => ['portfolio', 'project', id, 'communications'] as const,
  benefits: (id: string) => ['portfolio', 'project', id, 'benefits'] as const,
  requestBenefits: (id: string) => ['portfolio', 'request-benefits', id] as const,
  contractExtras: (id: string, cid: string) => ['portfolio', 'project', id, 'contract-extras', cid] as const,
  checklist: (id: string) => ['portfolio', 'project', id, 'checklist'] as const,
  templates: ['portfolio', 'templates'] as const,
  lessons: (q: string, exclude: string | undefined) => ['portfolio', 'lessons', q, exclude ?? ''] as const,
  projectSearch: (search: string) => ['portfolio', 'project-search', search] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

export function useHealth(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.health(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/health', { ...path(id), signal })),
    enabled,
  });
}

export function useBaselines(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.baselines(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/baselines', { ...path(id), signal })),
    enabled,
  });
}

export function useVariance(id: string, version: number | undefined, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.variance(id, version),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/baselines/variance', {
          params: { path: { id }, query: version === undefined ? {} : { version } },
          signal,
        }),
      ),
    enabled,
    retry: false,
  });
}

export function useEvm(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.evm(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/evm', { ...path(id), signal })),
    enabled,
    retry: false,
  });
}

export function useRaid(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.raid(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/raid', { ...path(id), signal })),
    enabled,
  });
}

export function useRiskMatrix(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.riskMatrix(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/risk-matrix', { ...path(id), signal })),
    enabled,
  });
}

export function useRaci(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.raci(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/raci', { ...path(id), signal })),
    enabled,
  });
}

export function useCommunications(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.communications(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/communications', { ...path(id), signal })),
    enabled,
  });
}

export function useBenefits(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.benefits(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/benefits', { ...path(id), signal })),
    enabled,
  });
}

export function useRequestBenefits(requestId: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.requestBenefits(requestId),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/requests/{id}/benefits', { ...path(requestId), signal })),
    enabled,
  });
}

export function useContractExtras(id: string, cid: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.contractExtras(id, cid),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/contracts/{cid}/extras', {
          params: { path: { id, cid } },
          signal,
        }),
      ),
    enabled,
  });
}

export function useGateChecklist(id: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.checklist(id),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/checklist', { ...path(id), signal }),
      ),
    enabled,
  });
}

export function useProjectTemplates(enabled = true) {
  return useQuery({
    queryKey: standardsKeys.templates,
    queryFn: async ({ signal }): Promise<ProjectTemplate[]> =>
      unwrap(await api.GET('/api/v1/portfolio/templates', { signal })),
    enabled,
  });
}

export function useLessonSearch(q: string, excludeProjectId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.lessons(q, excludeProjectId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/lessons/search', {
          params: { query: { q, ...(excludeProjectId ? { excludeProjectId } : {}) } },
          signal,
        }),
      ),
    enabled,
  });
}

/** Projects the user may read, for the dependency picker of the RAID log. */
export function useProjectSearch(search: string, enabled = true) {
  return useQuery({
    queryKey: standardsKeys.projectSearch(search),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects', {
          params: { query: { search: search || undefined, pageSize: 20, page: 1 } },
          signal,
        }),
      ),
    enabled,
  });
}

const idem = () => ({ 'Idempotency-Key': crypto.randomUUID() });

/** Every PM-standards command (each returns the server's fresh view; callers refresh through useProjectMutation). */
export const standardsApi = {
  overrideHealth: async ({
    id,
    dimension,
    value,
    justification,
    version,
  }: {
    id: string;
    dimension: HealthDimension;
    value: 'green' | 'amber' | 'red' | null;
    justification: string | null;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/health/{dimension}', {
        params: { path: { id, dimension } },
        body: { value, justification, version },
      }),
    ),
  saveRaid: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: RaidValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/raid/{itemId}', {
            params: { path: { id, itemId } },
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(await api.POST('/api/v1/portfolio/projects/{id}/raid', { ...path(id), body: { values } })),
  acceptRaid: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/raid/{itemId}/accept', {
        params: { path: { id, itemId } },
      }),
    ),
  saveRaci: async ({ id, itemId, values }: { id: string; itemId?: string; values: RaciValues }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/raci/{itemId}', {
            params: { path: { id, itemId } },
            body: { values },
          }),
        )
      : unwrap(await api.POST('/api/v1/portfolio/projects/{id}/raci', { ...path(id), body: { values } })),
  removeRaci: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/raci/{itemId}/remove', {
        params: { path: { id, itemId } },
      }),
    ),
  saveCommunication: async ({
    id,
    itemId,
    values,
  }: {
    id: string;
    itemId?: string;
    values: CommunicationValues;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/communications/{itemId}', {
            params: { path: { id, itemId } },
            body: { values },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/portfolio/projects/{id}/communications', { ...path(id), body: { values } }),
        ),
  generateCommunications: async ({ id }: { id: string }) =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/communications/generate', { ...path(id) })),
  communicationDone: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/communications/{itemId}/done', {
        params: { path: { id, itemId } },
      }),
    ),
  removeCommunication: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/communications/{itemId}/remove', {
        params: { path: { id, itemId } },
      }),
    ),
  saveBenefit: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: BenefitValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/benefits/{itemId}', {
            params: { path: { id, itemId } },
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(await api.POST('/api/v1/portfolio/projects/{id}/benefits', { ...path(id), body: { values } })),
  measureBenefit: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId: string;
    values: BenefitMeasurement;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/benefits/{itemId}/measure', {
        params: { path: { id, itemId } },
        body: { values, version },
      }),
    ),
  removeBenefit: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/benefits/{itemId}/remove', {
        params: { path: { id, itemId } },
      }),
    ),
  saveRequestBenefit: async ({
    id,
    itemId,
    values,
    version,
  }: {
    id: string;
    itemId?: string;
    values: BenefitValues;
    version?: number;
  }) =>
    itemId
      ? unwrap(
          await api.PUT('/api/v1/portfolio/requests/{id}/benefits/{itemId}', {
            params: { path: { id, itemId } },
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(await api.POST('/api/v1/portfolio/requests/{id}/benefits', { ...path(id), body: { values } })),
  removeRequestBenefit: async ({ id, itemId }: { id: string; itemId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/requests/{id}/benefits/{itemId}/remove', {
        params: { path: { id, itemId } },
      }),
    ),
  signOff: async ({
    mid,
    decision,
    comment,
    version,
  }: {
    mid: string;
    decision: 'accepted' | 'rejected';
    comment: string | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/milestones/{mid}/sign-off', {
        params: { path: { mid } },
        body: { decision, comment, version },
      }),
    ),
  savePayment: async ({
    id,
    cid,
    pid,
    values,
    version,
  }: {
    id: string;
    cid: string;
    pid?: string;
    values: PaymentValues;
    version?: number;
  }) =>
    pid
      ? unwrap(
          await api.PUT('/api/v1/portfolio/projects/{id}/contracts/{cid}/payments/{pid}', {
            params: { path: { id, cid, pid } },
            body: { values, version: version ?? null },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/portfolio/projects/{id}/contracts/{cid}/payments', {
            params: { path: { id, cid } },
            body: { values },
          }),
        ),
  rateSupplier: async ({
    id,
    cid,
    quality,
    timeliness,
    communication,
    comment,
  }: {
    id: string;
    cid: string;
    quality: number;
    timeliness: number;
    communication: number;
    comment: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/contracts/{cid}/rating', {
        params: { path: { id, cid } },
        body: { quality, timeliness, communication, comment },
      }),
    ),
  tickChecklist: async ({
    id,
    gate,
    code,
    checked,
    note,
    version,
  }: {
    id: string;
    gate: Gate;
    code: string;
    checked: boolean;
    note: string | null;
    version: number;
  }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/checklist/{gate}/{code}', {
        params: { path: { id, gate, code } },
        body: { checked, note, version },
      }),
    ),
  applyTemplate: async ({ id, templateId }: { id: string; templateId: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/apply-template', {
        ...path(id),
        body: { templateId },
        headers: idem(),
      }),
    ),
  saveAsTemplate: async ({
    id,
    code,
    nameAr,
    nameEn,
  }: {
    id: string;
    code: string;
    nameAr: string | null;
    nameEn: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/save-as-template', {
        ...path(id),
        body: { code, nameAr, nameEn },
      }),
    ),
  saveNarrative: async ({
    id,
    narrativeAr,
    narrativeEn,
  }: {
    id: string;
    narrativeAr: string | null;
    narrativeEn: string | null;
  }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/monthly/narrative', {
        ...path(id),
        body: { narrativeAr, narrativeEn },
      }),
    ),
};

/** AI drafts: nothing is saved by these calls (docs/design/portfolio.md §40). */
export const aiDrafts = {
  charter: async (id: string): Promise<CharterSuggestions> =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/ai/charter', { ...path(id) })),
  update: async (id: string): Promise<UpdateDraft> =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/ai/update', { ...path(id) })),
  statusNarrative: async (id: string): Promise<StatusNarrative> =>
    unwrap(await api.POST('/api/v1/portfolio/projects/{id}/ai/status-report', { ...path(id) })),
  changeImpact: async (cid: string): Promise<ChangeImpact> =>
    unwrap(
      await api.POST('/api/v1/portfolio/change-requests/{cid}/ai/impact', { params: { path: { cid } } }),
    ),
};

/** Request benefits change outside a project: refresh the request's list afterwards. */
export function useRequestBenefitMutation<TArgs, TResult>(
  requestId: string,
  fn: (args: TArgs) => Promise<TResult>,
) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: standardsKeys.requestBenefits(requestId) });
    },
  });
}

function saveBlob(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = name;
  link.click();
  URL.revokeObjectURL(url);
}

/** Downloads the monthly status report PDF of an approved period (audited as an export on the server). */
export async function downloadStatusReport(
  id: string,
  period: string,
  language: 'ar' | 'en',
  number: string,
) {
  const result = await api.GET('/api/v1/portfolio/projects/{id}/status-report', {
    params: { path: { id }, query: { period, language } },
    parseAs: 'blob',
  });
  const blob = unwrap(result);
  saveBlob(blob, `${number}-status-${period}-${language}.pdf`);
}

/** Portfolio AI assist keys (docs/design/portfolio.md §40). */
export type PortfolioAiFeature =
  | 'portfolio-charter-assistant'
  | 'portfolio-update-assistant'
  | 'portfolio-status-report'
  | 'portfolio-change-impact'
  | 'portfolio-plan-generator';

/** Whether an AI assist is available to the user now; when AI is off every AI action stays hidden (CLAUDE.md §12). */
export function useAiAssist(feature: PortfolioAiFeature): boolean {
  const capabilities = useAiCapabilities();
  return capabilities.data?.enabled === true && capabilities.data.features[feature] === true;
}
