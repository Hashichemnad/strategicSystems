import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

type S = components['schemas'];
export type GanttData = S['GanttDto'];
export type GanttRowData = S['GanttRowDto'];
export type PortfolioDashboard = S['PortfolioDashboardDto'];
export type DashboardProject = S['DashboardProjectDto'];
export type HeatCell = S['HeatCellDto'];
export type PortfolioGroup = S['PortfolioGroupDto'];
export type PortfolioGroupValues = S['PortfolioGroupValues'];
export type Insights = S['InsightsDto'];

/** Portfolio / programme / unit filter shared by the dashboard, the portfolio Gantt and insights. */
export interface PortfolioFilter {
  portfolioGroupId?: string | undefined;
  programmeId?: string | undefined;
  unitId?: string | undefined;
}

// The generated schema merges the project template content with another module's `TemplateContent`
// (same C# record name); the portfolio shape is declared here from Domain/Standards.cs until the generator disambiguates.
export interface TemplateMilestone {
  titleAr: string | null;
  titleEn: string | null;
  /** Ratio 0–1 (headers carry 0). */
  weight: number;
  offsetDays: number;
  durationDays: number;
  isHeader?: boolean;
  parentIndex?: number | null;
}
export interface TemplatePlanningItem {
  category: string;
  description: string;
}
export interface TemplateRisk {
  title: string;
  categoryCode: string | null;
  probability: number;
  impact: number;
}
export interface TemplateEpic {
  titleAr: string | null;
  titleEn: string | null;
  weight: number;
}
export interface ProjectTemplateContent {
  milestones?: TemplateMilestone[] | null;
  planningItems?: TemplatePlanningItem[] | null;
  risks?: TemplateRisk[] | null;
  epics?: TemplateEpic[] | null;
}
export interface ProjectTemplate {
  id: string;
  code: string;
  nameAr: string | null;
  nameEn: string | null;
  methodology: string;
  content: ProjectTemplateContent;
  status: string;
  version: number;
}
export interface ProjectTemplateInput {
  code: string;
  nameAr: string | null;
  nameEn: string | null;
  methodology: string;
  content: ProjectTemplateContent;
  version?: number | null;
}
type GeneratedTemplateInput = S['ProjectTemplateInput'];

export const planningCategories = [
  'in-scope',
  'out-of-scope',
  'assumption',
  'constraint',
  'deliverable',
] as const;
export const methodologies = ['waterfall', 'agile', 'hybrid'] as const;

export const viewKeys = {
  projectGantt: (id: string) => ['portfolio', 'project', id, 'gantt'] as const,
  gantt: (filter: PortfolioFilter) => ['portfolio', 'gantt', filter] as const,
  dashboard: (filter: PortfolioFilter) => ['portfolio', 'dashboard', filter] as const,
  groups: ['portfolio', 'groups'] as const,
  templates: ['portfolio', 'templates', 'all'] as const,
};

/** Drops empty values so the query string and cache key stay stable. */
export function cleanFilter(filter: PortfolioFilter): PortfolioFilter {
  const out: PortfolioFilter = {};
  if (filter.portfolioGroupId) out.portfolioGroupId = filter.portfolioGroupId;
  if (filter.programmeId) out.programmeId = filter.programmeId;
  if (filter.unitId) out.unitId = filter.unitId;
  return out;
}

export function useProjectGantt(id: string) {
  return useQuery({
    queryKey: viewKeys.projectGantt(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/projects/{id}/gantt', { params: { path: { id } }, signal })),
  });
}

export function usePortfolioGantt(filter: PortfolioFilter) {
  const query = cleanFilter(filter);
  return useQuery({
    queryKey: viewKeys.gantt(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/gantt', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function usePortfolioDashboard(filter: PortfolioFilter) {
  const query = cleanFilter(filter);
  return useQuery({
    queryKey: viewKeys.dashboard(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/dashboard', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

/** AI portfolio insights: a draft narrative the user reads (nothing is saved). */
export function usePortfolioInsights() {
  return useMutation({
    mutationFn: async (filter: PortfolioFilter) =>
      unwrap(await api.POST('/api/v1/portfolio/ai/insights', { params: { query: cleanFilter(filter) } })),
  });
}

export function usePortfolioGroups(enabled = true) {
  return useQuery({
    queryKey: viewKeys.groups,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/portfolio/groups', { signal })),
    enabled,
    staleTime: 30_000,
  });
}

export function useSavePortfolioGroup() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      id,
      values,
      version,
    }: {
      id?: string | undefined;
      values: PortfolioGroupValues;
      version?: number | undefined;
    }) =>
      id
        ? unwrap(
            await api.PUT('/api/v1/portfolio/groups/{gid}', {
              params: { path: { gid: id } },
              body: { values, version: version ?? null },
            }),
          )
        : unwrap(await api.POST('/api/v1/portfolio/groups', { body: { values } })),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: viewKeys.groups }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'dashboard'] }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'gantt'] }),
      ]);
    },
  });
}

export function useSetGrouping(projectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: {
      portfolioGroupId: string | null;
      programmeId: string | null;
      version: number;
    }) =>
      unwrap(
        await api.PUT('/api/v1/portfolio/projects/{id}/grouping', {
          params: { path: { id: projectId } },
          body,
        }),
      ),
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'project', projectId] }),
        queryClient.invalidateQueries({ queryKey: viewKeys.groups }),
      ]);
    },
  });
}

export function useProjectTemplates(enabled = true) {
  return useQuery({
    queryKey: viewKeys.templates,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/templates', { params: { query: { all: true } }, signal }),
      ) as unknown as ProjectTemplate[],
    enabled,
  });
}

export function useSaveProjectTemplate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, input }: { id?: string | undefined; input: ProjectTemplateInput }) => {
      const body = input as unknown as GeneratedTemplateInput;
      const result = id
        ? unwrap(await api.PUT('/api/v1/portfolio/templates/{tid}', { params: { path: { tid: id } }, body }))
        : unwrap(await api.POST('/api/v1/portfolio/templates', { body }));
      return result;
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['portfolio', 'templates'] });
    },
  });
}

export function useSetTemplateActive() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, active }: { id: string; active: boolean }) =>
      unwrap(
        await api.POST('/api/v1/portfolio/templates/{tid}/active/{active}', {
          params: { path: { tid: id, active } },
        }),
      ) as unknown as ProjectTemplate,
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['portfolio', 'templates'] });
    },
  });
}

/** Overall RAG of a dashboard project (effective value), or null when health is off / unknown. */
export function overallRag(project: Pick<DashboardProject, 'health'>): string | null {
  return project.health?.overall.effective ?? null;
}

/** Projects table / chips: the methodology tone. */
export const methodologyTone: Record<string, 'neutral' | 'info' | 'success' | 'warning'> = {
  waterfall: 'neutral',
  agile: 'info',
  hybrid: 'warning',
};

export const ragTone: Record<string, 'success' | 'warning' | 'danger' | 'neutral'> = {
  green: 'success',
  amber: 'warning',
  red: 'danger',
  grey: 'neutral',
};
