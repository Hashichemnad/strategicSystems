import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

type S = components['schemas'];
export type CardValues = S['CardValues'];
export type KpiCard = S['KpiCardDto'];
export type KpiCardListItem = S['KpiCardListItemDto'];
export type KpiOptions = S['KpiOptionsDto'];
export type KpiResult = S['KpiResultDto'];
export type KpiResultListItem = S['KpiResultListItemDto'];
export type KpiPlan = S['KpiPlanDto'];
export type KpiPlanListItem = S['KpiPlanListItemDto'];
export type KpiYearCard = S['KpiYearCardDto'];
export type KpiYearItemInput = S['KpiYearItemInput'];
export type KpiDashboard = S['KpiDashboardDto'];
export type KpiScoreNode = S['KpiScoreNodeDto'];
export type KpiScorecard = S['KpiScorecardDto'];
export type KpiStrategyMap = S['KpiStrategyMapDto'];
export type KpiPerformance = S['KpiPerformanceDto'];
export type KpiChange = S['KpiChangeDto'];
export type ProposedChange = S['ProposedChange'];
export type KpiElement = S['KpiElementDto'];
export type KpiElementInput = S['KpiElementInput'];
export type KpiWindow = S['KpiWindowDto'];
export type KpiSettingsValue = S['KpiSettings'];
export type KpiEntryInput = S['KpiEntryInput'];
export type KpiCardDraft = S['KpiCardDraftDto'];
export type KpiCardTarget = S['KpiCardTargetDto'];
export type DataElement = S['DataElement'];
export type ScaleLevel = S['ScaleLevel'];
export type ChecklistItem = S['ChecklistItem'];
export type KpiCardListQuery = NonNullable<operations['ListKpiCards']['parameters']['query']>;

export const resultStatuses = [
  'not_open',
  'open',
  'pending_approval',
  'returned',
  'approved',
  'locked',
] as const;
export const periodCodes = ['q1', 'q2', 'q3', 'q4', 'h1', 'h2', 'fy', 'ty'] as const;
export const ragCodes = ['green', 'amber', 'red', 'none'] as const;
export const anomalyCodes = ['jump', 'identical', 'negative', 'abovePercentage', 'farBeyondTarget'] as const;
export const smartCodes = [
  'specific',
  'measurable',
  'equation',
  'unitConsistent',
  'direction',
  'timeBound',
  'target',
  'owner',
] as const;

export type Tone = 'neutral' | 'info' | 'success' | 'warning' | 'danger';

export const ragTone: Record<string, Tone> = {
  green: 'success',
  amber: 'warning',
  red: 'danger',
  none: 'neutral',
};

/** Background classes for RAG boxes (strategy map, score bars). Design tokens only. */
export const ragFill: Record<string, string> = {
  green: 'bg-success',
  amber: 'bg-warning',
  red: 'bg-danger',
  none: 'bg-muted-foreground/40',
};

export const ragSoft: Record<string, string> = {
  green: 'bg-success-soft border-success',
  amber: 'bg-warning-soft border-warning',
  red: 'bg-danger-soft border-danger',
  none: 'bg-muted border-border',
};

export const resultTone: Record<string, Tone> = {
  not_open: 'neutral',
  open: 'info',
  pending_approval: 'info',
  returned: 'warning',
  approved: 'success',
  locked: 'neutral',
};

export const kpiKeys = {
  all: ['kpi'] as const,
  cards: (query: KpiCardListQuery) => [...kpiKeys.all, 'cards', query] as const,
  card: (id: string) => [...kpiKeys.all, 'card', id] as const,
  result: (id: string) => [...kpiKeys.all, 'result', id] as const,
};

const path = (id: string) => ({ params: { path: { id } } });

