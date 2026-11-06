import { fireEvent, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import { HeatGrid, moveFocus } from './HeatGrid';
import {
  addMonths,
  changeKind,
  currentPeriod,
  fromSearch,
  monthCount,
  monthsOf,
  periodRange,
  periodToDates,
  previousPeriod,
  shiftPeriod,
  toSearch,
  type PeriodValue,
} from './period';
import { OptionalPeriodSelector, PeriodSelector } from './PeriodSelector';
import { ProgressBar } from './parts';
import { RagGrid } from './RagGrid';
import { StrategyMap, strategyMapLayout } from './StrategyMap';
import { deltaClass, Tile, tileDelta } from './Tile';

describe('period helpers', () => {
  it('computes month, quarter, year and custom ranges', () => {
    expect(periodRange('month', '2026-05')).toEqual({ from: '2026-05', to: '2026-05' });
    expect(periodRange('quarter', '2026-05')).toEqual({ from: '2026-04', to: '2026-06' });
    expect(periodRange('quarter', '2026-12')).toEqual({ from: '2026-10', to: '2026-12' });
    expect(periodRange('year', '2026-05')).toEqual({ from: '2026-01', to: '2026-12' });
    expect(periodRange('custom', '2026-05')).toEqual({ from: '2026-05', to: '2026-05' });
    expect(currentPeriod('quarter', new Date(2026, 10, 5))).toEqual({
      kind: 'quarter',
      from: '2026-10',
      to: '2026-12',
      compare: false,
    });
  });

  it('adds months across years and counts them', () => {
    expect(addMonths('2026-01', -1)).toBe('2025-12');
    expect(addMonths('2025-11', 14)).toBe('2027-01');
    expect(monthCount('2025-11', '2026-02')).toBe(4);
    expect(monthsOf({ from: '2025-12', to: '2026-02' })).toEqual(['2025-12', '2026-01', '2026-02']);
  });

  it('gives the previous period of the same length', () => {
    const p = (kind: PeriodValue['kind'], from: string, to: string): PeriodValue => ({
      kind,
      from,
      to,
      compare: true,
    });
    expect(previousPeriod(p('month', '2026-01', '2026-01'))).toMatchObject({
      from: '2025-12',
      to: '2025-12',
    });
    expect(previousPeriod(p('quarter', '2026-01', '2026-03'))).toMatchObject({
      from: '2025-10',
      to: '2025-12',
    });
    expect(previousPeriod(p('year', '2026-01', '2026-12'))).toMatchObject({ from: '2025-01', to: '2025-12' });
    expect(previousPeriod(p('custom', '2026-02', '2026-06'))).toMatchObject({
      from: '2025-09',
      to: '2026-01',
    });
  });

  it('steps and changes kind', () => {
    const q: PeriodValue = { kind: 'quarter', from: '2026-10', to: '2026-12', compare: false };
    expect(shiftPeriod(q, 1)).toMatchObject({ from: '2027-01', to: '2027-03' });
    expect(changeKind(q, 'year')).toMatchObject({ kind: 'year', from: '2026-01', to: '2026-12' });
    expect(changeKind(q, 'custom')).toMatchObject({ kind: 'custom', from: '2026-10', to: '2026-12' });
  });

  it('round-trips through the URL and ignores invalid values', () => {
    const value: PeriodValue = { kind: 'custom', from: '2025-11', to: '2026-02', compare: true };
    expect(toSearch(value)).toEqual({ period: 'custom', from: '2025-11', to: '2026-02', compare: '1' });
    expect(fromSearch(toSearch(value))).toEqual(value);
    const quarter: PeriodValue = { kind: 'quarter', from: '2026-04', to: '2026-06', compare: false };
    expect(fromSearch(toSearch(quarter))).toEqual(quarter);
    const fallback: PeriodValue = { kind: 'month', from: '2026-11', to: '2026-11', compare: false };
    expect(fromSearch({ period: 'weekly', from: '2026-13' }, fallback)).toEqual(fallback);
    expect(fromSearch({ period: 'year' }, fallback)).toEqual({
      kind: 'year',
      from: '2026-01',
      to: '2026-12',
      compare: false,
    });
    // A quarter given from a mid-quarter month snaps to the quarter.
    expect(fromSearch({ period: 'quarter', from: '2026-05', to: '2026-05' })).toMatchObject({
      from: '2026-04',
      to: '2026-06',
    });
  });
});

describe('periodToDates', () => {
  it('gives the first and last day of the period (leap years included)', () => {
    expect(periodToDates({ from: '2026-01', to: '2026-03' })).toEqual({
      from: '2026-01-01',
      to: '2026-03-31',
    });
    expect(periodToDates({ from: '2028-02', to: '2028-02' })).toEqual({
      from: '2028-02-01',
      to: '2028-02-29',
    });
    expect(periodToDates({ from: '2025-11', to: '2026-02' })).toEqual({
      from: '2025-11-01',
      to: '2026-02-28',
    });
  });
});

describe('OptionalPeriodSelector', () => {
  it('shows "All periods" without a period and the step buttons with one', async () => {
    const { rerender } = await renderWithProviders(
      <OptionalPeriodSelector value={null} onChange={() => undefined} />,
    );
    expect(screen.getByRole('combobox', { name: 'Period type' })).toHaveTextContent('All periods');
    expect(screen.queryByRole('button', { name: 'Previous period' })).not.toBeInTheDocument();
    rerender(
      <OptionalPeriodSelector
        value={{ kind: 'year', from: '2026-01', to: '2026-12', compare: false }}
        onChange={() => undefined}
        allowCompare={false}
      />,
    );
    expect(screen.getByText('2026')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Previous period' })).toBeInTheDocument();
    expect(screen.queryByRole('switch')).not.toBeInTheDocument();
  });

  it('names "All periods" in Arabic', async () => {
    await renderWithProviders(<OptionalPeriodSelector value={null} onChange={() => undefined} />, {
      language: 'ar',
    });
    expect(screen.getByRole('combobox', { name: 'نوع الفترة' })).toHaveTextContent('كل الفترات');
  });
});

describe('PeriodSelector', () => {
  function Harness({ onChange }: { onChange: (v: PeriodValue) => void }) {
    const [value, setValue] = useState<PeriodValue>({
      kind: 'quarter',
      from: '2026-01',
      to: '2026-03',
      compare: false,
    });
    return (
      <PeriodSelector
        value={value}
        onChange={(v) => {
          setValue(v);
          onChange(v);
        }}
      />
    );
  }

  it('shows the quarter, steps back and turns the comparison on', async () => {
    const onChange = vi.fn();
    await renderWithProviders(<Harness onChange={onChange} />);
    expect(screen.getByText('Q1 2026')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Previous period' }));
    expect(screen.getByText('Q4 2025')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('switch', { name: 'Compare with previous period' }));
    expect(onChange).toHaveBeenLastCalledWith({
      kind: 'quarter',
      from: '2025-10',
      to: '2025-12',
      compare: true,
    });
  });

  it('labels the period in Arabic', async () => {
    await renderWithProviders(<Harness onChange={() => undefined} />, { language: 'ar' });
    expect(screen.getByText('الربع 1 من 2026')).toBeInTheDocument();
    expect(screen.getByRole('switch', { name: 'المقارنة بالفترة السابقة' })).toBeInTheDocument();
  });
});

describe('Tile', () => {
  it('judges the delta by direction', () => {
    expect(tileDelta(12, 10)).toEqual({ direction: 'up', amount: 2, good: true });
    expect(tileDelta(12, 10, false)).toEqual({ direction: 'up', amount: 2, good: false });
    expect(tileDelta(8, 10, false)).toEqual({ direction: 'down', amount: 2, good: true });
    expect(tileDelta(5, 5)).toEqual({ direction: 'same', amount: 0, good: null });
    expect(deltaClass(tileDelta(12, 10))).toBe('text-success');
    expect(deltaClass(tileDelta(12, 10, false))).toBe('text-danger');
    expect(deltaClass(tileDelta(5, 5))).toBe('text-muted-foreground');
  });

  it('formats the value and colours the delta (overdue going up is bad)', async () => {
    await renderWithProviders(
      <Tile label="Overdue" value={1200} previous={1000} higherIsBetter={false} rag="red" />,
    );
    expect(screen.getByTestId('tile-value')).toHaveTextContent('1,200');
    const delta = screen.getByTestId('tile-delta');
    expect(delta).toHaveTextContent('Up 200 on the previous period');
    expect(delta).toHaveClass('text-danger');
    expect(screen.getByText('Off track')).toHaveClass('sr-only');
  });

  it('shows a good drop in green', async () => {
    await renderWithProviders(
      <Tile label="Late" value={3} previous={5} higherIsBetter={false} sparkline={[5, 4, 3]} />,
    );
    expect(screen.getByTestId('tile-delta')).toHaveClass('text-success');
    expect(screen.getByTestId('tile-sparkline')).toBeInTheDocument();
  });
});

describe('ProgressBar', () => {
  it('is a named progressbar when labelled, decorative otherwise', async () => {
    await renderWithProviders(
      <>
        <ProgressBar value={0.425} label="Progress" />
        <ProgressBar value={1.2} />
      </>,
    );
    const bar = screen.getByRole('progressbar', { name: 'Progress' });
    expect(bar).toHaveAttribute('aria-valuenow', '43');
    expect(screen.getAllByRole('progressbar')).toHaveLength(1);
    expect(screen.getByText('120 %')).toBeInTheDocument();
  });
});

describe('HeatGrid', () => {
  const rows = [3, 2, 1].map((n) => ({ key: String(n), label: n }));
  const columns = [1, 2, 3].map((n) => ({ key: String(n), label: n }));
  const cell = (r: { key: string }, c: { key: string }) => ({
    label: `L${r.key} I${c.key}`,
    content: Number(r.key) * Number(c.key),
    testId: `cell-${r.key}-${c.key}`,
  });

  it('moves with the arrow keys, mirrored in RTL', () => {
    const size = { rows: 3, columns: 3 };
    expect(moveFocus('ArrowRight', { row: 0, column: 0 }, size, false)).toEqual({ row: 0, column: 1 });
    expect(moveFocus('ArrowRight', { row: 0, column: 1 }, size, true)).toEqual({ row: 0, column: 0 });
    expect(moveFocus('ArrowDown', { row: 2, column: 0 }, size, false)).toEqual({ row: 2, column: 0 });
    expect(moveFocus('End', { row: 1, column: 0 }, size, false)).toEqual({ row: 1, column: 2 });
    expect(moveFocus('a', { row: 1, column: 0 }, size, false)).toBeNull();
  });

  it('has one tab stop, names every cell, and clicks from the keyboard', async () => {
    const onCellClick = vi.fn();
    await renderWithProviders(
      <HeatGrid label="Heat" rows={rows} columns={columns} cell={cell} onCellClick={onCellClick} />,
    );
    expect(screen.getByRole('grid', { name: 'Heat' })).toBeInTheDocument();
    const cells = screen.getAllByRole('gridcell');
    expect(cells).toHaveLength(9);
    expect(cells.filter((c) => c.tabIndex === 0)).toHaveLength(1);
    expect(screen.getByTestId('cell-3-1')).toHaveAccessibleName(/L3 I1$/u);
    screen.getByTestId('cell-3-1').focus();
    fireEvent.keyDown(screen.getByTestId('cell-3-1'), { key: 'ArrowDown' });
    expect(screen.getByTestId('cell-2-1')).toHaveFocus();
    await userEvent.keyboard('{Enter}');
    expect(onCellClick).toHaveBeenCalledWith('2', '1');
  });
});

describe('RagGrid', () => {
  it('renders RAG cells with names and row / cell drill-down', async () => {
    const onRowClick = vi.fn();
    const onCellClick = vi.fn();
    await renderWithProviders(
      <RagGrid
        label="Department scorecard"
        rowHeader="Department"
        columns={[
          { key: 'kpi', label: 'KPI' },
          { key: 'risk', label: 'Risk' },
        ]}
        rows={[{ key: 'it', label: 'IT', text: 'IT', cells: { kpi: { rag: 'green', value: '92 %' } } }]}
        onRowClick={onRowClick}
        onCellClick={onCellClick}
      />,
    );
    expect(screen.getByRole('table', { name: 'Department scorecard' })).toBeInTheDocument();
    const cellButton = screen.getByRole('button', { name: 'IT, KPI: 92 % · On track' });
    cellButton.focus();
    await userEvent.keyboard('{Enter}');
    expect(onCellClick).toHaveBeenCalledWith('it', 'kpi');
    await userEvent.click(screen.getByRole('button', { name: 'IT' }));
    expect(onRowClick).toHaveBeenCalledWith('it');
    expect(screen.getByText('no value')).toHaveClass('sr-only');
  });
});

describe('StrategyMap', () => {
  const perspectives = [
    { key: 'customer', label: 'Customer' },
    { key: 'process', label: 'Internal process' },
  ];
  const objectives = [
    { key: 'o1', perspective: 'customer', label: 'Happy customers', score: 0.92, rag: 'green' as const },
    { key: 'o2', perspective: 'process', label: 'Faster services', score: null, rag: 'grey' as const },
    { key: 'o3', perspective: 'process', label: 'Digital channels', score: 0.6, rag: 'red' as const },
  ];

  it('mirrors the layout in RTL', () => {
    const ltr = strategyMapLayout(perspectives, objectives, [{ from: 'o2', to: 'o1' }], { rtl: false });
    const rtl = strategyMapLayout(perspectives, objectives, [{ from: 'o2', to: 'o1' }], { rtl: true });
    expect(ltr.bands).toHaveLength(2);
    const x = (layout: typeof ltr, key: string) => layout.nodes.find((n) => n.key === key)?.x ?? 0;
    expect(x(ltr, 'o2')).toBeLessThan(x(ltr, 'o3'));
    expect(x(rtl, 'o2')).toBeGreaterThan(x(rtl, 'o3'));
    expect(x(rtl, 'o1')).toBe(ltr.width - x(ltr, 'o1'));
    expect(ltr.arrows).toHaveLength(1);
    expect(ltr.arrows[0]?.y1).toBeLessThan(ltr.nodes.find((n) => n.key === 'o2')?.y ?? 0);
  });

  it('renders focusable nodes named with score and RAG in English', async () => {
    const onSelect = vi.fn();
    await renderWithProviders(
      <StrategyMap perspectives={perspectives} objectives={objectives} onSelect={onSelect} />,
    );
    const node = screen.getByRole('button', { name: 'Happy customers: 92 %, On track' });
    expect(screen.getByRole('button', { name: 'Faster services: no score, No data' })).toBeInTheDocument();
    node.focus();
    await userEvent.keyboard('{Enter}');
    expect(onSelect).toHaveBeenCalledWith('o1');
  });

  it('names the nodes in Arabic', async () => {
    await renderWithProviders(<StrategyMap perspectives={perspectives} objectives={objectives} />, {
      language: 'ar',
    });
    expect(screen.getByRole('group', { name: 'خريطة الاستراتيجية' })).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'Digital channels: 60 %، خارج المسار' })).toBeInTheDocument();
  });
});
