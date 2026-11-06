import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import { DemoPackPage } from './DemoPackPage';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseApi = {
  GET: (...args: unknown[]) => Promise<unknown>;
  POST: (...args: unknown[]) => Promise<unknown>;
};

const ok = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) });

async function renderPage(status: unknown, language: 'ar' | 'en' = 'en') {
  vi.spyOn(api as unknown as LooseApi, 'GET').mockResolvedValue(ok(status));
  const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue(
    ok({
      loaded: true,
      version: '1.0',
      contributors: [
        { code: 'platform', status: 'loaded', records: 22, errorCode: null },
        { code: 'kpi', status: 'loaded', records: 22, errorCode: null },
      ],
    }),
  );
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <DemoPackPage />
    </QueryClientProvider>,
    { language },
  );
  return post;
}

describe('DemoPackPage', () => {
  afterEach(() => vi.restoreAllMocks());

  it('loads the demo pack with the chosen password and shows what each system received', async () => {
    const post = await renderPage({ loaded: false, version: '1.0', contributors: [] });
    const load = await screen.findByRole('button', { name: 'Load demo data' });
    expect(load).toBeDisabled();
    await userEvent.type(screen.getByLabelText(/Password for the demo users/), 'Demo#Pack2026!');
    await userEvent.click(load);
    expect(post).toHaveBeenCalledWith('/api/v1/setup/demo-pack', { body: { password: 'Demo#Pack2026!' } });
    expect(await screen.findByRole('cell', { name: 'Performance Management' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Remove demo data' })).toBeInTheDocument();
  });

  it('offers removal in Arabic when the pack is loaded', async () => {
    await renderPage(
      {
        loaded: true,
        version: '1.0',
        contributors: [{ code: 'risk', status: 'loaded', records: 6, errorCode: null }],
      },
      'ar',
    );
    expect(await screen.findByRole('button', { name: 'إزالة بيانات العرض' })).toBeInTheDocument();
    expect(screen.getByRole('cell', { name: 'إدارة المخاطر' })).toBeInTheDocument();
    expect(screen.getByText('للعروض التقديمية والتدريب فقط')).toBeInTheDocument();
  });
});
