import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { ThemeProvider, type UiTheme } from '@/app/theme';
import { formatNumber } from '@/lib/format';
import { writePreference } from '@/lib/preferences';
import { renderWithProviders } from '@/test/render';
import { RecordLayout, type RecordSection } from './record-layout';
import { narrowQuery } from './record-side-menu';

const sections: RecordSection[] = [
  { key: 'overview', label: 'Overview', content: <p>Overview content</p> },
  { key: 'milestones', label: 'Milestones', count: 12, content: <p>Milestones content</p> },
  { key: 'secret', label: 'Secret', hidden: true, content: <p>Secret content</p> },
  { key: 'risks', label: 'Risks', count: 0, testId: 'risk-tab', content: <p>Risks content</p> },
  { key: 'history', label: 'History', content: <p>History content</p> },
];

/** A matchMedia whose `narrowQuery` answer can change while the page is open. */
function mockViewport(narrow: boolean) {
  const listeners = new Set<() => void>();
  const state = { narrow };
  vi.spyOn(window, 'matchMedia').mockImplementation(
    (query: string) =>
      ({
        get matches() {
          return query === narrowQuery ? state.narrow : false;
        },
        media: query,
        onchange: null,
        addEventListener: (_: string, listener: () => void) => listeners.add(listener),
        removeEventListener: (_: string, listener: () => void) => listeners.delete(listener),
        addListener: () => undefined,
        removeListener: () => undefined,
        dispatchEvent: () => false,
      }) as unknown as MediaQueryList,
  );
  return (next: boolean) => {
    state.narrow = next;
    act(() => listeners.forEach((listener) => listener()));
  };
}

function Page({ onChange, initial = 'overview' }: { onChange?: (key: string) => void; initial?: string }) {
  const [active, setActive] = useState(initial);
  return (
    <ThemeProvider>
      <RecordLayout
        testId="record"
        header={<h1>Record PR-0001</h1>}
        highlights={[{ key: 'owner', label: 'Owner', value: 'Mariam' }]}
        sections={sections}
        activeSection={active}
        onSectionChange={(key) => {
          onChange?.(key);
          setActive(key);
        }}
      >
        <div data-testid="dialogs" />
      </RecordLayout>
    </ThemeProvider>
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  window.localStorage.clear();
});

describe('RecordLayout side menu (ADR 0221)', () => {
  it.each<UiTheme>(['classic', 'enterprise', 'modern'])(
    'lists every visible section in order with counts and no tab strip (%s)',
    async (theme) => {
      writePreference('uiTheme', theme);
      await renderWithProviders(<Page />);
      expect(screen.getByTestId('record')).toHaveAttribute('data-record-layout', theme);
      expect(screen.queryByRole('tablist')).not.toBeInTheDocument();
      const nav = screen.getByRole('navigation', { name: 'Record sections' });
      const items = within(nav).getAllByRole('button');
      expect(items.map((item) => item.dataset.section)).toEqual([
        'overview',
        'milestones',
        'risks',
        'history',
      ]);
      expect(items[1]).toHaveTextContent('Milestones12');
      expect(items[1]).toHaveTextContent('12 items');
      expect(items[2]).toHaveTextContent('0');
      expect(items[0]).toHaveAttribute('aria-current', 'page');
      expect(items[1]).not.toHaveAttribute('aria-current');
      expect(screen.getByRole('region', { name: 'Overview' })).toHaveTextContent('Overview content');
      expect(screen.queryByText('Milestones content')).not.toBeInTheDocument();
      expect(screen.queryByText('Secret content')).not.toBeInTheDocument();
      expect(screen.getByTestId('dialogs')).toBeInTheDocument();
      // The old related-lists aside is merged into the menu.
      expect(screen.queryByTestId(/^related-/)).not.toBeInTheDocument();
    },
  );

  it('calls onSectionChange and shows only the chosen section', async () => {
    const onChange = vi.fn();
    await renderWithProviders(<Page onChange={onChange} />);
    await userEvent.click(screen.getByTestId('risk-tab'));
    expect(onChange).toHaveBeenCalledWith('risks');
    expect(screen.getByTestId('risk-tab')).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('region', { name: 'Risks' })).toHaveTextContent('Risks content');
    expect(screen.queryByText('Overview content')).not.toBeInTheDocument();
  });

  it('falls back to the first section for an unknown or hidden key', async () => {
    await renderWithProviders(<Page initial="secret" />);
    expect(screen.getByTestId('record-section-overview')).toHaveAttribute('aria-current', 'page');
    expect(screen.queryByText('Secret content')).not.toBeInTheDocument();
  });

  it('moves focus with the arrow keys, Home and End, and is one Tab stop', async () => {
    await renderWithProviders(<Page initial="milestones" />);
    const nav = screen.getByRole('navigation', { name: 'Record sections' });
    const items = within(nav).getAllByRole('button');
    expect(items.map((item) => item.tabIndex)).toEqual([-1, 0, -1, -1]);
    items[1]?.focus();
    await userEvent.keyboard('{ArrowDown}');
    expect(items[2]).toHaveFocus();
    await userEvent.keyboard('{End}');
    expect(items[3]).toHaveFocus();
    await userEvent.keyboard('{ArrowDown}');
    expect(items[0]).toHaveFocus();
    await userEvent.keyboard('{ArrowUp}');
    expect(items[3]).toHaveFocus();
    await userEvent.keyboard('{Home}');
    expect(items[0]).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(items[0]).toHaveAttribute('aria-current', 'page');
  });

  it('renders mirrored in Arabic (RTL) with counts formatted for Arabic', async () => {
    document.documentElement.dir = 'rtl';
    await renderWithProviders(<Page />, { language: 'ar' });
    const nav = screen.getByRole('navigation', { name: 'أقسام السجل' });
    expect(within(nav).getByTestId('record-section-milestones')).toHaveTextContent(formatNumber(12, 'ar'));
    // Logical properties only: the classes say start / end, never left / right.
    expect(nav.innerHTML).not.toMatch(/\b(ml|mr|pl|pr|left|right)-/);
    document.documentElement.dir = '';
  });

  it('is a drawer below md: the Sections button opens it, choosing closes it and focus returns', async () => {
    mockViewport(true);
    const onChange = vi.fn();
    await renderWithProviders(<Page onChange={onChange} />);
    expect(screen.queryByRole('navigation', { name: 'Record sections' })).not.toBeInTheDocument();
    const button = screen.getByTestId('record-sections-button');
    expect(button).toHaveTextContent('Overview');
    await userEvent.click(button);
    const drawer = await screen.findByRole('dialog', { name: 'Record sections' });
    const nav = within(drawer).getByRole('navigation', { name: 'Record sections' });
    await userEvent.click(within(nav).getByTestId('record-section-milestones'));
    expect(onChange).toHaveBeenCalledWith('milestones');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(button).toHaveFocus());
    expect(button).toHaveTextContent('Milestones');
    expect(screen.getByRole('region', { name: 'Milestones' })).toHaveTextContent('Milestones content');
  });

  it('closes the drawer with Escape and switches to the inline menu when the viewport widens', async () => {
    const resize = mockViewport(true);
    await renderWithProviders(<Page />);
    await userEvent.click(screen.getByTestId('record-sections-button'));
    expect(await screen.findByRole('dialog')).toBeInTheDocument();
    await userEvent.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    resize(false);
    expect(screen.queryByTestId('record-sections-button')).not.toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: 'Record sections' })).toBeInTheDocument();
  });
});
