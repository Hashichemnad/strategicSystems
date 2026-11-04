import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

type S = components['schemas'];
export type WaselPanel = S['WaselPanelDto'];
export type WaselAssignment = S['WaselAssignmentDto'];
export type WaselMember = S['WaselMemberDto'];
export type PmProposal = S['PmProposalDto'];
export type ProposalItem = S['ProposalItem'];
export type ProposalDiffEntry = S['ProposalDiffEntry'];
export type PmSubmission = S['PmSubmissionDto'];
export type PmSubmissionItem = S['PmSubmissionItemDto'];
export type VendorProgressValues = S['VendorProgressValues'];
export type DiffEntry = S['DiffEntry'];
export type PmDeliverable = S['PmDeliverableDto'];
export type PmVendorItem = S['PmVendorItemDto'];
export type VendorRating = S['VendorRatingDto'];
export type WaselComment = S['WaselCommentDto'];
export type VendorReviewAi = S['VendorReviewAiDto'];
export type VendorListItem = S['VendorListItemDto'];
export type VendorDetail = S['VendorDetailDto'];
export type VendorValues = S['VendorValues'];
export type VendorFile = S['VendorFileDto'];

export const waselKeys = {
  panel: (projectId: string) => ['portfolio', 'project', projectId, 'wasel'] as const,
  comments: (projectId: string, subjectType: string, subjectId: string) =>
    ['portfolio', 'project', projectId, 'wasel', 'comments', subjectType, subjectId] as const,
  vendors: (search: string, status: string) => ['portfolio', 'vendors', search, status] as const,
  vendor: (id: string) => ['portfolio', 'vendors', 'detail', id] as const,
};

const idem = () => ({ 'Idempotency-Key': crypto.randomUUID() });

export function useWaselPanel(projectId: string) {
  return useQuery({
    queryKey: waselKeys.panel(projectId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/wasel', {
          params: { path: { id: projectId } },
          signal,
        }),
      ),
  });
}

/** Every panel mutation answers the updated panel: it is written into the cache, and the project / plan reload. */
export function usePanelMutation<TArgs>(projectId: string, fn: (args: TArgs) => Promise<WaselPanel>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async (panel) => {
      queryClient.setQueryData(waselKeys.panel(projectId), panel);
      await Promise.all([
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'project', projectId] }),
        queryClient.invalidateQueries({ queryKey: ['portfolio', 'milestone'] }),
        queryClient.invalidateQueries({ queryKey: ['workflow'] }),
      ]);
    },
  });
}

