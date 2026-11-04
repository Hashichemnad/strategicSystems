import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '@/api/client';
import { renderWithProviders } from '@/test/render';
import { TaskActionDialog } from './TaskActionDialog';

/** The generated paths type is too deep for vitest's MockInstance inference: spy on a loosely typed view of the client. */
type LooseApi = { POST: (...args: unknown[]) => Promise<unknown> };

const task = { id: 'task-1', version: 7, template: 'full' };

async function renderDialog(action: 'approve' | 'reject' | 'return', language: 'ar' | 'en' = 'en') {
  vi.spyOn(api, 'GET').mockResolvedValue({
    data: { enabled: false, features: {} },
    error: undefined,
    response: new Response(null, { status: 200 }),
  } as never);
  const post = vi.spyOn(api as unknown as LooseApi, 'POST').mockResolvedValue({
    data: { id: 'task-1' },
    error: undefined,
    response: new Response(null, { status: 200 }),
  });
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  await renderWithProviders(
    <QueryClientProvider client={client}>
      <TaskActionDialog task={task} action={action} feedbackScaleMax={5} onClose={() => undefined} />
    </QueryClientProvider>,
    { language },
  );
  return post;
}

describe('TaskActionDialog', () => {
  afterEach(() => vi.restoreAllMocks());

  it('requires a reason to reject and sends it with the version', async () => {
    const post = await renderDialog('reject');
    const confirm = screen.getByTestId('task-action-confirm');
    expect(confirm).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox', { name: /reason/i }), 'Not my area');
    expect(confirm).toBeEnabled();
    await userEvent.click(confirm);
    expect(post).toHaveBeenCalledWith(
      '/api/v1/tasks/{id}/reject',
      expect.objectContaining({
        params: { path: { id: 'task-1' } },
        body: expect.objectContaining({ version: 7, reason: 'Not my area' }) as unknown,
      }),
    );
  });

  it('requires a comment to return the task, in Arabic', async () => {
    await renderDialog('return', 'ar');
    expect(screen.getByRole('dialog', { name: 'إعادة لاستكمال العمل' })).toBeInTheDocument();
    expect(screen.getByTestId('task-action-confirm')).toBeDisabled();
    await userEvent.type(screen.getByRole('textbox'), 'أضف الملحق');
    expect(screen.getByTestId('task-action-confirm')).toBeEnabled();
  });

  it('asks for the feedback point before approving', async () => {
    await renderDialog('approve');
    expect(screen.getByTestId('task-action-confirm')).toBeDisabled();
    expect(screen.getByText('From 1 to 5')).toBeInTheDocument();
  });
});
