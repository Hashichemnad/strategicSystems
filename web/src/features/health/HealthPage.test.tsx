import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import type { components } from '@/api/schema';
import { renderWithProviders } from '@/test/render';
import { formatBytes, formatParams, messagePath } from './healthFormat';
import { HealthPage } from './HealthPage';

type Report = components['schemas']['AdminHealthReport'];

/** The generated client is too large for TypeScript to infer the spy's types (TS2589). */
type LooseGetApi = { GET: (...args: unknown[]) => Promise<unknown> };

const report: Report = {
  status: 'error',
  checkedAt: '2026-11-06T08:00:00Z',
  checks: [
    {
      code: 'database',
      group: 'platform',
      status: 'ok',
      messageKey: 'health.database.ok',
      params: { latencyMs: '3', version: '16.4', sizeBytes: '52428800' },
    },
    {
      code: 'scheduler',
      group: 'jobs',
      status: 'warning',
      messageKey: 'health.scheduler.attention',
      params: { jobs: '2', failing: '1' },
      items: [
        {
          code: 'audit.verify-chain',
          status: 'error',
          messageKey: 'health.job.lastFailed',
          params: {
            lastRunAt: '2026-11-06T02:10:00Z',
            nextRunAt: '2026-11-07T02:10:00Z',
            failures24h: '1',
            cron: '0 10 2 * * ?',
          },
          labelAr: 'التحقق من سلامة سجل التدقيق',
          labelEn: 'Verify the audit chain',
        },
        {
          code: 'notifications.digest',
          status: 'ok',
          messageKey: 'health.job.notRunYet',
          params: { failures24h: '0', cron: '0 0 7 * * ?' },
          labelAr: 'الملخص',
          labelEn: 'Digest',
        },
      ],
    },
    { code: 'pdf', group: 'integrations', status: 'error', messageKey: 'health.pdf.unreachable', params: {} },
    { code: 'ai', group: 'integrations', status: 'notConfigured', messageKey: 'health.ai.off' },
  ],
};

function stub(value: Report) {
  vi.spyOn(api as unknown as LooseGetApi, 'GET').mockResolvedValue({
    data: value,
    error: undefined,
    response: new Response(null, { status: 200 }),
  });
}

async function renderPage(language: 'ar' | 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <HealthPage />
    </QueryClientProvider>,
    { language },
  );
}

describe('HealthPage', () => {
  afterEach(() => vi.restoreAllMocks());

  it('shows the overall status, each check by group with translated messages and the jobs table', async () => {
    stub(report);
    await renderPage('en');

    expect(await screen.findByRole('heading', { level: 1, name: 'System health' })).toBeInTheDocument();
    expect(screen.getByText('Problems found')).toBeInTheDocument();
    const database = screen.getByTestId('health-check-database');
    expect(within(database).getByText('Database')).toBeInTheDocument();
    expect(
      within(database).getByText('PostgreSQL 16.4 answered in 3 ms; database size 50 MB.'),
    ).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'Scheduled jobs and queue' })).toBeInTheDocument();
    const [failed, notRun, ...others] = screen.getAllByTestId('health-job-row');
    if (!failed || !notRun) throw new Error('two job rows expected');
    expect(others).toHaveLength(0);
    expect(within(failed).getByText('Verify the audit chain')).toBeInTheDocument();
    expect(within(failed).getByText('The last run failed.')).toBeInTheDocument();
    expect(within(notRun).getByText('Not run yet')).toBeInTheDocument();
    expect(
      within(screen.getByTestId('health-check-pdf')).getByText(
        'Gotenberg cannot be reached: PDF exports and reports fail.',
      ),
    ).toBeInTheDocument();
    expect(within(screen.getByTestId('health-check-ai')).getByText('Not configured')).toBeInTheDocument();
  });

  it('reads in Arabic', async () => {
    stub(report);
    await renderPage('ar');

    expect(await screen.findByRole('heading', { level: 1, name: 'صحة النظام' })).toBeInTheDocument();
    expect(screen.getByText('توجد مشكلات')).toBeInTheDocument();
    expect(screen.getByText('التحقق من سلامة سجل التدقيق')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 2, name: 'التكاملات' })).toBeInTheDocument();
  });
});

describe('health formatting', () => {
  it('formats sizes, numbers and dates and keeps codes as they are', () => {
    expect(formatBytes(1536, 'en')).toBe('1.5 KB');
    expect(formatBytes(5 * 1024 ** 3, 'en')).toBe('5 GB');
    const params = formatParams(
      { freeBytes: '1048576', count: '1234', host: 'smtp.example.org', port: '587' },
      'en',
    );
    expect(params).toEqual({ freeBytes: '1 MB', count: '1,234', host: 'smtp.example.org', port: '587' });
    expect(messagePath('health.database.ok')).toBe('database.ok');
  });
});
