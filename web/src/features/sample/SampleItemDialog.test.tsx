import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import type { AiCapabilities, ExtractionResult } from '@/features/ai/api';
import { renderWithProviders } from '@/test/render';
import { SampleItemDialog } from './SampleItemDialog';

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };

const on: AiCapabilities = {
  enabled: true,
  features: { extract: true, translate: true, similar: false, brief: true, ask: true },
};
const off: AiCapabilities = {
  enabled: false,
  features: { extract: false, translate: false, similar: false, brief: false, ask: false },
};

const extraction: ExtractionResult = {
  formKey: 'sample.item',
  fileName: 'business-case.docx',
  documentKind: 'docx',
  truncated: false,
  language: 'mixed',
  fields: [
    {
      name: 'nameEn',
      value: 'Digital archive',
      confidence: 0.92,
      source: 'Name (English): Digital archive',
      sourceVerified: true,
      locationKind: 'page',
      locationNumber: 2,
      fromScan: true,
    },
    {
      name: 'amount',
      value: '1250000',
      confidence: 0.35,
      source: 'not in the document',
      sourceVerified: false,
      locationKind: null,
      locationNumber: null,
      fromScan: false,
    },
  ],
  warnings: ['ai.extract.fromScan'],
  usageId: 'u-1',
  scannedPages: 1,
};

/** The POST spy, typed loosely: the generated paths type is too deep for vitest's MockInstance inference. */
/** The generated paths type is too deep for vitest's MockInstance inference: spy on a loosely typed view of the client. */
type LooseApi = { POST: (...args: unknown[]) => Promise<unknown> };

function mockApi(capabilities: AiCapabilities): { mock: { calls: unknown[][] } } {
  vi.spyOn(api as unknown as LooseGetApi, 'GET').mockResolvedValue({
    data: capabilities,
    response: new Response(null, { status: 200 }),
  });
  return vi
    .spyOn(api as unknown as LooseApi, 'POST')
    .mockImplementation(((path: string) =>
      Promise.resolve(
        path === '/api/v1/ai/forms/{formKey}/extract'
          ? { data: extraction, response: new Response(null, { status: 200 }) }
          : { data: { id: 'new', version: 1 }, response: new Response(null, { status: 201 }) },
      )) as never);
}

async function renderDialog(language: 'ar' | 'en' = 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <SampleItemDialog open onOpenChange={() => undefined} />
    </QueryClientProvider>,
    { language },
  );
}

describe('SampleItemDialog with AI', () => {
  afterEach(() => vi.restoreAllMocks());

  it('fills the form from a document as highlighted drafts and saves nothing until every value is reviewed', async () => {
    const post = mockApi(on);
    const user = userEvent.setup();
    await renderDialog();

    await user.click(await screen.findByRole('button', { name: 'Fill from a document' }));
    const input = document.querySelector<HTMLInputElement>('input[type="file"]');
    if (!input) throw new Error('no file input');
    await user.upload(input, new File(['x'], 'business-case.docx'));
    await user.click(screen.getByRole('button', { name: 'Read the document' }));

    await waitFor(() => expect(screen.getByLabelText('Name (English)')).toHaveValue('Digital archive'));
    expect(screen.getByLabelText('Amount (AED)')).toHaveValue('1250000');
    expect(screen.getAllByText('AI draft')).toHaveLength(2);
    expect(screen.getByText('Confidence 92%')).toBeInTheDocument();
    expect(screen.getByText('From scan')).toBeInTheDocument(); // read by OCR from a scanned page
    expect(screen.getByText('Source (page 2)')).toBeInTheDocument();
    expect(screen.getByText('Not found in the document: check this value carefully.')).toBeInTheDocument();
    expect(screen.getByText('Review the 2 AI-suggested values before saving.')).toBeInTheDocument();
    const create = screen.getByRole('button', { name: 'Create' });
    expect(create).toBeDisabled();

    // Editing a field counts as review; the other one is accepted explicitly.
    await user.type(screen.getByLabelText('Name (English)'), ' 2027');
    expect(screen.getAllByText('AI draft')).toHaveLength(1);
    expect(create).toBeDisabled();
    await user.click(screen.getByRole('button', { name: 'Accept' }));
    expect(create).toBeEnabled();
    expect(screen.queryByText('AI draft')).not.toBeInTheDocument();

    // Only the extraction call so far: nothing was saved by the AI.
    expect(post.mock.calls.map((call) => call[0])).toEqual(['/api/v1/ai/forms/{formKey}/extract']);
    await user.click(create);
    await waitFor(() => expect(post.mock.calls.map((call) => call[0])).toContain('/api/v1/sample/items'));
  });

  it('shows no AI action when AI is switched off', async () => {
    mockApi(off);
    await renderDialog('ar');
    await screen.findByRole('button', { name: 'إنشاء' });
    expect(screen.queryByRole('button', { name: 'التعبئة من مستند' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'ترجمة من الإنجليزية' })).not.toBeInTheDocument();
  });

  it('offers translation and marks the translated text as a draft in Arabic', async () => {
    vi.spyOn(api as unknown as LooseGetApi, 'GET').mockResolvedValue({
      data: on,
      response: new Response(null, { status: 200 }),
    });
    vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue({
      data: { translation: 'الأرشيف الرقمي', from: 'en', to: 'ar', usageId: 'u' },
      response: new Response(null, { status: 200 }),
    });
    const user = userEvent.setup();
    await renderDialog('ar');
    await user.type(await screen.findByLabelText('الاسم (إنجليزي)'), 'Digital archive');
    const [translateName] = screen.getAllByRole('button', { name: 'ترجمة من الإنجليزية' });
    if (!translateName) throw new Error('no translate button');
    await user.click(translateName);
    await waitFor(() => expect(screen.getByLabelText('الاسم (عربي)')).toHaveValue('الأرشيف الرقمي'));
    expect(screen.getByText('مسودة الذكاء الاصطناعي')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إنشاء' })).toBeDisabled();
  });
});
