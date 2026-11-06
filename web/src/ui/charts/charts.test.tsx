import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { renderWithProviders } from '@/test/render';
import {
  barOption,
  bubbleOption,
  funnelOption,
  gaugeOption,
  lineOption,
  resolveTokenColors,
  token,
  topN,
} from './builders';
import { Chart } from './Chart';
import { applyDirection, prepareOption } from './direction';
import type { ChartTokens } from './theme';
import type { ChartOption } from './types';

// A stand-in for the ECharts runtime (jsdom has no layout): the wrapper's options, table and accessibility are under test here.
const runtime = vi.hoisted(() => ({ fail: false, options: [] as unknown[] }));
vi.mock('./echarts-runtime', () => ({
  init: () => {
    if (runtime.fail) throw new Error('no chart in this test');
    return {
      on: () => undefined,
      setOption: (option: unknown) => runtime.options.push(option),
      resize: () => undefined,
      dispose: () => undefined,
      getDataURL: () => 'data:image/svg+xml;base64,',
      getWidth: () => 100,
      getHeight: () => 100,
    };
  },
}));

type Obj = Record<string, unknown>;
const get = (option: ChartOption, key: string) => (option as Obj)[key] as Obj;

describe('applyDirection', () => {
  const ltr: ChartOption = {
    grid: { left: 8, right: 40 },
    legend: { top: 0, left: 0 },
    xAxis: { type: 'category', data: ['a', 'b'] },
    yAxis: { type: 'value' },
    series: [{ type: 'bar', data: [1, 2], label: { show: true, position: 'right' } }],
  };

  it('leaves left-to-right options untouched', () => {
    expect(applyDirection(ltr, false)).toBe(ltr);
  });

  it('inverts the x axis and moves the y axis to the right in RTL', () => {
    const rtl = applyDirection(ltr, true);
    expect(get(rtl, 'xAxis').inverse).toBe(true);
    expect(get(rtl, 'yAxis').position).toBe('right');
  });

  it('keeps an axis the caller already inverted the right way round (XOR)', () => {
    const rtl = applyDirection({ ...ltr, xAxis: { type: 'value', inverse: true } }, true);
    expect(get(rtl, 'xAxis').inverse).toBe(false);
  });

  it('puts the legend on the start (right) side and swaps grid margins', () => {
    const rtl = applyDirection(ltr, true);
    expect(get(rtl, 'legend')).toMatchObject({ right: 0, align: 'right' });
    expect(get(rtl, 'legend').left).toBeUndefined();
    expect(get(rtl, 'grid')).toEqual({ left: 40, right: 8 });
  });

  it('places a legend without a side on the right and swaps a second y axis to the left', () => {
    const rtl = applyDirection(
      {
        legend: {},
        yAxis: [{ type: 'value' }, { type: 'value' }],
        visualMap: { left: 'left' },
      },
      true,
    );
    expect(get(rtl, 'legend').right).toBe(0);
    const axes = (rtl as Obj).yAxis as Obj[];
    expect(axes.map((a) => a.position)).toEqual(['right', 'left']);
    expect(get(rtl, 'visualMap').right).toBe('right');
  });

  it('mirrors series label positions', () => {
    const rtl = applyDirection(ltr, true);
    const series = (rtl as Obj).series as { label: Obj }[];
    expect(series[0]?.label.position).toBe('left');
  });

  it('adds the aria description', () => {
    const prepared = prepareOption(ltr, { rtl: false, description: 'Projects by stage' });
    expect(get(prepared, 'aria')).toEqual({
      enabled: true,
      label: { enabled: true, description: 'Projects by stage' },
    });
  });
});

