import { queryOptions, useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type MyAccess = components['schemas']['AccessMeDto'];

export const accessKey = ['access', 'me'] as const;

/**
 * Systems the user may open and permission codes they hold (any scope). Only for showing menus and actions: the server
 * checks every call again (CLAUDE.md §7).
 */
export const accessQueryOptions = queryOptions({
  queryKey: accessKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/access/me', { signal })),
  staleTime: 60_000,
});

export function useAccess(): MyAccess | undefined {
  return useQuery(accessQueryOptions).data;
}

export function canIn(access: MyAccess | undefined, ...permissions: string[]): boolean {
  return !!access && permissions.some((permission) => access.permissions.includes(permission));
}

/** True when the user holds at least one of the permissions. */
export function useCan(...permissions: string[]): boolean {
  return canIn(useAccess(), ...permissions);
}

export function useHasSystem(system: string): boolean {
  return useAccess()?.systems.includes(system) ?? false;
}
