import createClient, { type Middleware } from 'openapi-fetch';
import i18n from '@/i18n';
import type { components, paths } from './schema';

export type Problem = components['schemas']['Problem'];

const UNSAFE_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);
const XSRF_HEADER = 'X-XSRF-TOKEN';

/** The only HTTP client in the SPA. Types come from the generated OpenAPI schema (never hand-written). */
export const api = createClient<paths>({ baseUrl: '' });

let antiforgeryToken: Promise<string> | null = null;

async function fetchAntiforgeryToken(): Promise<string> {
  const { data } = await api.GET('/api/v1/antiforgery/token');
  if (!data) throw new Error('Antiforgery token unavailable');
  return data.token;
}

function getAntiforgeryToken(): Promise<string> {
  antiforgeryToken ??= fetchAntiforgeryToken().catch((error: unknown) => {
    antiforgeryToken = null;
    throw error;
  });
  return antiforgeryToken;
}

function newCorrelationId(): string {
  return crypto.randomUUID().replaceAll('-', '');
}

const platformHeaders: Middleware = {
  async onRequest({ request, schemaPath }) {
    request.headers.set('X-Client', 'spa');
    request.headers.set('X-Correlation-Id', newCorrelationId());
    request.headers.set('Accept-Language', i18n.language);
    if (UNSAFE_METHODS.has(request.method) && schemaPath !== '/api/v1/antiforgery/token') {
      request.headers.set(XSRF_HEADER, await getAntiforgeryToken());
    }
    return request;
  },
  async onResponse({ request, response }) {
    // A stale antiforgery token (e.g. server keys rotated) is dropped so the user's next attempt fetches a new one.
    if (response.status === 400 && UNSAFE_METHODS.has(request.method)) {
      const problem = (await response
        .clone()
        .json()
        .catch(() => null)) as Problem | null;
      if (problem?.code === 'antiforgery.invalid') antiforgeryToken = null;
    }
    return response;
  },
};

api.use(platformHeaders);

/** An API failure carrying the server's Problem Details (code, messageKey, field errors). */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: Problem,
  ) {
    super(problem.code);
    this.name = 'ApiError';
  }
}

/**
 * Unwraps an openapi-fetch result: returns the data, or throws {@link ApiError}.
 * Used by every TanStack Query function so errors flow into one place.
 */
export function unwrap<T>(result: { data?: T; error?: unknown; response: Response }): T {
  if (result.error !== undefined || !result.response.ok) {
    throw new ApiError(result.response.status, toProblem(result.error, result.response.status));
  }
  return result.data as T;
}

function toProblem(error: unknown, status: number): Problem {
  if (error && typeof error === 'object' && 'code' in error && 'messageKey' in error) {
    return error as Problem;
  }
  return { status, code: 'unexpected', messageKey: 'errors.unexpected' };
}