export const waselApi = {
  setMode: async (a: { projectId: string; mode: string; version: number }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/projects/{id}/wasel/mode', {
        params: { path: { id: a.projectId } },
        body: { mode: a.mode, version: a.version },
      }),
    ),
  assign: async (a: {
    projectId: string;
    vendorId: string;
    contractId: string | null;
    accessUntil: string | null;
    expiryRule: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/wasel/assignments', {
        params: { path: { id: a.projectId } },
        body: {
          vendorId: a.vendorId,
          contractId: a.contractId,
          accessUntil: a.accessUntil,
          expiryRule: a.expiryRule,
        },
      }),
    ),
  invite: async (a: {
    projectId: string;
    vendorId: string;
    email: string;
    name: string | null;
    role: string;
    language: string | null;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/wasel/invitations', {
        params: { path: { id: a.projectId } },
        body: { vendorId: a.vendorId, email: a.email, name: a.name, role: a.role, language: a.language },
      }),
    ),
  revokeMember: async (a: { memberId: string; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/wasel-members/{mid}/revoke', {
        params: { path: { mid: a.memberId } },
        body: { reason: a.reason },
      }),
    ),
  revokeAssignment: async (a: { assignmentId: string; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/wasel-assignments/{aid}/revoke', {
        params: { path: { aid: a.assignmentId } },
        body: { reason: a.reason },
      }),
    ),
  assignMilestone: async (a: { milestoneId: string; vendorId: string | null }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/milestones/{mid}/vendor', {
        params: { path: { mid: a.milestoneId } },
        body: { vendorId: a.vendorId },
      }),
    ),
  acceptProposal: async (a: {
    proposalId: string;
    items: ProposalItem[];
    comment: string | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/proposals/{pid}/accept', {
        params: { path: { pid: a.proposalId } },
        body: { items: a.items, comment: a.comment, version: a.version },
        headers: idem(),
      }),
    ),
  returnProposal: async (a: { proposalId: string; comment: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/proposals/{pid}/return', {
        params: { path: { pid: a.proposalId } },
        body: { items: null, comment: a.comment, version: a.version },
        headers: idem(),
      }),
    ),
  acceptItem: async (a: {
    submissionId: string;
    itemId: string;
    values: VendorProgressValues | null;
    version: number;
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/vendor-submissions/{sid}/items/{iid}/accept', {
        params: { path: { sid: a.submissionId, iid: a.itemId } },
        body: { values: a.values, comment: null, version: a.version },
        headers: idem(),
      }),
    ),
  returnItem: async (a: { submissionId: string; itemId: string; comment: string; version: number }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/vendor-submissions/{sid}/items/{iid}/return', {
        params: { path: { sid: a.submissionId, iid: a.itemId } },
        body: { values: null, comment: a.comment, version: a.version },
        headers: idem(),
      }),
    ),
  share: async (a: { submissionId: string; note: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/vendor-submissions/{sid}/share', {
        params: { path: { sid: a.submissionId } },
        body: { note: a.note },
      }),
    ),
  aiReview: async (submissionId: string) =>
    unwrap(
      await api.POST('/api/v1/portfolio/vendor-submissions/{sid}/ai-review', {
        params: { path: { sid: submissionId } },
      }),
    ),
  decideDeliverable: async (a: { id: string; accept: boolean; comment: string | null; version: number }) =>
    unwrap(
      a.accept
        ? await api.POST('/api/v1/portfolio/vendor-deliverables/{did}/accept', {
            params: { path: { did: a.id } },
            body: { comment: a.comment, version: a.version },
          })
        : await api.POST('/api/v1/portfolio/vendor-deliverables/{did}/return', {
            params: { path: { did: a.id } },
            body: { comment: a.comment, version: a.version },
          }),
    ),
  decideItem: async (a: { id: string; accept: boolean; comment: string | null; version: number }) =>
    unwrap(
      a.accept
        ? await api.POST('/api/v1/portfolio/vendor-items/{vid}/accept', {
            params: { path: { vid: a.id } },
            body: { comment: a.comment, version: a.version },
          })
        : await api.POST('/api/v1/portfolio/vendor-items/{vid}/reject', {
            params: { path: { vid: a.id } },
            body: { comment: a.comment, version: a.version },
          }),
    ),
  rate: async (a: {
    projectId: string;
    vendorId: string;
    milestoneId: string | null;
    values: S['VendorRatingValues'];
  }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/projects/{id}/wasel/ratings', {
        params: { path: { id: a.projectId } },
        body: { vendorId: a.vendorId, milestoneId: a.milestoneId, values: a.values },
      }),
    ),
  shareMeeting: async (a: { meetingId: string; vendorId: string | null }) =>
    unwrap(
      await api.PUT('/api/v1/portfolio/meetings/{mid}/vendor', {
        params: { path: { mid: a.meetingId } },
        body: { vendorId: a.vendorId },
      }),
    ),
};

export function useWaselComments(projectId: string, subjectType: string, subjectId: string) {
  return useQuery({
    queryKey: waselKeys.comments(projectId, subjectType, subjectId),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/projects/{id}/wasel/comments', {
          params: { path: { id: projectId }, query: { subjectType, subjectId } },
          signal,
        }),
      ),
  });
}

export function useAddWaselComment(projectId: string, subjectType: string, subjectId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (body: string) =>
      unwrap(
        await api.POST('/api/v1/portfolio/projects/{id}/wasel/comments', {
          params: { path: { id: projectId } },
          body: { subjectType, subjectId, body, vendorId: null },
        }),
      ),
    onSuccess: (list) =>
      queryClient.setQueryData(waselKeys.comments(projectId, subjectType, subjectId), list),
  });
}