describe('builders', () => {
  const categories = [
    { key: 'a', label: 'A' },
    { key: 'b', label: 'B' },
    { key: 'c', label: 'C' },
    { key: 'd', label: 'D' },
  ];

  it('keeps the top N and adds the rest up as others', () => {
    const result = topN(categories, [{ name: 'n', values: [1, 9, 4, 2] }], 2, 'Others');
    expect(result.categories.map((c) => c.key)).toEqual(['b', 'c', '__others']);
    expect(result.series[0]?.values).toEqual([9, 4, 3]);
  });

  it('builds a horizontal stacked bar with a table of the same data', () => {
    const spec = barOption({
      categories,
      series: [
        { name: 'Open', values: [1, 2, 3, 4] },
        { name: 'Late', values: [0, 1, null, 2] },
      ],
      orientation: 'horizontal',
      stacked: true,
      categoryHeader: 'Unit',
    });
    expect(get(spec.option, 'yAxis')).toMatchObject({ type: 'category', inverse: true });
    expect(get(spec.option, 'xAxis').type).toBe('value');
    const series = (spec.option as Obj).series as Obj[];
    expect(series.every((s) => s.stack === 'total')).toBe(true);
    expect(spec.table.columns).toEqual(['Unit', 'Open', 'Late']);
    expect(spec.table.rows[2]).toEqual(['C', '3', null]);
    expect(spec.table.rowKeys).toEqual(['a', 'b', 'c', 'd']);
  });

  it('draws the previous period dashed in the line chart', () => {
    const spec = lineOption({
      categories: categories.slice(0, 2),
      series: [{ name: 'Opened', values: [1, 2] }],
      previous: [{ name: 'Opened (previous)', values: [3, 4] }],
    });
    const series = (spec.option as Obj).series as { lineStyle: Obj }[];
    expect(series).toHaveLength(2);
    expect(series[1]?.lineStyle.type).toBe('dashed');
    expect(spec.table.columns).toEqual(['', 'Opened', 'Opened (previous)']);
  });

  it('builds bubble, gauge and funnel options with tables', () => {
    const bubble = bubbleOption({
      groups: [{ name: 'Green', points: [{ key: 'p1', label: 'P1', x: 50, y: 3, size: null }] }],
      x: { name: 'Value' },
      y: { name: 'Complexity' },
      size: { name: 'Budget', hidden: 'Hidden' },
    });
    expect(bubble.table.rows[0]).toEqual(['P1', '50', '3', 'Hidden', 'Green']);
    expect(gaugeOption({ name: 'On time', value: null }).table.rows).toEqual([[null]]);
    const funnel = funnelOption({ steps: [{ key: 's', label: 'Submitted', value: 5 }] });
    expect(funnel.table.rowKeys).toEqual(['s']);
  });

  it('resolves design-token colours for the theme', () => {
    const tokens = { danger: '#ff0000', mutedForeground: '#777777' } as ChartTokens;
    const formatter = () => '';
    const resolved = resolveTokenColors(
      { series: [{ itemStyle: { color: token('danger') } }, { color: token('neutral'), formatter }] },
      tokens,
    );
    expect(resolved.series[0]?.itemStyle?.color).toBe('#ff0000');
    expect(resolved.series[1]?.color).toBe('#777777');
    expect(resolved.series[1]?.formatter).toBe(formatter);
  });
});

describe('Chart', () => {
  const spec = barOption({
    categories: [
      { key: 'open', label: 'Open' },
      { key: 'closed', label: 'Closed' },
    ],
    series: [{ name: 'Findings', values: [12, 1500] }],
    categoryHeader: 'Status',
  });

  it('is an image with a name and toggles an accessible table of the same data', async () => {
    runtime.fail = false;
    await renderWithProviders(<Chart {...spec} label="Findings by status" testId="chart" />);
    expect(screen.getByRole('img', { name: 'Findings by status' })).toBeInTheDocument();
    const toggle = await screen.findByRole('button', { name: 'Show as table' });
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(toggle);
    expect(screen.getByRole('button', { name: 'Hide table' })).toHaveAttribute('aria-expanded', 'true');
    const table = screen.getByRole('table', { name: 'Data of the chart: Findings by status' });
    expect(table).toHaveTextContent('Status');
    expect(table).toHaveTextContent('Closed');
    expect(screen.getByRole('rowheader', { name: 'Open' })).toBeInTheDocument();
  });

  it('renders the RTL option in Arabic, shows the table and drills down from a row', async () => {
    runtime.fail = false;
    runtime.options = [];
    const onClick = vi.fn();
    await renderWithProviders(<Chart {...spec} label="الملاحظات" onClick={onClick} tableOpen />, {
      language: 'ar',
    });
    expect(screen.getByRole('button', { name: 'إخفاء الجدول' })).toBeInTheDocument();
    await vi.waitFor(() => expect(runtime.options.length).toBeGreaterThan(0));
    const rendered = runtime.options.at(-1) as Record<string, Record<string, unknown>>;
    expect(rendered.xAxis?.inverse).toBe(true);
    expect(rendered.aria).toMatchObject({ enabled: true });
    await userEvent.click(screen.getByRole('button', { name: 'Closed' }));
    expect(onClick).toHaveBeenCalledWith(expect.objectContaining({ key: 'closed', dataIndex: 1 }));
  });

  it('falls back to the table when the chart cannot render', async () => {
    runtime.fail = true;
    await renderWithProviders(<Chart {...spec} label="Findings" />);
    expect(await screen.findByText(/could not be displayed/)).toBeInTheDocument();
    expect(screen.getByRole('table')).toHaveTextContent('Open');
  });
});
