import { queryOptions } from '@tanstack/react-query';
import { api, ApiError, platformRequestHeaders, type Problem, unwrap } from '@/api/client';
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

/**
 * The optional demo pack (another part of Phase 18). The endpoint is not part of every build: a 404 means "not available" and the
 * wizard hides the step. Called without the typed client on purpose, as the path may be absent from the OpenAPI document.
 */
export const DEMO_PACK_PATH = '/api/v1/setup/demo-pack';

/** A GET on the POST-only route answers 405 when the demo pack exists and 404 when it does not. */
export async function demoPackAvailable(signal?: AbortSignal): Promise<boolean> {
  // eslint-disable-next-line no-restricted-syntax -- optional endpoint that may be absent from this build's OpenAPI document
  const response = await fetch(DEMO_PACK_PATH, { method: 'GET', credentials: 'same-origin', signal });
  return response.status !== 404;
}

/** Loads the demo pack with the password the demo users get. Throws {@link ApiError} with the server's problem on failure. */
export async function loadDemoPack(password: string): Promise<void> {
  // eslint-disable-next-line no-restricted-syntax -- optional endpoint that may be absent from this build's OpenAPI document
  const response = await fetch(DEMO_PACK_PATH, {
    method: 'POST',
    credentials: 'same-origin',
    headers: await platformRequestHeaders(),
    body: JSON.stringify({ password }),
  });
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as Problem | null;
    throw new ApiError(
      response.status,
      problem ?? { status: response.status, code: 'unexpected', messageKey: 'errors.unexpected' },
    );
  }
}

export function isForbidden(error: unknown): boolean {
  return error instanceof ApiError && error.status === 403;
}
