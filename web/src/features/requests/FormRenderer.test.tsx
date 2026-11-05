import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import type { FormSchema, FormValues } from './api';
import { FormRenderer, FormValuesView, plainText } from './FormRenderer';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };

const schema: FormSchema = {
  sections: [
    {
      code: 'main',
      fields: [
        { code: 'title', type: 'text', labelAr: 'العنوان', labelEn: 'Title', required: true },
        { code: 'urgent', type: 'checkbox', labelAr: 'عاجل', labelEn: 'Urgent' },
        {
          code: 'reason',
          type: 'textarea',
          labelAr: 'سبب الاستعجال',
          labelEn: 'Why urgent',
          visibleWhen: { field: 'urgent', op: 'eq', value: true },
        },
        {
          code: 'items',
          type: 'table',
          labelAr: 'البنود',
          labelEn: 'Items',
          columns: [{ code: 'name', type: 'text', labelAr: 'الاسم', labelEn: 'Name' }],
        },
      ],
    },
  ],
};

function Harness({ onValues }: { onValues: (values: FormValues) => void }) {
  const [values, setValues] = useState<FormValues>({});
  return (
    <FormRenderer
      schema={schema}
      values={values}
      onChange={(next) => {
        setValues(next);
        onValues(next);
      }}
      errors={{ title: { messageKey: 'validation.notEmpty' } }}
    />
  );
}

async function render(node: React.ReactNode, language: 'ar' | 'en' = 'en') {
  vi.spyOn(api as unknown as LooseGetApi, 'GET').mockResolvedValue({
    data: [],
    error: undefined,
    response: new Response(null, { status: 200 }),
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(<QueryClientProvider client={client}>{node}</QueryClientProvider>, { language });
}

describe('FormRenderer', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows conditional fields live, adds table rows and puts server errors on their input', async () => {
    const onValues = vi.fn();
    await render(<Harness onValues={onValues} />);
    expect(screen.getByRole('textbox', { name: /title/i })).toHaveAttribute('aria-invalid', 'true');
    expect(screen.queryByRole('textbox', { name: /why urgent/i })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('checkbox', { name: /urgent/i }));
    expect(screen.getByRole('textbox', { name: /why urgent/i })).toBeInTheDocument();
    await userEvent.click(screen.getByTestId('add-row-items'));
    await userEvent.type(screen.getByRole('textbox', { name: /^name/i }), 'Pens');
    expect(onValues).toHaveBeenLastCalledWith({ urgent: true, items: [{ name: 'Pens' }] });
  });

  it('labels fields in Arabic', async () => {
    await render(<Harness onValues={() => undefined} />, 'ar');
    expect(screen.getByRole('textbox', { name: /العنوان/u })).toBeInTheDocument();
  });

  it('shows values read-only with tables as tables', async () => {
    await render(
      <FormValuesView schema={schema} values={{ title: 'Paper', urgent: false, items: [{ name: 'A4' }] }} />,
    );
    expect(screen.getByTestId('value-title')).toHaveTextContent('Paper');
    expect(screen.getByTestId('value-urgent')).toHaveTextContent('No');
    expect(screen.getByRole('table')).toHaveTextContent('A4');
  });

  it('renders rich text as inert text', () => {
    expect(plainText('<p>Hi<img src=x onerror="alert(1)"></p><script>bad()</script>')).toBe('Hi\nbad()');
  });
});
