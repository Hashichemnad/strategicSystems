import { afterEach, describe, expect, it, vi } from 'vitest';
import { api, UNAUTHORIZED_EVENT } from './client';

// Node's Request needs an absolute URL; the SPA itself uses relative ones.
const baseUrl = 'http://localhost';

const unauthorized = () =>
  Promise.resolve(
    new Response(JSON.stringify({ code: 'auth.required', messageKey: 'errors.unauthorized' }), {
      status: 401,
      headers: { 'Content-Type': 'application/problem+json' },
    }),
  );

describe('api client', () => {
  const listener = vi.fn();
  window.addEventListener(UNAUTHORIZED_EVENT, listener);
  afterEach(() => listener.mockClear());

  it('announces an ended session when a signed-in call gets 401', async () => {
    const { response } = await api.GET('/api/v1/org/levels', { baseUrl, fetch: unauthorized });
    expect(response.status).toBe(401);
    expect(listener).toHaveBeenCalledOnce();
  });

  it('does not treat the session probe or sign-in answers as an ended session', async () => {
    await api.GET('/api/v1/identity/me', { baseUrl, fetch: unauthorized });
    await api.GET('/api/v1/identity/auth/providers', { baseUrl, fetch: unauthorized });
    expect(listener).not.toHaveBeenCalled();
  });

  it('sends the platform headers', async () => {
    const fetch = vi.fn((request: Request) => {
      expect(request.headers.get('X-Client')).toBe('spa');
      expect(request.headers.get('X-Correlation-Id')).toMatch(/^[0-9a-f]{32}$/u);
      return Promise.resolve(Response.json([]));
    });
    await api.GET('/api/v1/org/levels', { baseUrl, fetch });
    expect(fetch).toHaveBeenCalledOnce();
  });
});
