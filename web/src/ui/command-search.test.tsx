import { useState } from 'react';
import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { CommandSearch, type CommandSearchGroup, type CommandSearchItem } from '@/ui';
import { matchRecordPath, recordMatchers } from '@/features/search/visits';

const groups: CommandSearchGroup[] = [
  {
    id: 'risk',
    heading: 'Risk Management',
    items: [
      { id: 'r1', label: 'RISK-0001 · Data centre outage', description: 'Risk' },
      { id: 'r2', label: 'RISK-0002 · Vendor delay', description: 'Risk' },
    ],
  },
  { id: 'tasks', heading: 'Tasks', items: [{ id: 't1', label: 'TSK-0009 · Prepare evidence' }] },
];

function Harness({
  onSelect,
  initial = '',
  data = groups,
  emptyText,
}: {
  onSelect: (item: CommandSearchItem) => void;
  initial?: string;
  data?: CommandSearchGroup[];
  emptyText?: string;
}) {
  const [value, setValue] = useState(initial);
  return (
    <CommandSearch
      value={value}
      onValueChange={setValue}
      groups={data}
      onSelect={onSelect}
      label="Search all systems"
      clearLabel="Clear search"
      emptyText={emptyText}
    />
  );
}

describe('CommandSearch', () => {
  it('is an ARIA combobox whose listbox groups the options under system headings', async () => {
    await renderWithProviders(<Harness onSelect={vi.fn()} />);
    const input = screen.getByRole('combobox', { name: 'Search all systems' });
    expect(input).toHaveAttribute('aria-expanded', 'false');

    await userEvent.click(input);

    expect(input).toHaveAttribute('aria-expanded', 'true');
    const listbox = screen.getByRole('listbox');
    expect(input).toHaveAttribute('aria-controls', listbox.id);
    const risk = within(listbox).getByRole('group', { name: 'Risk Management' });
    expect(within(risk).getAllByRole('option')).toHaveLength(2);
    expect(within(listbox).getByRole('group', { name: 'Tasks' })).toBeInTheDocument();
  });

  it('moves with arrow keys (wrapping), Home and End, and opens the active option with Enter', async () => {
    const onSelect = vi.fn();
    await renderWithProviders(<Harness onSelect={onSelect} />);
    const input = screen.getByRole('combobox');
    await userEvent.click(input);
    const options = screen.getAllByRole('option');
    const idOf = (index: number) => options[index]?.id ?? '';
    expect(options[0]).toHaveAttribute('aria-selected', 'true');
    expect(input).toHaveAttribute('aria-activedescendant', idOf(0));

    await userEvent.keyboard('{ArrowDown}{ArrowDown}');
    expect(input).toHaveAttribute('aria-activedescendant', idOf(2));
    await userEvent.keyboard('{ArrowDown}');
    expect(input).toHaveAttribute('aria-activedescendant', idOf(0)); // wraps to the first
    await userEvent.keyboard('{ArrowUp}');
    expect(input).toHaveAttribute('aria-activedescendant', idOf(2)); // and back to the last
    await userEvent.keyboard('{Home}');
    expect(input).toHaveAttribute('aria-activedescendant', idOf(0));
    await userEvent.keyboard('{End}{ArrowUp}');
    expect(input).toHaveAttribute('aria-activedescendant', idOf(1));

    await userEvent.keyboard('{Enter}');
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'r2' }));
    expect(input).toHaveAttribute('aria-expanded', 'false');
  });

  it('closes with Escape, clears with a second Escape and keeps focus in the input', async () => {
    await renderWithProviders(<Harness onSelect={vi.fn()} initial="risk" />);
    const input = screen.getByRole('combobox');
    await userEvent.click(input);
    expect(input).toHaveAttribute('aria-expanded', 'true');

    await userEvent.keyboard('{Escape}');
    expect(input).toHaveAttribute('aria-expanded', 'false');
    expect(input).toHaveValue('risk');
    expect(input).toHaveFocus();

    await userEvent.keyboard('{Escape}');
    expect(input).toHaveValue('');

    await userEvent.keyboard('{ArrowDown}');
    expect(input).toHaveAttribute('aria-expanded', 'true'); // arrow keys reopen the list
  });

  it('selects with the mouse and shows the empty text when there is nothing', async () => {
    const onSelect = vi.fn();
    const { unmount } = await renderWithProviders(<Harness onSelect={onSelect} />);
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.click(screen.getByRole('option', { name: /Prepare evidence/ }));
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 't1' }));
    unmount();

    await renderWithProviders(<Harness onSelect={onSelect} data={[]} emptyText="No results for “zz”." />);
    await userEvent.click(screen.getByRole('combobox'));
    expect(screen.getByText('No results for “zz”.')).toBeVisible();
    expect(screen.queryAllByRole('option')).toHaveLength(0);
  });

  it('works the same in Arabic (RTL)', async () => {
    const onSelect = vi.fn();
    await renderWithProviders(<Harness onSelect={onSelect} />, { language: 'ar' });
    await userEvent.click(screen.getByRole('combobox'));
    await userEvent.keyboard('{ArrowDown}{Enter}');
    expect(onSelect).toHaveBeenCalledWith(expect.objectContaining({ id: 'r2' }));
  });
});

describe('record visits', () => {
  const matchers = recordMatchers([
    {
      type: 'risk.risk',
      systemCode: 'risk',
      nameAr: 'خطر',
      nameEn: 'Risk',
      linkTemplate: '/risk/risks/{id}',
    },
    { type: 'tasks.task', systemCode: 'tasks', nameAr: 'مهمة', nameEn: 'Task', linkTemplate: '/tasks/{id}' },
  ]);

  it('recognises record pages from the link templates', () => {
    const id = '01a1b6bb-3b48-7bf5-bc9c-ec1b203f76ec';
    expect(matchRecordPath(`/risk/risks/${id}`, matchers)).toEqual({ type: 'risk.risk', id });
    expect(matchRecordPath(`/tasks/${id.toUpperCase()}/`, matchers)).toEqual({ type: 'tasks.task', id });
    expect(matchRecordPath('/risk/risks', matchers)).toBeNull();
    expect(matchRecordPath(`/risk/risks/${id}/history`, matchers)).toBeNull();
    expect(matchRecordPath('/tasks/new', matchers)).toBeNull();
  });
});
