import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { reportingKeys } from './api';

// Reporting 2.0 (docs/design/reporting-2.md): report types, multi-object runs, folders, record dossiers.

export type UsableReportType = components['schemas']['UsableReportTypeDto'];
export type ReportTypeObject = components['schemas']['ReportTypeObjectDto'];
export type ReportTypeField = components['schemas']['ReportTypeFieldDto'];
export type ReportTypeRun = components['schemas']['ReportTypeRunDto'];
export type ReportGroup = components['schemas']['ReportGroupDto'];
export type ReportTypeAdmin = components['schemas']['ReportTypeDto'];
export type ReportTypeInput = components['schemas']['ReportTypeInput'];
export type ReportTypeNode = components['schemas']['ReportTypeNode'];
export type ReportableMetadata = components['schemas']['ReportableMetadataDto'];
export type ReportFolder = components['schemas']['ReportFolderDto'];
export type ReportSummary = components['schemas']['ReportSummary'];
export type ReportChartSpec = components['schemas']['ReportChart'];
export type ReportHighlight = components['schemas']['ReportHighlight'];
export type ReportFilter = components['schemas']['ReportFilter'];
export type ReportSort = components['schemas']['ReportSort'];
export type DossierTemplate = components['schemas']['DossierTemplateDto'];
export type DossierTemplateInput = components['schemas']['DossierTemplateInput'];
export type DossierDefinition = components['schemas']['DossierDefinition'];
export type DossierSection = components['schemas']['DossierSection'];
export type UsableDossierTemplate = components['schemas']['UsableDossierTemplateDto'];
export type DossierRecord = components['schemas']['DossierRecordDto'];

/** A type-based report definition (every part present, as the builder sends it). */
export interface TypeDefinition {
  reportType: string;
  dataset: string;
  columns: string[];
  filters: ReportFilter[];
  groupBy: string[];
  sort: ReportSort[];
  summaries: ReportSummary[];
  chart: ReportChartSpec | null;
  highlights: ReportHighlight[];
  showDetails: boolean;
}

export const reporting2Keys = {
  types: ['reporting', 'report-types'] as const,
  type: (code: string) => ['reporting', 'report-type', code] as const,
  adminTypes: ['reporting', 'admin', 'report-types'] as const,
  reportable: (kind: string) => ['reporting', 'reportable', kind] as const,
  folders: ['reporting', 'folders'] as const,
  dossierTemplates: ['reporting', 'dossier-templates'] as const,
  adminDossierTemplates: ['reporting', 'admin', 'dossier-templates'] as const,
  dossierRecords: (code: string, q: string) => ['reporting', 'dossier-records', code, q] as const,
};

export function useReportTypes() {
  return useQuery({
    queryKey: reporting2Keys.types,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/report-types', { signal })),
  });
}

export function useRunTypeReport() {
  return useMutation({
    mutationFn: async (input: {
      definition: TypeDefinition;
      page: number;
      pageSize: number;
      language: string;
    }) =>
      unwrap(
        await api.POST('/api/v1/reporting/reports/query', {
          body: {
            definition: input.definition,
            page: input.page,
            pageSize: input.pageSize,
            language: input.language,
          },
        }),
      ),
  });
}

export function useFolders() {
  return useQuery({
    queryKey: reporting2Keys.folders,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/folders', { signal })),
  });
}

export function useSaveFolder() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: { name: string; kind: 'private' | 'shared' }) =>
      unwrap(await api.POST('/api/v1/reporting/folders', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reporting2Keys.folders }),
  });
}

export function useShareFolder() {
  return useMutation({
    mutationFn: async ({ id, shares }: { id: string; shares: components['schemas']['ShareInput'][] }) =>
      unwrap(
        await api.PUT('/api/v1/reporting/folders/{id}/shares', { params: { path: { id } }, body: shares }),
      ),
  });
}

