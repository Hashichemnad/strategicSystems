import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import { KpiCardPicker } from './KpiCardPicker';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };
type Query = { search?: string; ids?: string[]; page?: number; pageSize?: number };

const card = (n: number) => ({
  id: `card-${n}`,
  code: `KPI-${String(n).padStart(4, '0')}`,
  nameAr: `مؤشر ${n}`,
  nameEn: `Indicator ${n}`,
  status: 'active',
});
const all = Array.from({ length: 250 }, (_, i) => card(i + 1));

function mockLookup() {
  return vi.spyOn(api as unknown as LooseGetApi, 'GET').mockImplementation((...args: unknown[]) => {
    const query = (args[1] as { params?: { query?: Query } } | undefined)?.params?.query ?? {};
    const page = query.page ?? 1;
    const size = query.pageSize ?? 20;
    const matches = query.ids
      ? all.filter((c) => query.ids?.includes(c.id))
      : all.filter(
          (c) =>
            !query.search ||
            `${c.code} ${c.nameAr} ${c.nameEn}`.toLowerCase().includes(query.search.toLowerCase()),
        );
    return Promise.resolve({
      data: {
        items: matches.slice((page - 1) * size, page * size),
        page,
        pageSize: size,
        totalCount: matches.length,
      },
      error: undefined,
      response: new Response(null, { status: 200 }),
    });
  });
}

function Harness({ initial, onChange }: { initial?: string; onChange: (id: string | null) => void }) {
  const [value, setValue] = useState<string | null>(initial ?? null);
  return (
    <KpiCardPicker
      value={value}
      onChange={(id) => {
        setValue(id);
        onChange(id);
      }}
      testId="picker"
    />
  );
}

async function renderPicker(language: 'ar' | 'en' = 'en', initial?: string) {
  const get = mockLookup();
  const onChange = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <Harness initial={initial} onChange={onChange} />
    </QueryClientProvider>,
    { language },
  );
  return { get, onChange };
}

describe('KpiCardPicker', () => {
  afterEach(() => vi.restoreAllMocks());

  it('searches the server as you type and picks with the keyboard', async () => {
    const { get, onChange } = await renderPicker();
    const input = screen.getByRole('combobox');
    await userEvent.type(input, 'Indicator 237');
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(1));
    expect(get).toHaveBeenCalledWith(
      '/api/v1/kpi/cards/lookup',
      expect.objectContaining({
        params: { query: expect.objectContaining({ search: 'Indicator 237' }) as unknown },
      }),
    );
    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect(onChange).toHaveBeenCalledWith('card-237');
    expect(await screen.findByTestId('picker-selected')).toHaveTextContent('KPI-0237 Indicator 237');
  });

  it('moves the active option with the arrow keys, Home and End, and closes with Escape', async () => {
    await renderPicker();
    const input = screen.getByRole('combobox');
    await userEvent.click(input);
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(20));
    await userEvent.keyboard('{ArrowDown}{ArrowDown}');
    expect(input).toHaveAttribute('aria-activedescendant', screen.getAllByRole('option')[2]?.id);
    await userEvent.keyboard('{End}');
    expect(screen.getAllByRole('option')[19]).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{Home}');
    expect(screen.getAllByRole('option')[0]).toHaveAttribute('aria-selected', 'true');
    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('pages beyond the first results with Show more', async () => {
    await renderPicker();
    await userEvent.click(screen.getByRole('combobox'));
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(20));
    await userEvent.click(screen.getByRole('button', { name: 'Show more' }));
    await waitFor(() => expect(screen.getAllByRole('option')).toHaveLength(40));
  });

  it('shows the selected card even when it is not on the current page, in Arabic', async () => {
    const { get } = await renderPicker('ar', 'card-249');
    expect(await screen.findByTestId('picker-selected')).toHaveTextContent('KPI-0249 مؤشر 249');
    expect(get).toHaveBeenCalledWith(
      '/api/v1/kpi/cards/lookup',
      expect.objectContaining({ params: { query: { ids: ['card-249'] } } }),
    );
  });
});
