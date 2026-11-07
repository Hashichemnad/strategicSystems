import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import { SystemMembersView } from './MembersPage';
import type { SystemMembers } from './members';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseApi = {
  GET: (...args: unknown[]) => Promise<unknown>;
  POST: (...args: unknown[]) => Promise<unknown>;
};

const ok = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) });

const person = {
  kind: 'user',
  id: '0192f000-0000-7000-8000-000000000001',
  nameAr: 'مريم',
  nameEn: 'Mariam',
  detail: 'mariam@x.test',
};

const page: SystemMembers = {
  system: {
    code: 'portfolio',
    kind: 'module',
    nameAr: 'إدارة المحافظ والمشاريع',
    nameEn: 'Portfolio management',
    enabled: true,
    owningUnitId: null,
    owningUnitNameAr: null,
    owningUnitNameEn: null,
    sort: 100,
    version: 1,
  },
  platformAdministrator: false,
  lists: [
    {
      key: 'administrators',
      kind: 'administrators',
      roles: [{ code: 'administrator', nameAr: 'مسؤول نظام المحافظ', nameEn: 'Portfolio administrator' }],
      canManage: true,
      members: [
        {
          id: '0192f000-0000-7000-8000-0000000000a1',
          principal: person,
          validFrom: '2026-11-01',
          validTo: null,
          active: true,
          systemAccess: true,
          roleCodes: ['administrator'],
        },
      ],
    },
    {
      key: 'users',
      kind: 'users',
      roles: [{ code: 'requester', nameAr: 'مقدم طلب', nameEn: 'Requester' }],
      canManage: true,
      members: [],
    },
    {
      key: 'extra:viewer',
      kind: 'extra',
      roles: [{ code: 'viewer', nameAr: 'مستعرض المحفظة', nameEn: 'Portfolio viewer' }],
      canManage: false,
      members: [],
    },
  ],
};

async function renderView(language: 'ar' | 'en' = 'en') {
  vi.spyOn(api as unknown as LooseApi, 'GET').mockImplementation((path: unknown) =>
    Promise.resolve(ok(path === '/api/v1/access/systems/{code}/members/user-search' ? [person] : page)),
  );
  const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue(ok(page));
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <SystemMembersView system="portfolio" />
    </QueryClientProvider>,
    { language },
  );
  return post;
}

describe('SystemMembersView', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the lists with members, empty states and lists only the platform administrator manages', async () => {
    await renderView();
    const administrators = await screen.findByRole('region', { name: 'System administrators' });
    expect(within(administrators).getByText(/Mariam/)).toBeInTheDocument();
    expect(within(administrators).getByText('In force')).toBeInTheDocument();
    const users = screen.getByRole('region', { name: 'Users' });
    expect(within(users).getByText('No one is on this list yet.')).toBeInTheDocument();
    const viewer = screen.getByRole('region', { name: 'Portfolio viewer' });
    expect(within(viewer).getByText('Managed by the platform administrator')).toBeInTheDocument();
    expect(within(viewer).queryByRole('button', { name: /Add/ })).not.toBeInTheDocument();
  });

  it('adds a person found through the system members search', async () => {
    const post = await renderView();
    await userEvent.click(await screen.findByRole('button', { name: 'Add to Users' }));
    const dialog = await screen.findByRole('dialog');
    await userEvent.type(within(dialog).getByRole('combobox', { name: /Person/ }), 'mar');
    await userEvent.click(await within(dialog).findByRole('option', { name: /Mariam/ }));
    await userEvent.type(within(dialog).getByLabelText(/Note/), 'New analyst');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Add' }));
    expect(post).toHaveBeenCalledWith('/api/v1/access/systems/{code}/members', {
      params: { path: { code: 'portfolio' } },
      body: {
        list: 'users',
        principalKind: 'user',
        principalId: person.id,
        validFrom: null,
        validTo: null,
        note: 'New analyst',
      },
    });
  });

  it('removes a member after confirmation with a reason, in Arabic', async () => {
    const post = await renderView('ar');
    const administrators = await screen.findByRole('region', { name: 'مسؤولو النظام' });
    await userEvent.click(within(administrators).getByRole('button', { name: /إزالة/ }));
    const dialog = await screen.findByRole('dialog');
    expect(within(dialog).getByText(/ينتهي وصوله عبر هذه القائمة الآن/)).toBeInTheDocument();
    await userEvent.type(within(dialog).getByLabelText(/السبب/), 'انتقل');
    await userEvent.click(within(dialog).getByRole('button', { name: 'إزالة' }));
    expect(post).toHaveBeenCalledWith('/api/v1/access/systems/{code}/members/{id}/remove', {
      params: { path: { code: 'portfolio', id: '0192f000-0000-7000-8000-0000000000a1' } },
      body: { reason: 'انتقل' },
    });
  });
});