// ---- vendor register ----

export function useVendors(search = '', status = '') {
  return useQuery({
    queryKey: waselKeys.vendors(search, status),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/portfolio/vendors', {
          params: { query: { search: search || undefined, status: status || undefined } },
          signal,
        }),
      ),
  });
}

export function useVendor(id: string) {
  return useQuery({
    queryKey: waselKeys.vendor(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/portfolio/vendors/{id}', { params: { path: { id } }, signal })),
  });
}

/** Register mutations answer the vendor's detail; every vendor list reloads. */
export function useVendorMutation<TArgs>(fn: (args: TArgs) => Promise<VendorDetail>) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: fn,
    onSuccess: async (vendor) => {
      queryClient.setQueryData(waselKeys.vendor(vendor.id), vendor);
      await queryClient.invalidateQueries({ queryKey: ['portfolio', 'vendors'] });
    },
  });
}

export const vendorApi = {
  save: async (a: { id?: string; values: VendorValues; version?: number }) =>
    unwrap(
      a.id
        ? await api.PUT('/api/v1/portfolio/vendors/{id}', {
            params: { path: { id: a.id } },
            body: { values: a.values, version: a.version ?? null },
          })
        : await api.POST('/api/v1/portfolio/vendors', { body: { values: a.values, version: null } }),
    ),
  status: async (a: { id: string; status: string; reason: string | null }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/vendors/{id}/status', {
        params: { path: { id: a.id } },
        body: { status: a.status, reason: a.reason },
      }),
    ),
  deactivateUser: async (a: { id: string; reason: string }) =>
    unwrap(
      await api.POST('/api/v1/portfolio/vendor-users/{id}/deactivate', {
        params: { path: { id: a.id } },
        body: { reason: a.reason },
      }),
    ),
  reactivateUser: async (id: string) =>
    unwrap(await api.POST('/api/v1/portfolio/vendor-users/{id}/reactivate', { params: { path: { id } } })),
};

// ---- pure helpers (unit-tested) ----

export const vendorRoles = ['vendor-lead', 'vendor-contributor'] as const;
export const updateModes = ['pm-only', 'vendor-then-pm'] as const;
export const expiryRules = ['contract-end', 'project-closure', 'date'] as const;
export const vendorStatuses = ['active', 'suspended', 'blacklisted'] as const;

/** One row of the side-by-side review: last approved, vendor and PM values of a field, and whether the PM changed it. */
export interface CompareRow {
  field: 'achieve' | 'actualStart' | 'actualEnd' | 'causality' | 'correctiveActions';
  previous: string | number | null;
  vendor: string | number | null;
  pm: string | number | null;
  changed: boolean;
}

export function compareRows(item: PmSubmissionItem, pm: VendorProgressValues): CompareRow[] {
  const v = item.vendor;
  const prev = item.previous;
  const rows: Omit<CompareRow, 'changed'>[] = [
    { field: 'achieve', previous: prev?.approved ?? null, vendor: v.achieve, pm: pm.achieve },
    { field: 'actualStart', previous: prev?.actualStart ?? null, vendor: v.actualStart, pm: pm.actualStart },
    { field: 'actualEnd', previous: prev?.actualEnd ?? null, vendor: v.actualEnd, pm: pm.actualEnd },
    { field: 'causality', previous: null, vendor: v.causality, pm: pm.causality },
    { field: 'correctiveActions', previous: null, vendor: v.correctiveActions, pm: pm.correctiveActions },
  ];
  return rows.map((r) => ({ ...r, changed: normalise(r.vendor) !== normalise(r.pm) }));
}

const normalise = (value: string | number | null | undefined) =>
  value === null || value === undefined ? '' : typeof value === 'number' ? String(value) : value.trim();

/** True when the PM's values differ from the vendor's: accepting then means "edit then accept" (the diff is logged). */
export function isEdited(item: PmSubmissionItem, pm: VendorProgressValues) {
  return compareRows(item, pm).some((r) => r.changed);
}
