import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type DashboardSummary = components['schemas']['DashboardSummaryDto'];
export type Dashboard = components['schemas']['ReportingDashboardDto'];
export type DashboardData = components['schemas']['DashboardDataDto'];
export type DashboardInput = components['schemas']['DashboardInput'];
/** Widget configuration; the server always sends identity and layout (C# init defaults make them optional in the schema). */
export type WidgetConfig = components['schemas']['WidgetConfig'] & {
  id: string;
  type: string;
  x: number;
  y: number;
  w: number;
  h: number;
};
export type DashboardDefinition = { widgets: WidgetConfig[] };
export type WidgetData = components['schemas']['WidgetDataDto'];
export type ScorecardData = components['schemas']['ScorecardDto'];
export type Catalogue = components['schemas']['ReportingCatalogueDto'];
export type MetricCatalogue = components['schemas']['MetricCatalogueDto'];
export type MyWorkCards = components['schemas']['MyWorkCardsDto'];
export type MyWorkCardData = components['schemas']['MyWorkCardDto'];
export type Dataset = components['schemas']['DatasetDto'];
export type ReportDefinition = Required<components['schemas']['ReportDefinition']>;
export type ReportRun = components['schemas']['ReportRunDto'];
export type SavedReport = components['schemas']['SavedReportDto'];
export type Schedule = components['schemas']['ScheduleDto'];
export type ScheduleInput = components['schemas']['ScheduleInput'];
export type Delivery = components['schemas']['ScheduleDeliveryDto'];
export type Brief = components['schemas']['BriefDto'];
export type BriefInput = components['schemas']['BriefInput'];
export type ReportingSettingsDoc = components['schemas']['SettingsDocumentOfReportingSettings'];
export type ReportingSettings = Required<components['schemas']['ReportingSettings']>;
export type ShareInput = components['schemas']['ShareInput'];

/** The period a dashboard shows (whole months, ADR 0142). */
export interface PeriodQuery {
  kind: 'month' | 'quarter' | 'year' | 'custom';
  from?: string;
  to?: string;
  compare: boolean;
}

export const reportingKeys = {
  all: ['reporting'] as const,
  dashboards: ['reporting', 'dashboards'] as const,
  dashboard: (key: string) => ['reporting', 'dashboard', key] as const,
  data: (key: string, period: PeriodQuery, unit?: string, draft?: boolean) =>
    ['reporting', 'data', key, period, unit ?? null, draft ?? false] as const,
  catalogue: ['reporting', 'catalogue'] as const,
  myWork: ['reporting', 'my-work'] as const,
  pins: ['reporting', 'pins'] as const,
  datasets: ['reporting', 'datasets'] as const,
  reports: ['reporting', 'reports'] as const,
  report: (id: string) => ['reporting', 'report', id] as const,
  schedules: ['reporting', 'schedules'] as const,
  deliveries: (id: string) => ['reporting', 'deliveries', id] as const,
  briefs: ['reporting', 'briefs'] as const,
  settings: ['reporting', 'settings'] as const,
  refresh: ['reporting', 'refresh'] as const,
  filters: (id: string) => ['reporting', 'filters', id] as const,
};

export function useDashboards() {
  return useQuery({
    queryKey: reportingKeys.dashboards,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/dashboards', { signal })),
  });
}

export function useDashboard(key: string) {
  return useQuery({
    queryKey: reportingKeys.dashboard(key),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/reporting/dashboards/{key}', { params: { path: { key } }, signal })),
  });
}

export function useDashboardData(key: string, period: PeriodQuery, unit?: string, draft?: boolean) {
  return useQuery({
    queryKey: reportingKeys.data(key, period, unit, draft),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/reporting/dashboards/{key}/data', {
          params: {
            path: { key },
            query: {
              kind: period.kind,
              from: period.from,
              to: period.to,
              compare: period.compare,
              unit,
              draft,
            },
          },
          signal,
        }),
      ),
    placeholderData: keepPreviousData,
    // Facts are refreshed about every minute; a visible dashboard re-reads every five (docs/design/reporting.md §15).
    refetchInterval: 5 * 60 * 1000,
  });
}

export function useCatalogue(enabled = true) {
  return useQuery({
    queryKey: reportingKeys.catalogue,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/catalogue', { signal })),
    enabled,
    staleTime: 5 * 60 * 1000,
  });
}

export function useSaveDashboard(id?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ input, version }: { input: DashboardInput; version?: number }) =>
      id
        ? unwrap(
            await api.PUT('/api/v1/reporting/dashboards/{id}', {
              params: { path: { id } },
              body: { dashboard: input, version: version ?? 0 },
            }),
          )
        : unwrap(await api.POST('/api/v1/reporting/dashboards', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function usePublishDashboard() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.POST('/api/v1/reporting/dashboards/{id}/publish', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function useSetDashboardRoles() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      roles,
    }: {
      id: string;
      roles: { roleKey: string; sort: number; isDefault: boolean }[];
    }) =>
      unwrap(
        await api.PUT('/api/v1/reporting/dashboards/{id}/roles', { params: { path: { id } }, body: roles }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function useMyWorkCards() {
  return useQuery({
    queryKey: reportingKeys.myWork,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/my-work/cards', { signal })),
  });
}

export function useSaveMyWorkLayout() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (cards: { code: string; hidden: boolean }[]) =>
      unwrap(await api.PUT('/api/v1/reporting/my-work/layout', { body: cards })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.myWork }),
  });
}

