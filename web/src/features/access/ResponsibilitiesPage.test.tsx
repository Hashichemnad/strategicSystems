import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { translateMessageKey } from '@/api/problem';
import i18n from '@/i18n';
import ar from '@/i18n/ar/responsibilities.json';
import en from '@/i18n/en/responsibilities.json';
import { renderWithProviders } from '@/test/render';
import { ResponsibilitiesView } from './ResponsibilitiesPage';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseApi = {
  GET: (...args: unknown[]) => Promise<unknown>;
  POST: (...args: unknown[]) => Promise<unknown>;
};

const ok = (data: unknown) => ({ data, error: undefined, response: new Response(null, { status: 200 }) });

const person = (id: string, nameEn: string, nameAr: string) => ({ id, nameEn, nameAr });
const ownerA = person('00000000-0000-7000-8000-00000000000a', 'Owner A', 'المسؤول أ');
const ownerB = person('00000000-0000-7000-8000-00000000000b', 'Owner B', 'المسؤول ب');
const row = (id: string, number: string, titleEn: string, version = 7) => ({
  id,
  version,
  number,
  titleAr: 'مؤشر ' + titleEn,
  titleEn,
  status: 'active',
  responsible: ownerA,
  unit: { id: 'u1', nameAr: 'إدارة أ', nameEn: 'Department A' },
  link: `/kpi/cards/${id}`,
  contextAr: null,
  contextEn: null,
});
const rows = [
  row('c1', 'KPI-001', 'Permits'),
  row('c2', 'KPI-002', 'Licences'),
  row('c3', 'KPI-003', 'Inspections'),
];

function mockApi() {
  vi.spyOn(api as unknown as LooseApi, 'GET').mockImplementation((path: unknown, options: unknown) => {
    const query =
      (options as { params?: { query?: Record<string, unknown> } } | undefined)?.params?.query ?? {};
    switch (path) {
      case '/api/v1/access/me':
        return Promise.resolve(ok({ systems: ['kpi'], permissions: ['kpi.responsibilities.manage'] }));
      case '/api/v1/access/systems/{code}/responsibilities':
        return Promise.resolve(
          ok({
            systemCode: 'kpi',
            nameAr: 'إدارة الأداء',
            nameEn: 'Performance',
            kinds: [
              {
                code: 'card-person',
                labelAr: 'الموظف المسؤول عن بطاقة المؤشر',
                labelEn: 'KPI card responsible person',
                target: 'employee',
                restrictedCandidates: false,
                noteKey: null,
                statuses: [{ code: 'active', labelAr: 'نشطة', labelEn: 'Active' }],
              },
            ],
          }),
        );
      case '/api/v1/access/systems/{code}/responsibilities/{kind}':
        return Promise.resolve(ok({ items: rows, total: rows.length, page: query.page ?? 1, pageSize: 25 }));
      case '/api/v1/org/units/tree':
        return Promise.resolve(ok([]));
      case '/api/v1/org/employees':
        return Promise.resolve(
          ok({
            items: [{ ...ownerB, employeeNo: 'E2', unitNameAr: 'إدارة ب', unitNameEn: 'Department B' }],
            totalCount: 1,
            page: 1,
            pageSize: 8,
          }),
        );
      case '/api/v1/org/employees/{id}':
        return Promise.resolve(ok({ ...ownerB, employeeNo: 'E2' }));
      default:
        return Promise.resolve(ok(null));
    }
  });
  return vi.spyOn(api as unknown as LooseApi, 'POST').mockImplementation((path: unknown) =>
    Promise.resolve(
      path === '/api/v1/access/systems/{code}/responsibilities/{kind}/preview'
        ? ok([
            {
              id: 'c1',
              outcome: 'allowed',
              messageKey: null,
              number: 'KPI-001',
              titleAr: null,
              titleEn: 'Permits',
              previous: ownerA,
              reference: null,
            },
            {
              id: 'c2',
              outcome: 'blocked',
              messageKey: 'responsibilities.outcome.changedMeanwhile',
              number: 'KPI-002',
              titleAr: null,
              titleEn: 'Licences',
              previous: ownerA,
              reference: null,
            },
          ])
        : ok({
            moved: 1,
            changeRequests: 0,
            skipped: 1,
            new: ownerB,
            items: [
              {
                id: 'c1',
                outcome: 'moved',
                messageKey: null,
                number: 'KPI-001',
                titleAr: null,
                titleEn: 'Permits',
                previous: ownerA,
                reference: null,
              },
              {
                id: 'c2',
                outcome: 'skipped',
                messageKey: 'responsibilities.outcome.changedMeanwhile',
                number: 'KPI-002',
                titleAr: null,
                titleEn: 'Licences',
                previous: ownerA,
                reference: null,
              },
            ],
          }),
    ),
  );
}

