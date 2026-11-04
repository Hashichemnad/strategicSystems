import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

type S = components['schemas'];
export type Catalogue = S['CatalogueDto'];
export type CatalogueType = S['CatalogueTypeDto'];
export type TypeForm = S['TypeFormDto'];
export type FormSchema = S['FormSchema'];
export type FormSection = S['FormSection'];
export type FormFieldDef = S['FormField'];
export type FieldCondition = S['FieldCondition'];
export type StageDefinition = S['StageDefinition'];
export type StageAction = S['StageAction'];
export type StageStep = S['StageStepDto'];
export type TypeDetail = S['TypeDetailDto'];
export type TypeInput = S['TypeInput'];
export type TypeSummary = S['TypeSummaryDto'];
export type TypeOptions = S['TypeOptionsDto'];
export type SimpleStages = S['SimpleStagesInput'];
export type PdfTemplate = S['PdfTemplate'];
export type SubmitterRule = S['SubmitterRule'];
export type FormFill = S['FormFillDto'];
export type BulkResult = S['BulkResultDto'];
export type ClassificationSuggestion = S['ClassificationSuggestionDto'];
export type DuplicateCheck = S['DuplicateCheckDto'];

/** Form values keyed by field code (jsonb on the server). */
export type FormValues = Record<string, unknown>;

export type RequestPerson = S['RequestPersonDto'];
export type StageActionView = S['StageActionDto'];
export type RequestCan = S['RequestActionsDto'];
export type RequestListItem = S['RequestRowDto'];

/** Form values (jsonb) are typed as free objects; the generated schema only knows them as JsonObject. */
export type RequestEvent = Omit<S['RequestEventDto'], 'data'> & { data: FormValues | null };

export type FrameworkRequest = Omit<S['RequestRecordDto'], 'data' | 'processData' | 'events'> & {
  data: FormValues;
  processData: Record<string, FormValues>;
  events: RequestEvent[];
};

export type RequestPage = S['PagedResultOfRequestRowDto'];

/** Resource / owner type of a request in the shared engine (approvals, status, files, links, tasks). */
export const requestResource = 'requests.request';

export const requestStatuses = [
  'draft',
  'in_review',
  'returned',
  'in_progress',
  'completed',
  'rejected',
  'cancelled',
] as const;

export const statusTone: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  draft: 'neutral',
  in_review: 'info',
  returned: 'warning',
  in_progress: 'info',
  completed: 'success',
  rejected: 'danger',
  cancelled: 'neutral',
};

export const requestKeys = {
  all: ['requests'] as const,
  catalogue: ['requests', 'catalogue'] as const,
  typeForm: (code: string) => ['requests', 'type-form', code] as const,
  mine: (query: object) => ['requests', 'mine', query] as const,
  queue: (query: object) => ['requests', 'queue', query] as const,
  request: (id: string) => ['requests', 'request', id] as const,
  types: ['requests', 'types'] as const,
  typeOptions: ['requests', 'types', 'options'] as const,
  type: (code: string) => ['requests', 'types', code] as const,
};

const asJson = (values: FormValues | null | undefined) =>
  (values ?? null) as unknown as S['JsonObject'] | null;
const asRequest = (value: unknown) => value as FrameworkRequest;

export function useCatalogue() {
  return useQuery({
    queryKey: requestKeys.catalogue,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/requests/catalogue', { signal })),
  });
}

export function useTypeForm(code: string) {
  return useQuery({
    queryKey: requestKeys.typeForm(code),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/requests/types/{code}/form', { params: { path: { code } }, signal })),
    retry: false,
  });
}

export interface ListQuery {
  page?: number;
  pageSize?: number;
  status?: string;
  type?: string;
  stage?: string;
  search?: string;
  overdue?: boolean;
}

