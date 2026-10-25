import { queryOptions, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, resetAntiforgeryToken, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type Me = components['schemas']['MeDto'];
export type SignInOutcome = components['schemas']['SignInOutcome'];

export const meKey = ['identity', 'me'] as const;

/** The signed-in user. A 401 means "not signed in" and is handled by the router (redirect to /login). */
export const meQueryOptions = queryOptions({
  queryKey: meKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/identity/me', { signal })),
  staleTime: 5 * 60_000,
  retry: false,
});

export function useMe(): Me | undefined {
  return useQuery(meQueryOptions).data;
}

/** Only allows same-site paths ("/x"), never "//host" or absolute URLs: no open redirects after sign-in. */
export function safeRedirect(target: string | undefined): string {
  return target && target.startsWith('/') && !target.startsWith('//') && !target.startsWith('/\\')
    ? target
    : '/';
}

/** After any sign-in step completes: new antiforgery token, fresh user data. */
export function useCompleteSignIn() {
  const queryClient = useQueryClient();
  return async () => {
    resetAntiforgeryToken();
    queryClient.removeQueries();
    await queryClient.query(meQueryOptions);
  };
}

export function useSignOut() {
  const queryClient = useQueryClient();
  return async () => {
    await api.POST('/api/v1/identity/auth/logout').catch(() => undefined);
    resetAntiforgeryToken();
    queryClient.clear();
  };
}
