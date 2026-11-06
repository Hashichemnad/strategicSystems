import { queryOptions } from '@tanstack/react-query';
import { api, ApiError, unwrap } from '@/api/client';
import type { components } from '@/api/schema';

export type SetupStatus = components['schemas']['SetupStatusDto'];
export type SetupBootstrapInput = components['schemas']['SetupBootstrapInput'];

/** Header carrying the one-time setup token (ADR 0150). */
export const SETUP_TOKEN_HEADER = 'X-Setup-Token';

export const setupStatusKey = ['setup', 'status'] as const;

/** Public: whether the first-run wizard is open (no administrator yet) and whether the guided configuration was finished. */
export const setupStatusQuery = queryOptions({
  queryKey: setupStatusKey,
  queryFn: async ({ signal }) => unwrap(await api.GET('/api/v1/setup/status', { signal })),
  staleTime: 60_000,
});

/** True when the token is the current one; false when the server refuses it (403). */
export async function verifySetupToken(token: string): Promise<boolean> {
  const result = await api.POST('/api/v1/setup/verify-token', {
    headers: { [SETUP_TOKEN_HEADER]: token.trim() },
  });
  if (result.response.status === 204) return true;
  if (result.response.status === 403) return false;
  unwrap(result);
  return false;
}

export async function bootstrapSetup(token: string, body: SetupBootstrapInput) {
  return unwrap(
    await api.POST('/api/v1/setup/bootstrap', { body, headers: { [SETUP_TOKEN_HEADER]: token.trim() } }),
  );
}

export async function completeSetup() {
  return unwrap(await api.POST('/api/v1/setup/complete'));
}

/** The optional demo pack (ADR 0153): available when its status endpoint answers (a build without it answers 404). */
export async function demoPackAvailable(signal?: AbortSignal): Promise<boolean> {
  const result = await api.GET('/api/v1/setup/demo-pack/status', { signal });
  return result.response.status !== 404;
}

/** Loads the demo pack with the password the demo users get. Throws {@link ApiError} with the server's problem on failure. */
export async function loadDemoPack(password: string): Promise<void> {
  unwrap(await api.POST('/api/v1/setup/demo-pack', { body: { password } }));
}

export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403;
}
