import { keepPreviousData, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/api/client';
import type { components, operations } from '@/api/schema';

export type OrgTreeNode = components['schemas']['OrgTreeNodeDto'];
export type OrgUnit = components['schemas']['OrgUnitDto'];
export type OrgUnitVersion = components['schemas']['OrgUnitVersionDto'];
export type OrgLevel = components['schemas']['OrgLevelDto'];
export type Employee = components['schemas']['EmployeeDto'];
export type EmployeeListItem = components['schemas']['EmployeeListItemDto'];
export type EmployeeAssignment = components['schemas']['EmployeeAssignmentDto'];
export type EmployeeListQuery = NonNullable<operations['ListEmployees']['parameters']['query']>;

export const orgKeys = {
  all: ['org'] as const,
  levels: ['org', 'levels'] as const,
  tree: (asOf?: string, includeInactive?: boolean) =>
    ['org', 'units', 'tree', asOf ?? 'now', includeInactive ?? false] as const,
  unit: (id: string) => ['org', 'units', id] as const,
  unitHistory: (id: string) => ['org', 'units', id, 'history'] as const,
  employees: (query: EmployeeListQuery) => ['org', 'employees', 'list', query] as const,
  employee: (id: string) => ['org', 'employees', id] as const,
  employeeHistory: (id: string) => ['org', 'employees', id, 'history'] as const,
};

export function useLevels() {
  return useQuery({
    queryKey: orgKeys.levels,
    queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/org/levels', { signal })),
    staleTime: 5 * 60_000,
  });
}

export function useUnitTree(asOf?: string, includeInactive?: boolean) {
  return useQuery({
    queryKey: orgKeys.tree(asOf, includeInactive),
    queryFn: async ({ signal }) =>
      unwrap(
        await api.GET('/api/v1/org/units/tree', { params: { query: { asOf, includeInactive } }, signal }),
      ),
    placeholderData: keepPreviousData,
  });
}

export function useUnit(id: string | undefined) {
  return useQuery({
    queryKey: orgKeys.unit(id ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/org/units/{id}', { params: { path: { id: id ?? '' } }, signal })),
    enabled: !!id,
  });
}

export function useUnitHistory(id: string | undefined) {
  return useQuery({
    queryKey: orgKeys.unitHistory(id ?? ''),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/org/units/{id}/history', { params: { path: { id: id ?? '' } }, signal })),
    enabled: !!id,
  });
}

export function useEmployees(query: EmployeeListQuery, enabled = true) {
  return useQuery({
    queryKey: orgKeys.employees(query),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/org/employees', { params: { query }, signal })),
    placeholderData: keepPreviousData,
    enabled,
  });
}

export function useEmployee(id: string) {
  return useQuery({
    queryKey: orgKeys.employee(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/org/employees/{id}', { params: { path: { id } }, signal })),
  });
}

export function useEmployeeHistory(id: string) {
  return useQuery({
    queryKey: orgKeys.employeeHistory(id),
    queryFn: async ({ signal }) =>
      unwrap(await api.GET('/api/v1/org/employees/{id}/history', { params: { path: { id } }, signal })),
  });
}

export function useInvalidateOrg() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: orgKeys.all });
}

/** Today in ISO form (calendar date in the user's time zone). */
export function todayIso(): string {
  const now = new Date();
  return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
}

/** Empty strings become null before they are sent (the API distinguishes "not given" from text). */
export function nullIfEmpty(value: string | undefined | null): string | null {
  return value === undefined || value === null || value.trim() === '' ? null : value.trim();
}