/** `enabled` false: no request (the workspace shell asks only for the current app's options, ADR 0172). */
export function useKpiOptions(enabled = true) {
  return useQuery({
    enabled,
    queryKey: [...kpiKeys.all, 'options'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/kpi/options', { signal })),
    staleTime: 60_000,
  });
}

export function useKpiCards(query: KpiCardListQuery) {
  return useQuery({
    queryKey: kpiKeys.cards(query),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/kpi/cards', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useKpiCard(id: string | undefined) {
  return useQuery({
    queryKey: kpiKeys.card(id ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/cards/{id}', { ...path(id ?? ''), signal })),
    enabled: !!id,
    retry: false,
  });
}

export function useKpiPerformance(id: string, year?: number) {
  return useQuery({
    queryKey: [...kpiKeys.card(id), 'performance', year],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/kpi/cards/{id}/performance', {
          params: { path: { id }, query: year ? { year } : {} },
          signal,
        }),
      ),
  });
}

export function useKpiVersions(id: string) {
  return useQuery({
    queryKey: [...kpiKeys.card(id), 'versions'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/cards/{id}/versions', { ...path(id), signal })),
  });
}

export function useKpiLinked(id: string) {
  return useQuery({
    queryKey: [...kpiKeys.card(id), 'linked'],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/cards/{id}/linked', { ...path(id), signal })),
  });
}

export function useKpiChanges(cardId?: string) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'changes', cardId],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/changes', { params: { query: cardId ? { cardId } : {} }, signal })),
  });
}

export function useKpiPlans(year: number, unitId?: string) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'plans', year, unitId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/kpi/plans', {
          params: { query: { year, ...(unitId ? { unitId } : {}) } },
          signal,
        }),
      ),
  });
}

export function useKpiPlan(id: string) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'plan', id],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/kpi/plans/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export function useKpiYearPreview(year: number | undefined) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'preview', year],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/kpi/years/{year}/preview', { params: { path: { year: year ?? 0 } }, signal }),
      ),
    enabled: !!year,
  });
}

export function useKpiWindows(year: number) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'windows', year],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/years/{year}/windows', { params: { path: { year } }, signal })),
  });
}

export function useKpiPending(view?: 'scope', year?: number) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'pending', view, year],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/kpi/results/pending', {
          params: { query: { ...(view ? { view } : {}), ...(year ? { year } : {}) } },
          signal,
        }),
      ),
  });
}

export function useKpiResult(id: string) {
  return useQuery({
    queryKey: kpiKeys.result(id),
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/kpi/results/{id}', { ...path(id), signal })),
    retry: false,
  });
}

export interface QueueQuery {
  year?: number;
  period?: string;
  unitId?: string;
  mine?: boolean;
}

export function useKpiQueue(query: QueueQuery, enabled = true) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'queue', query],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/approvals', { params: { query }, signal })),
    enabled,
  });
}

export function useKpiScorecard(year: number, unitId?: string) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'scorecard', year, unitId],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/kpi/scorecard', {
          params: { query: { year, ...(unitId ? { unitId } : {}) } },
          signal,
        }),
      ),
  });
}

export function useKpiStrategyMap(year: number) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'map', year],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/strategy/map', { params: { query: { year } }, signal })),
  });
}

export function useKpiDashboard(year: number, range?: { from: string; to: string }) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'dashboard', year, range ?? null],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/dashboard', { params: { query: { year, ...range } }, signal })),
  });
}

export function useKpiDataQuality(year: number, enabled = true) {
  return useQuery({
    queryKey: [...kpiKeys.all, 'quality', year],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/kpi/data-quality', { params: { query: { year } }, signal })),
    enabled,
  });
}

export function useKpiElements() {
  return useQuery({
    queryKey: [...kpiKeys.all, 'elements'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/kpi/strategy/elements', { signal })),
  });
}

export function useKpiSettings() {
  return useQuery({
    queryKey: [...kpiKeys.all, 'settings'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/kpi/settings', { signal })),
  });
}

/** A mutation that refreshes every KPI query (and workflow panels) afterwards. */
export function useKpiMutation<TInput, TOutput>(run: (input: TInput) => Promise<TOutput>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: run,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: kpiKeys.all });
      await queryClient.invalidateQueries({ queryKey: ['workflow'] });
    },
  });
}

