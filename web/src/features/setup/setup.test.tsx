import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { parseReleaseNotes } from './AboutDialog';
import { SetupPage } from './SetupPage';

const verifySetupToken = vi.fn<(token: string) => Promise<boolean>>();
const bootstrapSetup = vi.fn();

vi.mock('./api', () => ({
  setupStatusKey: ['setup', 'status'],
  verifySetupToken: (token: string) => verifySetupToken(token),
  bootstrapSetup: (...args: unknown[]) => bootstrapSetup(...args) as unknown,
}));

vi.mock('@tanstack/react-router', () => ({
  Link: ({ children }: { children: ReactNode }) => <a href="/login">{children}</a>,
}));

function renderSetup(language: 'ar' | 'en' = 'en') {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return renderWithProviders(
    <QueryClientProvider client={client}>
      <SetupPage />
    </QueryClientProvider>,
    { language },
  );
}

describe('SetupPage', () => {
  beforeEach(() => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response('{}', { status: 404 }));
    verifySetupToken.mockReset();
    bootstrapSetup.mockReset();
  });
  afterEach(() => vi.restoreAllMocks());

  it('refuses a wrong token, then walks through every step and creates the administrator', async () => {
    const user = userEvent.setup();
    await renderSetup();
    verifySetupToken.mockResolvedValueOnce(false).mockResolvedValueOnce(true);

    await user.type(screen.getByTestId('setup-token'), 'wrong');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByText(/This setup token is not valid/)).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Setup token' })).toBeInTheDocument();

    await user.clear(screen.getByTestId('setup-token'));
    await user.type(screen.getByTestId('setup-token'), 'the-token');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Organisation' })).toBeInTheDocument();

    // Required fields block the step.
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(screen.getByRole('heading', { name: 'Organisation' })).toBeInTheDocument();
    await user.type(screen.getByLabelText(/Organisation name \(Arabic\)/), 'هيئة أكمي');
    await user.type(screen.getByLabelText(/Organisation name \(English\)/), 'Acme Authority');
    await user.type(screen.getByLabelText(/Short code/), 'ACME');
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Branding' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Languages' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next' }));
    expect(await screen.findByRole('heading', { name: 'Administrator' })).toBeInTheDocument();

    await user.type(screen.getByLabelText(/Full name \(Arabic\)/), 'المسؤول');
    await user.type(screen.getByLabelText(/Full name \(English\)/), 'First Admin');
    await user.type(screen.getByLabelText(/Employee number/), 'A-1');
    await user.type(screen.getByLabelText(/E-mail \(user name\)/), 'admin@example.org');
    await user.type(screen.getByLabelText(/^Password/), 'Strong#Password2026');
    await user.type(screen.getByLabelText(/Confirm password/), 'Other#Password2026');
    await user.click(screen.getByRole('button', { name: 'Create the administrator' }));
    expect(await screen.findByText('The passwords do not match.')).toBeInTheDocument();
    expect(bootstrapSetup).not.toHaveBeenCalled();

    await user.clear(screen.getByLabelText(/Confirm password/));
    await user.type(screen.getByLabelText(/Confirm password/), 'Strong#Password2026');
    bootstrapSetup.mockResolvedValueOnce({ userId: 'u', userName: 'admin@example.org' });
    await user.click(screen.getByRole('button', { name: 'Create the administrator' }));

    await waitFor(() => expect(bootstrapSetup).toHaveBeenCalledTimes(1));
    const [token, body] = bootstrapSetup.mock.calls[0] as [string, Record<string, unknown>];
    expect(token).toBe('the-token');
    expect(body).toMatchObject({
      organisationNameEn: 'Acme Authority',
      organisationCode: 'ACME',
      timeZone: 'Asia/Dubai',
      currencyCode: 'AED',
      defaultLanguage: 'en',
      adminEmail: 'admin@example.org',
      adminEmployeeNo: 'A-1',
      emailHeaderEn: null,
    });
    expect(await screen.findByTestId('setup-done')).toHaveTextContent('admin@example.org');
  });

  it('is fully translated in Arabic', async () => {
    await renderSetup('ar');
    expect(screen.getByRole('heading', { name: 'إعداد النظام' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'رمز الإعداد' })).toBeInTheDocument();
  });
});

describe('parseReleaseNotes', () => {
  it('reads headings, bullets with continuation lines and paragraphs as text only', () => {
    const blocks = parseReleaseNotes(
      '# Version 1.0.0\n\nFirst <b>release</b>.\n\n## Highlights\n- One\n  continued\n- Two\n\nEnd.',
    );
    expect(blocks).toEqual([
      { kind: 'h1', text: 'Version 1.0.0' },
      { kind: 'p', text: 'First <b>release</b>.' },
      { kind: 'h2', text: 'Highlights' },
      { kind: 'ul', items: ['One continued', 'Two'] },
      { kind: 'p', text: 'End.' },
    ]);
  });
});
