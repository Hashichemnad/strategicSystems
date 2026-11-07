import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';
import { accessKey } from './session';

export type SystemMembers = components['schemas']['SystemMembersDto'];
export type MemberList = components['schemas']['SystemMemberListDto'];
export type Member = components['schemas']['SystemMemberDto'];
export type MemberPrincipalKind = 'user' | 'unit' | 'everyone';

/**
 * Systems with a Members page (codes of the access catalogue, ADR 0166). Only used to decide whether the Settings entry shows;
 * the list of systems the user may manage comes from the server.
 */
export const memberSystems = [
  'portfolio',
  'kpi',
  'risk',
  'teams',
  'tasks',
  'requests',
  'docreg',
  'ip',
  'bench',
  'cap',
  'auditmgmt',
  'sample',
] as const;

/** The delegated-administration permission of a system (ADR 0167). */
export const memberManagePermission = (system: string) => `${system}.members.manage`;

/** Every permission that opens Settings → Members. */
export const membersPermissions = [
  'access.assignment.manage',
  ...memberSystems.map(memberManagePermission),
] as const;

export const membersKey = (system: string) => ['access', 'members', system] as const;

export function useMemberSystems() {
  return useQuery({
    queryKey: ['access', 'members', 'systems'],
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/members/systems', { signal })),
  });
}

export function useSystemMembers(system: string) {
  return useQuery({
    queryKey: membersKey(system),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/systems/{code}/members', {
          params: { path: { code: system } },
          signal,
        }),
      ),
  });
}

export interface AddMemberValues {
  list: string;
  principalKind: MemberPrincipalKind;
  principalId: string | null;
  validFrom: string | null;
  validTo: string | null;
  note: string | null;
}

/** Add / remove: the response is the refreshed page, so it replaces the cache; the user's own access may have changed too. */
export function useMemberMutations(system: string) {
  const queryClient = useQueryClient();
  const settle = (data: SystemMembers) => {
    queryClient.setQueryData(membersKey(system), data);
    void queryClient.invalidateQueries({ queryKey: accessKey });
  };
  const add = useMutation({
    mutationFn: async (body: AddMemberValues) =>
      unwrap(
        await api.POST('/api/v1/access/systems/{code}/members', { params: { path: { code: system } }, body }),
      ),
    onSuccess: settle,
  });
  const remove = useMutation({
    mutationFn: async ({ id, reason }: { id: string; reason: string | null }) =>
      unwrap(
        await api.POST('/api/v1/access/systems/{code}/members/{id}/remove', {
          params: { path: { code: system, id } },
          body: { reason },
        }),
      ),
    onSuccess: settle,
  });
  return { add, remove };
}

export function useMemberRoles(system: string, enabled: boolean) {
  return useQuery({
    queryKey: ['access', 'member-roles', system],
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/access/systems/{code}/member-roles', {
          params: { path: { code: system } },
          signal,
        }),
      ),
    enabled,
  });
}
