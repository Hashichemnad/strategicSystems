import { useQuery } from '@tanstack/react-query';
import createClient, { type Middleware } from 'openapi-fetch';
import { unwrap } from '@/api/client';
import type { components, paths } from '@/api/schema';
import i18n from '@/i18n';

/**
 * The Wasel vendor portal's own HTTP client (ADR 0110). It never shares the internal client's antiforgery token or its 401
 * handling: the vendor session lives on /api/wasel only, and an expired vendor session goes back to /wasel/login.
 */
export const waselApi = createClient<paths>({ baseUrl: '' });

type S = components['schemas'];
export type VendorPortalMe = S['VendorPortalMeDto'];
export type VendorProject = S['VendorProjectDto'];
export type VendorMilestone = S['VendorMilestoneDto'];
export type VendorSubmission = S['VendorSubmissionDto'];
export type VendorSubmissionItem = S['VendorSubmissionItemDto'];
export type VendorProgressValues = S['VendorProgressValues'];
export type VendorProposals = S['VendorProposalsDto'];
export type VendorProposal = S['VendorProposalDto'];
export type ProposalItem = S['ProposalItem'];
export type ProposalDiffEntry = S['ProposalDiffEntry'];
export type DiffEntry = S['DiffEntry'];
export type VendorDeliverable = S['VendorDeliverableDto'];
export type VendorFile = S['VendorFileDto'];
export type VendorItem = S['VendorItemDto'];
export type VendorMeeting = S['VendorMeetingDto'];
export type WaselComment = S['WaselCommentDto'];
export type SubmissionHistory = S['VendorSubmissionHistoryDto'];

const UNSAFE = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const XSRF_HEADER = 'X-XSRF-TOKEN';
const TOKEN_PATH = '/api/wasel/v1/antiforgery/token';

let token: Promise<string> | null = null;

/** The token is bound to the vendor identity: drop it after signing in or out. */
export function resetWaselAntiforgery(): void {
  token = null;
}

function antiforgery(): Promise<string> {
  token ??= waselApi
    .GET(TOKEN_PATH)
    .then(({ data }) => {
      if (!data) throw new Error('Antiforgery token unavailable');
      return data.token;
    })
    .catch((error: unknown) => {
      token = null;
      throw error;
    });
  return token;
}

/** Calls whose 401 is a normal answer (sign-in steps, session probe). */
const PROBES = ['/api/wasel/v1/auth/', '/api/wasel/v1/me'];
const AUTH_PAGES = ['/wasel/login', '/wasel/set-password'];

const middleware: Middleware = {
  async onRequest({ request, schemaPath }) {
    request.headers.set('X-Client', 'spa');
    request.headers.set('X-Correlation-Id', crypto.randomUUID().replaceAll('-', ''));
    request.headers.set('Accept-Language', i18n.language);
    if (UNSAFE.has(request.method) && schemaPath !== TOKEN_PATH) {
      request.headers.set(XSRF_HEADER, await antiforgery());
    }
    return request;
  },
  onResponse({ response, schemaPath }) {
    if (
      response.status === 401 &&
      !PROBES.some((probe) => schemaPath.startsWith(probe)) &&
      !AUTH_PAGES.some((page) => window.location.pathname.startsWith(page))
    ) {
      window.location.assign(`/wasel/login?redirect=${encodeURIComponent(window.location.pathname)}`);
    }
    if (response.status === 400 && schemaPath !== TOKEN_PATH) token = null;
    return response;
  },
};

waselApi.use(middleware);

export const waselKeys = {
  me: ['wasel', 'me'] as const,
  portal: ['wasel', 'portal'] as const,
  project: (id: string) => ['wasel', 'project', id] as const,
  milestones: (id: string) => ['wasel', 'project', id, 'milestones'] as const,
  proposals: (id: string) => ['wasel', 'project', id, 'proposals'] as const,
  submission: (id: string) => ['wasel', 'project', id, 'submission'] as const,
  history: (id: string) => ['wasel', 'project', id, 'history'] as const,
  deliverables: (id: string) => ['wasel', 'project', id, 'deliverables'] as const,
  items: (id: string) => ['wasel', 'project', id, 'items'] as const,
  meetings: (id: string) => ['wasel', 'project', id, 'meetings'] as const,
  comments: (id: string, type: string, subject: string) =>
    ['wasel', 'project', id, 'comments', type, subject] as const,
};

const p = (id: string) => ({ params: { path: { id } } });

export function useWaselMe(enabled = true) {
  return useQuery({
    queryKey: waselKeys.me,
    queryFn: async ({ signal }) => unwrap(await waselApi.GET('/api/wasel/v1/me', { signal })),
    retry: false,
    enabled,
  });
}

export function useWaselPortal() {
  return useQuery({
    queryKey: waselKeys.portal,
    queryFn: async ({ signal }) => unwrap(await waselApi.GET('/api/wasel/v1/portal', { signal })),
  });
}

export function useVendorProject(id: string) {
  return useQuery({
    queryKey: waselKeys.project(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}', { ...p(id), signal })),
    retry: false,
  });
}

export function useVendorMilestones(id: string) {
  return useQuery({
    queryKey: waselKeys.milestones(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/milestones', { ...p(id), signal })),
  });
}

export function useVendorProposals(id: string) {
  return useQuery({
    queryKey: waselKeys.proposals(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/proposals', { ...p(id), signal })),
  });
}

export function useVendorSubmission(id: string) {
  return useQuery({
    queryKey: waselKeys.submission(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/submission', { ...p(id), signal })),
  });
}

export function useSubmissionHistory(id: string) {
  return useQuery({
    queryKey: waselKeys.history(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/submissions', { ...p(id), signal })),
  });
}

export function useVendorDeliverables(id: string) {
  return useQuery({
    queryKey: waselKeys.deliverables(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/deliverables', { ...p(id), signal })),
  });
}

export function useVendorItems(id: string) {
  return useQuery({
    queryKey: waselKeys.items(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/items', { ...p(id), signal })),
  });
}

export function useVendorMeetings(id: string) {
  return useQuery({
    queryKey: waselKeys.meetings(id),
    queryFn: async ({ signal }) =>
      unwrap(await waselApi.GET('/api/wasel/v1/projects/{id}/meetings', { ...p(id), signal })),
  });
}

export function useVendorComments(id: string, subjectType: string, subjectId: string) {
  return useQuery({
    queryKey: waselKeys.comments(id, subjectType, subjectId),
    queryFn: async ({ signal }) =>
      unwrap(
        await waselApi.GET('/api/wasel/v1/projects/{id}/comments', {
          params: { path: { id }, query: { subjectType, subjectId } },
          signal,
        }),
      ),
    enabled: Boolean(subjectId),
  });
}

/** Multipart body for openapi-fetch (the generated type describes the fields; the browser builds the boundary). */
export function formBody(fields: Record<string, string | File | undefined>) {
  const form = new FormData();
  for (const [key, value] of Object.entries(fields)) {
    if (value !== undefined && value !== '') form.append(key, value);
  }
  return { body: form as never, bodySerializer: (body: unknown) => body as FormData };
}

export function fileUrl(projectId: string, fileId: string): string {
  return `/api/wasel/v1/projects/${projectId}/files/${fileId}`;
}