export function usePins() {
  return useQuery({
    queryKey: reportingKeys.pins,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/pins', { signal })),
  });
}

export function usePin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: {
      kind: 'dashboard' | 'widget' | 'report';
      refId: string;
      widgetId?: string | null;
    }) =>
      unwrap(
        await api.POST('/api/v1/reporting/pins', {
          body: { kind: input.kind, refId: input.refId, widgetId: input.widgetId ?? null },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function useUnpin() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (id: string) =>
      unwrap(await api.DELETE('/api/v1/reporting/pins/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function useSavedFilters(dashboardId: string | undefined) {
  return useQuery({
    queryKey: reportingKeys.filters(dashboardId ?? ''),
    enabled: !!dashboardId,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/reporting/dashboards/{id}/filters', {
          params: { path: { id: dashboardId ?? '' } },
          signal,
        }),
      ),
  });
}

export function useSaveFilter(dashboardId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { name: string; filters: Record<string, unknown>; isDefault: boolean }) =>
      unwrap(
        await api.POST('/api/v1/reporting/dashboards/{id}/filters', {
          params: { path: { id: dashboardId } },
          body: { name: input.name, filters: input.filters, isDefault: input.isDefault },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.filters(dashboardId) }),
  });
}

export function useDatasets() {
  return useQuery({
    queryKey: reportingKeys.datasets,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/datasets', { signal })),
  });
}

export function useRunReport() {
  return useMutation({
    mutationFn: async (input: {
      definition: ReportDefinition;
      page: number;
      pageSize: number;
      language: string;
    }) => unwrap(await api.POST('/api/v1/reporting/reports/run', { body: input })),
  });
}

export function useReports() {
  return useQuery({
    queryKey: reportingKeys.reports,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/reports', { signal })),
  });
}

export function useReport(id: string | undefined) {
  return useQuery({
    queryKey: reportingKeys.report(id ?? ''),
    enabled: !!id,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/reporting/reports/{id}', { params: { path: { id: id ?? '' } }, signal })),
  });
}

export function useSaveReport(id?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: {
      name: string;
      description?: string | null;
      definition: ReportDefinition;
      version?: number;
    }) => {
      const report = {
        name: input.name,
        description: input.description ?? null,
        definition: input.definition,
      };
      return id
        ? unwrap(
            await api.PUT('/api/v1/reporting/reports/{id}', {
              params: { path: { id } },
              body: { report, version: input.version ?? null },
            }),
          )
        : unwrap(await api.POST('/api/v1/reporting/reports', { body: report }));
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function useShareReport() {
  return useMutation({
    mutationFn: async ({ id, shares }: { id: string; shares: ShareInput[] }) =>
      unwrap(
        await api.PUT('/api/v1/reporting/reports/{id}/shares', { params: { path: { id } }, body: shares }),
      ),
  });
}

export function useSchedules(enabled = true) {
  return useQuery({
    queryKey: reportingKeys.schedules,
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/schedules', { signal })),
  });
}

export function useSaveSchedule(id?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ input, version }: { input: ScheduleInput; version?: number }) =>
      id
        ? unwrap(
            await api.PUT('/api/v1/reporting/schedules/{id}', {
              params: { path: { id } },
              body: { schedule: input, version: version ?? null },
            }),
          )
        : unwrap(await api.POST('/api/v1/reporting/schedules', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.schedules }),
  });
}

export function useDeliveries(id: string | undefined) {
  return useQuery({
    queryKey: reportingKeys.deliveries(id ?? ''),
    enabled: !!id,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/reporting/schedules/{id}/deliveries', {
          params: { path: { id: id ?? '' } },
          signal,
        }),
      ),
  });
}

export function useGenerateBrief() {
  return useMutation({
    mutationFn: async (input: { kind: string; from?: string; to?: string; unitId?: string }) =>
      unwrap(
        await api.POST('/api/v1/reporting/brief', {
          body: {
            kind: input.kind,
            from: input.from ?? null,
            to: input.to ?? null,
            unitId: input.unitId ?? null,
          },
        }),
      ),
  });
}

export function useSaveBrief() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: BriefInput) =>
      unwrap(await api.POST('/api/v1/reporting/briefs', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.briefs }),
  });
}

export function useReportingSettings() {
  return useQuery({
    queryKey: reportingKeys.settings,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/settings', { signal })),
  });
}

export function useSaveReportingSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ settings, version }: { settings: ReportingSettings; version: number }) =>
      unwrap(await api.PUT('/api/v1/reporting/settings', { body: { settings, version } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.all }),
  });
}

export function useRefreshStatus(enabled = true) {
  return useQuery({
    queryKey: reportingKeys.refresh,
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/refresh', { signal })),
  });
}

export function useRequestRefresh() {
  return useMutation({
    mutationFn: async () => unwrap(await api.POST('/api/v1/reporting/refresh')),
  });
}

/** Downloads a server export (dashboard or report) through the authorised endpoint. */
export async function downloadBlob(response: Response, fallbackName: string): Promise<void> {
  const blob = await response.blob();
  const disposition = response.headers.get('Content-Disposition') ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const name = match?.[1] ? decodeURIComponent(match[1]) : fallbackName;
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}
