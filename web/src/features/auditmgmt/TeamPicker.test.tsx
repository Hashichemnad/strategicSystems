import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { AuditTeamOption } from './api';
import { AuditTeamPicker } from './TeamPicker';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };

const team: AuditTeamOption = {
  id: 'team-1',
  number: 'TEAM-0007',
  nameAr: 'فريق الإيرادات',
  nameEn: 'Revenue squad',
  status: 'active',
};

function Harness({ onPick }: { onPick: (team: AuditTeamOption | null) => void }) {
  const [value, setValue] = useState<AuditTeamOption | null>(null);
  return (
    <AuditTeamPicker
      value={value}
      onChange={(picked) => {
        setValue(picked);
        onPick(picked);
      }}
    />
  );
}

describe('AuditTeamPicker', () => {
  afterEach(() => vi.restoreAllMocks());

  it.each(['en', 'ar'] as const)(
    'searches the server as the user types and picks a team (%s)',
    async (language) => {
      const get = vi.spyOn(api as unknown as LooseGetApi, 'GET').mockResolvedValue({
        data: [team],
        error: undefined,
        response: new Response(null, { status: 200 }),
      });
      const onPick = vi.fn();
      await renderWithProviders(
        <QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
          <Harness onPick={onPick} />
        </QueryClientProvider>,
        { language },
      );

      await userEvent.type(screen.getByRole('combobox'), 'TEAM-0007');
      await waitFor(() =>
        expect(get).toHaveBeenCalledWith(
          '/api/v1/auditmgmt/lifecycle/teams',
          expect.objectContaining({ params: { query: { search: 'TEAM-0007' } } }),
        ),
      );
      const name = language === 'ar' ? 'فريق الإيرادات' : 'Revenue squad';
      await userEvent.click(await screen.findByRole('button', { name: new RegExp(name) }));

      expect(onPick).toHaveBeenCalledWith(team);
      expect(screen.queryByRole('combobox')).toBeNull();
      expect(screen.getByText(`TEAM-0007 ${name}`)).toBeInTheDocument();
    },
  );
});