async function renderView(language: 'ar' | 'en' = 'en') {
  const post = mockApi();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <ResponsibilitiesView system="kpi" kind={undefined} onChange={vi.fn()} />
    </QueryClientProvider>,
    { language },
  );
  return post;
}

describe('ResponsibilitiesView', () => {
  afterEach(() => vi.restoreAllMocks());

  it('selects records, previews with a reason and shows what moved and what was skipped', async () => {
    const post = await renderView();
    const table = await screen.findByTestId('responsibilities-table');
    await userEvent.click(within(table).getByRole('checkbox', { name: 'Select KPI-001' }));
    await userEvent.click(within(table).getByRole('checkbox', { name: 'Select KPI-002' }));
    expect(screen.getByText('2 records selected')).toBeInTheDocument();

    const preview = screen.getByTestId('responsibilities-preview');
    expect(preview).toBeDisabled();
    await userEvent.type(screen.getByRole('combobox', { name: 'New person' }), 'Owner');
    await userEvent.click(await screen.findByRole('option', { name: /Owner B/ }));
    await userEvent.type(screen.getByLabelText(/Reason/), 'Reorganisation');
    await userEvent.click(preview);

    expect(post).toHaveBeenCalledWith('/api/v1/access/systems/{code}/responsibilities/{kind}/preview', {
      params: { path: { code: 'kpi', kind: 'card-person' } },
      body: {
        items: [
          { id: 'c1', version: 7 },
          { id: 'c2', version: 7 },
        ],
        newId: ownerB.id,
        reason: 'Reorganisation',
      },
    });
    const outcomes = await screen.findByTestId('responsibilities-outcomes');
    expect(within(outcomes).getByText('Will move')).toBeInTheDocument();
    expect(
      within(outcomes).getByText('Changed by someone else meanwhile — reload and try again'),
    ).toBeInTheDocument();

    await userEvent.click(screen.getByTestId('responsibilities-confirm'));
    await userEvent.click(await screen.findByRole('button', { name: 'Reassign 1 record' }));
    expect(await screen.findByText('Moved: 1 · change requests: 0 · skipped: 1')).toBeInTheDocument();
    expect(post.mock.calls.at(-1)?.[0]).toBe(
      '/api/v1/access/systems/{code}/responsibilities/{kind}/reassign',
    );
  });

  it('works in Arabic', async () => {
    await renderView('ar');
    const table = await screen.findByTestId('responsibilities-table');
    expect(within(table).getByRole('checkbox', { name: 'تحديد كل سجلات هذه الصفحة' })).toBeInTheDocument();
    expect(within(table).getByText('مؤشر Permits')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'المسؤوليات' })).toBeInTheDocument();
  });
});

/** Every responsibilities message key the server returns (C# sources) has an AR and an EN text. */
describe('responsibilities message keys', () => {
  const keys = new Set<string>();
  const walk = (dir: string) => {
    for (const name of readdirSync(dir)) {
      const path = join(dir, name);
      if (name === 'bin' || name === 'obj' || name === 'Migrations') continue;
      if (statSync(path).isDirectory()) walk(path);
      else if (name.endsWith('.cs'))
        for (const match of readFileSync(path, 'utf8').matchAll(
          /"(responsibilities\.(?:outcome|notes)\.[A-Za-z]+)"/g,
        ))
          if (match[1]) keys.add(match[1]);
    }
  };
  walk('../src');
  const dynamicT = i18n.t.bind(i18n) as unknown as Parameters<typeof translateMessageKey>[0];

  it('finds the keys', () => {
    expect([...keys]).toContain('responsibilities.outcome.notEligible');
    expect([...keys]).toContain('responsibilities.notes.changeRequest');
  });

  it.each([...keys])('%s is translated in both languages', async (key) => {
    const path = key.split('.').slice(1);
    const lookup = (source: unknown) =>
      path.reduce<unknown>((value, part) => (value as Record<string, unknown>)[part], source);
    expect(typeof lookup(en)).toBe('string');
    expect(typeof lookup(ar)).toBe('string');
    await i18n.changeLanguage('en');
    expect(translateMessageKey(dynamicT, key)).toBe(lookup(en));
  });
});