export function useMoveReport() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ id, folderId }: { id: string; folderId: string | null }) =>
      unwrap(
        await api.PUT('/api/v1/reporting/reports/{id}/folder', {
          params: { path: { id } },
          body: { folderId },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: reportingKeys.reports }),
  });
}

export function useSaveTypeReport(id?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: {
      name: string;
      definition: TypeDefinition;
      folderId: string | null;
      version?: number;
    }) => {
      const report = {
        name: input.name,
        description: null,
        definition: input.definition,
        folderId: input.folderId,
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

export function useAdminReportTypes(enabled = true) {
  return useQuery({
    queryKey: reporting2Keys.adminTypes,
    enabled,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/admin/report-types', { signal })),
  });
}

export function useReportable(kind: 'types' | 'dossiers', enabled = true) {
  return useQuery({
    queryKey: reporting2Keys.reportable(kind),
    enabled,
    staleTime: 5 * 60_000,
    queryFn: async ({ signal }) =>
      unwrap(
        kind === 'types'
          ? await api.GET('/api/v1/reporting/admin/reportable', { signal })
          : await api.GET('/api/v1/reporting/admin/dossier-reportable', { signal }),
      ),
  });
}

export function useSaveReportType(id?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ input, version }: { input: ReportTypeInput; version?: number }) =>
      id
        ? unwrap(
            await api.PUT('/api/v1/reporting/admin/report-types/{id}', {
              params: { path: { id } },
              body: { reportType: input, version: version ?? null },
            }),
          )
        : unwrap(await api.POST('/api/v1/reporting/admin/report-types', { body: input })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: reporting2Keys.adminTypes });
      void queryClient.invalidateQueries({ queryKey: reporting2Keys.types });
    },
  });
}

export function useDossierTemplates() {
  return useQuery({
    queryKey: reporting2Keys.dossierTemplates,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/reporting/dossier-templates', { signal })),
  });
}

export function useAdminDossierTemplates(enabled = true) {
  return useQuery({
    queryKey: reporting2Keys.adminDossierTemplates,
    enabled,
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/reporting/admin/dossier-templates', { signal })),
  });
}

export function useSaveDossierTemplate(id?: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ input, version }: { input: DossierTemplateInput; version?: number }) =>
      id
        ? unwrap(
            await api.PUT('/api/v1/reporting/admin/dossier-templates/{id}', {
              params: { path: { id } },
              body: { template: input, version: version ?? null },
            }),
          )
        : unwrap(await api.POST('/api/v1/reporting/admin/dossier-templates', { body: input })),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: reporting2Keys.adminDossierTemplates });
      void queryClient.invalidateQueries({ queryKey: reporting2Keys.dossierTemplates });
    },
  });
}

export function useDossierRecords(code: string | undefined, q: string) {
  return useQuery({
    queryKey: reporting2Keys.dossierRecords(code ?? '', q),
    enabled: !!code,
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/reporting/dossier-templates/{code}/records', {
          params: { path: { code: code ?? '' }, query: { q: q || undefined } },
          signal,
        }),
      ),
  });
}

/** Saves a server-generated file (dossier, export) under the name the server sent. */
export function saveBlob(blob: Blob, name: string): void {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = name;
  a.click();
  URL.revokeObjectURL(url);
}

/** One record's dossier (PDF or Word), as the caller. */
export async function renderDossier(
  code: string,
  recordId: string,
  format: 'pdf' | 'docx',
  language: string,
) {
  return api.POST('/api/v1/reporting/dossiers/{code}/render', {
    params: { path: { code } },
    body: { recordId, format, language },
    parseAs: 'blob',
  });
}

/** A filtered set of records → merged PDF or ZIP. */
export async function renderDossierSet(
  code: string,
  filters: ReportFilter[],
  format: 'pdf' | 'zip-pdf' | 'zip-docx',
  language: string,
) {
  return api.POST('/api/v1/reporting/dossiers/{code}/bulk', {
    params: { path: { code } },
    body: { filters, format, language },
    parseAs: 'blob',
  });
}