export const kpiApi = {
  createCard: async (body: S['KpiCardInput']) => unwrap(await api.POST('/api/v1/kpi/cards', { body })),
  updateCard: async ({ id, card, version }: { id: string; card: S['KpiCardInput']; version: number }) =>
    unwrap(await api.PUT('/api/v1/kpi/cards/{id}', { ...path(id), body: { card, version } })),
  setCardStatus: async ({ id, active, version }: { id: string; active: boolean; version: number }) =>
    active
      ? unwrap(await api.POST('/api/v1/kpi/cards/{id}/activate', { ...path(id), body: { version } }))
      : unwrap(await api.POST('/api/v1/kpi/cards/{id}/deactivate', { ...path(id), body: { version } })),
  setTargets: async ({ id, targets }: { id: string; targets: KpiCardTarget[] }) =>
    unwrap(await api.PUT('/api/v1/kpi/cards/{id}/targets', { ...path(id), body: { targets } })),
  requestChange: async ({ id, proposed, reason }: { id: string; proposed: ProposedChange; reason: string }) =>
    unwrap(await api.POST('/api/v1/kpi/cards/{id}/changes', { ...path(id), body: { proposed, reason } })),
  withdrawChange: async ({ id, reason }: { id: string; reason: string | null }) =>
    unwrap(await api.POST('/api/v1/kpi/changes/{id}/withdraw', { ...path(id), body: { reason } })),
  startYear: async ({ year, items }: { year: number; items: KpiYearItemInput[] }) =>
    unwrap(await api.POST('/api/v1/kpi/years/{year}/start', { params: { path: { year } }, body: { items } })),
  confirmPlans: async (planIds: string[]) =>
    unwrap(await api.POST('/api/v1/kpi/plans/confirm', { body: { planIds } })),
  updatePlan: async ({ id, input }: { id: string; input: S['KpiPlanInput'] }) =>
    unwrap(await api.PUT('/api/v1/kpi/plans/{id}', { ...path(id), body: input })),
  saveWindows: async ({ year, windows }: { year: number; windows: S['KpiWindowInput'][] }) =>
    unwrap(
      await api.PUT('/api/v1/kpi/years/{year}/windows', { params: { path: { year } }, body: { windows } }),
    ),
  lockPeriod: async ({
    year,
    period,
    lock,
    reason,
  }: {
    year: number;
    period: string;
    lock: boolean;
    reason: string | null;
  }) =>
    lock
      ? unwrap(
          await api.POST('/api/v1/kpi/years/{year}/periods/{period}/lock', {
            params: { path: { year, period } },
            body: { reason },
          }),
        )
      : unwrap(
          await api.POST('/api/v1/kpi/years/{year}/periods/{period}/reopen', {
            params: { path: { year, period } },
            body: { reason },
          }),
        ),
  saveDraft: async ({ id, entry, version }: { id: string; entry: KpiEntryInput; version: number }) =>
    unwrap(await api.PUT('/api/v1/kpi/results/{id}/draft', { ...path(id), body: { entry, version } })),
  submit: async ({
    id,
    entry,
    version,
    comment,
  }: {
    id: string;
    entry: KpiEntryInput;
    version: number;
    comment: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/kpi/results/{id}/submit', { ...path(id), body: { entry, version, comment } }),
    ),
  recall: async ({ id, version }: { id: string; version: number }) =>
    unwrap(await api.POST('/api/v1/kpi/results/{id}/recall', { ...path(id), body: { version } })),
  decide: async ({
    id,
    action,
    comment,
    checklist,
  }: {
    id: string;
    action: 'approve' | 'return' | 'reject';
    comment: string | null;
    checklist: Record<string, boolean>;
  }) =>
    unwrap(
      await api.POST('/api/v1/kpi/results/{id}/decide', {
        ...path(id),
        body: { action, comment, checklist },
      }),
    ),
  reopen: async ({ id, reason }: { id: string; reason: string }) =>
    unwrap(await api.POST('/api/v1/kpi/results/{id}/reopen', { ...path(id), body: { reason } })),
  openNow: async (id: string) => unwrap(await api.POST('/api/v1/kpi/results/{id}/open-now', path(id))),
  bulkApprove: async (body: S['KpiBulkInput']) =>
    unwrap(await api.POST('/api/v1/kpi/approvals/bulk', { body })),
  saveElement: async (body: KpiElementInput) =>
    unwrap(await api.POST('/api/v1/kpi/strategy/elements', { body })),
  saveSettings: async ({ settings, version }: { settings: KpiSettingsValue; version: number }) =>
    unwrap(await api.PUT('/api/v1/kpi/settings', { body: { settings, version } })),
  // AI: drafts and advice only
  evidenceCheck: async (id: string) =>
    unwrap(await api.POST('/api/v1/kpi/ai/results/{id}/evidence-check', path(id))),
  causality: async ({ id, notes }: { id: string; notes: string | null }) =>
    unwrap(await api.POST('/api/v1/kpi/ai/results/{id}/causality', { ...path(id), body: { notes } })),
  buildCard: async (description: string) =>
    unwrap(await api.POST('/api/v1/kpi/ai/card-builder', { body: { description } })),
  buildCardFromDocument: async ({ file, description }: { file: File; description: string | null }) => {
    const form = new FormData();
    form.append('file', file);
    return unwrap(
      await api.POST('/api/v1/kpi/ai/card-builder/document', {
        params: { query: description ? { description } : {} },
        body: form as unknown as { file: string },
      }),
    );
  },
  smart: async (id: string) => unwrap(await api.GET('/api/v1/kpi/ai/cards/{id}/smart', path(id))),
  targetSuggestion: async ({ id, year }: { id: string; year: number }) =>
    unwrap(
      await api.GET('/api/v1/kpi/ai/cards/{id}/target-suggestion', {
        params: { path: { id }, query: { year } },
      }),
    ),
  narrative: async ({ year, unitId }: { year: number; unitId: string | null }) =>
    unwrap(await api.POST('/api/v1/kpi/ai/narrative', { body: { year, unitId } })),
};

export function exportUrl(
  kind: string,
  format: 'xlsx' | 'pdf',
  language: string,
  extra: Record<string, string> = {},
): string {
  const query = new URLSearchParams({ kind, format, language, ...extra });
  return `/api/v1/kpi/export?${query.toString()}`;
}

export const currentYear = (): number => new Date().getFullYear();

/** Targets in the KPI's unit: "85 – 95" for a range, "80" for a single value. */
export function targetText(
  format: (value: number) => string,
  targetType: string,
  target: number | null | undefined,
  min: number | null | undefined,
  max: number | null | undefined,
): string {
  if (targetType === 'range') return min == null || max == null ? '—' : `${format(min)} – ${format(max)}`;
  return target == null ? '—' : format(target);
}

/**
 * Live preview of the SPEC K5 calculation in the wizard (the server's PerformanceCalculator is the source of truth; this
 * mirrors it only to show the user what they are about to submit).
 */
export function previewPerformance(
  targetType: string,
  direction: string,
  target: number | null,
  min: number | null,
  max: number | null,
  achieve: number | null,
): { actual: number | null; capped: number | null } {
  if (achieve == null) return { actual: null, capped: null };
  let actual: number | null;
  if (targetType === 'range') {
    if (min == null || max == null || min > max) return { actual: null, capped: null };
    if (achieve >= min && achieve <= max) actual = 1;
    else if (achieve < min) actual = min > 0 ? achieve / min : null;
    else actual = achieve === 0 ? 1 : max / achieve;
  } else {
    if (target == null) return { actual: null, capped: null };
    if (target === 0) actual = achieve === 0 ? 1 : null;
    else if (direction === 'increase') actual = achieve / target;
    else if (direction === 'decrease') actual = achieve === 0 ? 1 : target / achieve;
    else if (direction === 'matching')
      actual = achieve === target ? 1 : achieve > target ? target / achieve : achieve / target;
    else actual = null;
  }
  if (actual == null) return { actual: null, capped: null };
  const rounded = Math.round(actual * 10000) / 10000;
  return { actual: rounded, capped: Math.min(rounded, 1) };
}

export function ragOf(capped: number | null, green: number, amber: number): string {
  if (capped == null) return 'none';
  if (capped >= green) return 'green';
  if (capped >= amber) return 'amber';
  return 'red';
}