export function useMyRequests(query: ListQuery) {
  return useQuery({
    queryKey: requestKeys.mine(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/requests/mine', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useRequestQueue(query: ListQuery) {
  return useQuery({
    queryKey: requestKeys.queue(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/requests/queue', { params: { query }, signal })),
    placeholderData: keepPreviousData,
  });
}

export function useFrameworkRequest(id: string | undefined) {
  return useQuery({
    queryKey: requestKeys.request(id ?? ''),
    queryFn: async ({ signal }) =>
      asRequest(
        unwrap(await api.GET('/api/v1/requests/{id}', { params: { path: { id: id ?? '' } }, signal })),
      ),
    enabled: Boolean(id),
  });
}

export function useRequestTypes() {
  return useQuery({
    queryKey: requestKeys.types,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/requests/admin/types', { signal })),
  });
}

export function useTypeOptions() {
  return useQuery({
    queryKey: requestKeys.typeOptions,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/requests/admin/types/options', { signal })),
    staleTime: 60_000,
  });
}

export function useRequestType(code: string | undefined) {
  return useQuery({
    queryKey: requestKeys.type(code ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/requests/admin/types/{code}', {
          params: { path: { code: code ?? '' } },
          signal,
        }),
      ),
    enabled: Boolean(code),
  });
}

const idem = () => ({ 'Idempotency-Key': crypto.randomUUID() });

/** Every request command; each returns the request as it now stands. */
export const requestApi = {
  create: async ({
    typeCode,
    data,
    source,
  }: {
    typeCode: string;
    data?: FormValues | null;
    source?: string;
  }) =>
    asRequest(
      unwrap(
        await api.POST('/api/v1/requests', {
          body: { typeCode, data: asJson(data), source: source ?? null },
        }),
      ),
    ),
  save: async ({ id, data, version }: { id: string; data: FormValues; version: number }) =>
    asRequest(
      unwrap(
        await api.PUT('/api/v1/requests/{id}', {
          params: { path: { id } },
          body: { data: asJson(data), version },
        }),
      ),
    ),
  submit: async ({ id, version, comment }: { id: string; version: number; comment?: string | null }) =>
    asRequest(
      unwrap(
        await api.POST('/api/v1/requests/{id}/submit', {
          params: { path: { id } },
          body: { version, comment: comment ?? null },
          headers: idem(),
        }),
      ),
    ),
  cancel: async ({ id, version, reason }: { id: string; version: number; reason: string }) =>
    asRequest(
      unwrap(
        await api.POST('/api/v1/requests/{id}/cancel', {
          params: { path: { id } },
          body: { reason, version },
        }),
      ),
    ),
  decide: async ({
    id,
    action,
    comment,
    form,
  }: {
    id: string;
    action: string;
    comment: string | null;
    form: FormValues | null;
  }) =>
    asRequest(
      unwrap(
        await api.POST('/api/v1/requests/{id}/decide', {
          params: { path: { id } },
          body: { action, comment, form: asJson(form) },
          headers: idem(),
        }),
      ),
    ),
  act: async ({
    id,
    action,
    version,
    comment,
    form,
  }: {
    id: string;
    action: string;
    version: number;
    comment: string | null;
    form: FormValues | null;
  }) =>
    asRequest(
      unwrap(
        await api.POST('/api/v1/requests/{id}/actions/{action}', {
          params: { path: { id, action } },
          body: { version, comment, form: asJson(form) },
          headers: idem(),
        }),
      ),
    ),
  bulkComplete: async ({ ids, comment }: { ids: string[]; comment: string | null }) =>
    unwrap(
      await api.POST('/api/v1/requests/queue/bulk-complete', { body: { ids, action: 'complete', comment } }),
    ),
  aiFill: async ({ typeCode, file }: { typeCode: string; file: File }) => {
    const form = new FormData();
    form.append('file', file);
    return unwrap(
      await api.POST('/api/v1/requests/types/{code}/ai-fill', {
        params: { path: { code: typeCode } },
        body: form as unknown as { file: string },
        bodySerializer: (body: unknown) => body as FormData,
      }),
    );
  },
  /** Uploads a file to the request (owner `requests.request`); form file fields hold the returned id. */
  upload: async ({ requestId, file }: { requestId: string; file: File }) => {
    const form = new FormData();
    form.append('file', file);
    return unwrap(
      await api.POST('/api/v1/files/owners/{ownerType}/{ownerId}', {
        params: { path: { ownerType: requestResource, ownerId: requestId } },
        body: form as unknown as { file: string },
        bodySerializer: (body: unknown) => body as FormData,
      }),
    );
  },
  classify: async ({ id }: { id: string }) =>
    unwrap(
      await api.POST('/api/v1/docreg/ai/requests/{requestId}/classify', {
        params: { path: { requestId: id } },
      }),
    ),
  duplicates: async ({ id }: { id: string }) =>
    unwrap(
      await api.POST('/api/v1/docreg/ai/requests/{requestId}/duplicates', {
        params: { path: { requestId: id } },
      }),
    ),
};

/** Wraps a request command: the request, the lists, the status engine and the files are refreshed afterwards. */
export function useRequestMutation<TArgs, TResult>(fn: (args: TArgs) => Promise<TResult>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async () => {
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: requestKeys.all }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
        queryClient.invalidateQueries({ queryKey: ['files'] }),
      ]);
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

/** Downloads a PDF template rendered for the request (audited as an export on the server). */
export async function downloadRequestPdf(id: string, template: string, language: string, fileName: string) {
  const result = await api.GET('/api/v1/requests/{id}/pdf/{template}', {
    params: { path: { id, template }, query: { language } },
    parseAs: 'blob',
  });
  const blob = unwrap(result);
  saveBlob(blob, fileName);
}

/** Renders a template with sample values and opens it in a new tab. */
export async function previewTypePdf(code: string, template: PdfTemplate, language: string) {
  const result = await api.POST('/api/v1/requests/admin/types/{code}/preview-pdf', {
    params: { path: { code } },
    body: { template, language },
    parseAs: 'blob',
  });
  const blob = unwrap(result);
  const url = URL.createObjectURL(blob);
  window.open(url, '_blank', 'noopener');
  window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
}

export function useSaveType(code: string | undefined) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: TypeInput) =>
      code
        ? unwrap(
            await api.PUT('/api/v1/requests/admin/types/{code}', { params: { path: { code } }, body: input }),
          )
        : unwrap(await api.POST('/api/v1/requests/admin/types', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: requestKeys.all }),
  });
}

/** A document of the library, for record fields that point at `docreg.document`. */
export function useDocumentSearch(search: string, enabled: boolean) {
  return useQuery({
    queryKey: ['requests', 'record-search', 'docreg.document', search],
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/docreg/library', { params: { query: { search, pageSize: 8 } }, signal })),
    enabled,
  });
}

export function useLinkView(type: string | null | undefined, id: string | null | undefined) {
  return useQuery({
    queryKey: ['links', 'resolve', type, id],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/links/resolve', {
          params: { query: { type: type ?? '', id: id ?? '' } },
          signal,
        }),
      ),
    enabled: Boolean(type && id),
    retry: false,
  });
}
