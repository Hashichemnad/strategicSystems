import type { ChartTokens, ChartTone } from './theme';
import type { ChartCell, ChartOption, ChartSpec, ChartTable } from './types';

/**
 * Typed option builders (ADR 0148). They write left-to-right options with theme-aware colours ({@link token}); the {@link Chart}
 * wrapper mirrors them for RTL and resolves the colours, so features never deal with direction or the palette. Each returns the
 * option and the same data as a table (the accessible "Show as table" equivalent).
 */

/** A design-token colour for options (resolved by the Chart for the current light / dark theme). */
export function token(tone: ChartTone): string {
  return `token:${tone}`;
}

/** Replaces `token:<tone>` colours in an option with the theme's colours. Pure; functions (formatters) are kept as they are. */
export function resolveTokenColors<T>(value: T, tokens: ChartTokens): T {
  const walk = (v: unknown): unknown => {
    if (typeof v === 'string') {
      const match = /^token:(primary|info|success|warning|danger|neutral)$/.exec(v);
      if (!match) return v;
      const tone = match[1] as ChartTone;
      return tone === 'neutral' ? tokens.mutedForeground : tokens[tone];
    }
    if (Array.isArray(v)) return v.map(walk);
    if (v && typeof v === 'object' && Object.getPrototypeOf(v) === Object.prototype) {
      const out: Record<string, unknown> = {};
      for (const [k, item] of Object.entries(v)) out[k] = walk(item);
      return out;
    }
    return v;
  };
  return walk(value) as T;
}

export type Format = (value: number) => string;
const plain: Format = (value) => String(value);

export interface Category {
  key: string;
  label: string;
}

export interface BarSeries {
  name: string;
  values: readonly (number | null)[];
  /** A CSS colour or {@link token}; defaults to the palette. */
  color?: string;
}

export interface BarInput {
  categories: readonly Category[];
  series: readonly BarSeries[];
  /** Vertical columns (default) or horizontal bars (long labels, rankings). */
  orientation?: 'vertical' | 'horizontal';
  stacked?: boolean;
  /** Keep the N largest categories (by total) and add the rest up as "others" (`othersLabel`, key `__others`). */
  topN?: number;
  othersLabel?: string;
  /** Value labels on the bars (default: on for a single series). */
  showValues?: boolean;
  /** Per-category colours for a single series (e.g. by RAG or band). */
  categoryColors?: readonly (string | undefined)[];
  format?: Format;
  /** Integer axis (counts). */
  integer?: boolean;
  valueName?: string;
  /** Header of the first table column. */
  categoryHeader?: string;
  markLine?: { value: number; label?: string };
  /** Hides the legend (default: shown for more than one series). */
  legend?: boolean;
}

/** Top-N + "others" over categories and their series values (pure; exported for tests). */
export function topN(
  categories: readonly Category[],
  series: readonly BarSeries[],
  n: number,
  othersLabel: string,
): { categories: Category[]; series: BarSeries[]; kept: number[] } {
  if (categories.length <= n)
    return { categories: [...categories], series: [...series], kept: categories.map((_, i) => i) };
  const totals = categories.map((_, i) => series.reduce((sum, s) => sum + (s.values[i] ?? 0), 0));
  const order = categories.map((_, i) => i).sort((a, b) => (totals[b] ?? 0) - (totals[a] ?? 0));
  const kept = order.slice(0, n);
  const rest = order.slice(n);
  return {
    categories: [...kept.map((i) => categories[i] as Category), { key: '__others', label: othersLabel }],
    series: series.map((s) => ({
      ...s,
      values: [...kept.map((i) => s.values[i] ?? null), rest.reduce((sum, i) => sum + (s.values[i] ?? 0), 0)],
    })),
    kept,
  };
}

function tableOf(
  categoryHeader: string,
  categories: readonly Category[],
  series: readonly { name: string; values: readonly (number | null)[] }[],
  format: Format,
): ChartTable {
  return {
    columns: [categoryHeader, ...series.map((s) => s.name)],
    rows: categories.map((c, i) => [
      c.label,
      ...series.map((s): ChartCell => {
        const v = s.values[i];
        return v == null ? null : format(v);
      }),
    ]),
    rowKeys: categories.map((c) => c.key),
  };
}

const legendTop = { top: 0, left: 0, type: 'scroll' } as const;

export function barOption(input: BarInput): ChartSpec {
  const format = input.format ?? plain;
  const horizontal = input.orientation === 'horizontal';
  const { categories, series, kept } =
    input.topN !== undefined
      ? topN(input.categories, input.series, input.topN, input.othersLabel ?? 'Others')
      : {
          categories: [...input.categories],
          series: [...input.series],
          kept: input.categories.map((_, i) => i),
        };
  const categoryColors = kept.map((i) => input.categoryColors?.[i]);
  const showLegend = input.legend ?? series.length > 1;
  const showValues = input.showValues ?? series.length === 1;
  const categoryAxis = {
    type: 'category' as const,
    data: categories.map((c) => c.label),
    axisLabel: horizontal
      ? { width: 140, overflow: 'truncate' as const }
      : { interval: 0, width: 80, overflow: 'break' as const, hideOverlap: true },
    // Horizontal bars list the first category at the top.
    ...(horizontal ? { inverse: true } : {}),
  };
  const valueAxis = {
    type: 'value' as const,
    ...(input.integer ? { minInterval: 1 } : {}),
    ...(input.valueName ? { name: input.valueName, nameLocation: 'middle' as const, nameGap: 32 } : {}),
    axisLabel: { formatter: (v: number) => format(v) },
  };
  const option = {
    grid: { top: showLegend ? 36 : 16, bottom: 8, left: 8, right: horizontal ? 40 : 16, containLabel: true },
    ...(showLegend ? { legend: { ...legendTop } } : {}),
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'shadow' },
      valueFormatter: (v) => (typeof v === 'number' ? format(v) : String(v ?? '—')),
    },
    xAxis: horizontal ? valueAxis : categoryAxis,
    yAxis: horizontal ? categoryAxis : valueAxis,
    series: series.map((s, si) => ({
      name: s.name,
      type: 'bar' as const,
      ...(input.stacked ? { stack: 'total' } : {}),
      barMaxWidth: 40,
      ...(s.color ? { itemStyle: { color: s.color } } : {}),
      data: s.values.map((v, i) => {
        const color = series.length === 1 ? categoryColors[i] : undefined;
        return { value: v, key: categories[i]?.key, ...(color ? { itemStyle: { color } } : {}) };
      }),
      ...(showValues
        ? {
            label: {
              show: true,
              position: input.stacked
                ? ('inside' as const)
                : horizontal
                  ? ('right' as const)
                  : ('top' as const),
              formatter: (p: { value: unknown }) => {
                const raw = (p.value as { value?: unknown } | number | null) ?? null;
                const v = typeof raw === 'number' ? raw : null;
                return v == null || (input.stacked && v === 0) ? '' : format(v);
              },
            },
          }
        : {}),
      ...(input.markLine && si === 0
        ? {
            markLine: {
              symbol: 'none',
              silent: true,
              data: [horizontal ? { xAxis: input.markLine.value } : { yAxis: input.markLine.value }],
              label: { formatter: () => input.markLine?.label ?? format(input.markLine?.value ?? 0) },
            },
          }
        : {}),
    })),
  } as ChartOption;
  return { option, table: tableOf(input.categoryHeader ?? '', categories, series, format) };
}

export interface LineSeries {
  name: string;
  values: readonly (number | null)[];
  color?: string;
  /** Filled area (stacked when `stack` is set). */
  area?: boolean;
  dashed?: boolean;
  stack?: string;
  step?: boolean;
  /** No point symbols (dense series). */
  noSymbol?: boolean;
}

export interface LineInput {
  /** Points on the x axis (months, dates, sprints) in time order. */
  categories: readonly Category[];
  series: readonly LineSeries[];
  /** The same series for the previous period, drawn dashed in the same colour (period comparison). */
  previous?: readonly { name: string; values: readonly (number | null)[] }[];
  format?: Format;
  integer?: boolean;
  valueName?: string;
  categoryHeader?: string;
  /** Start the line at the axis edge (cumulative flow); default true for areas. */
  edgeToEdge?: boolean;
  markLine?: { value: number; label?: string };
  min?: number;
  max?: number;
}

export function lineOption(input: LineInput): ChartSpec {
  const format = input.format ?? plain;
  const all: LineSeries[] = [
    ...input.series,
    ...(input.previous ?? []).map((p, i) => {
      const color = input.series[i]?.color;
      return { name: p.name, values: p.values, dashed: true, ...(color ? { color } : {}) };
    }),
  ];
  const showLegend = all.length > 1;
  const option = {
    grid: { top: showLegend ? 36 : 16, bottom: 8, left: 8, right: 16, containLabel: true },
    ...(showLegend ? { legend: { ...legendTop } } : {}),
    tooltip: {
      trigger: 'axis',
      valueFormatter: (v: unknown) => (typeof v === 'number' ? format(v) : typeof v === 'string' ? v : '—'),
    },
    xAxis: {
      type: 'category',
      boundaryGap: !(input.edgeToEdge ?? input.series.some((s) => s.area)),
      data: input.categories.map((c) => c.label),
      axisLabel: { hideOverlap: true },
    },
    yAxis: {
      type: 'value',
      ...(input.integer ? { minInterval: 1 } : {}),
      ...(input.min !== undefined ? { min: input.min } : {}),
      ...(input.max !== undefined ? { max: input.max } : {}),
      ...(input.valueName ? { name: input.valueName } : {}),
      axisLabel: { formatter: (v: number) => format(v) },
    },
    series: all.map((s, si) => {
      const color = s.color;
      return {
        name: s.name,
        type: 'line',
        connectNulls: false,
        ...(s.stack ? { stack: s.stack } : {}),
        ...(s.step ? { step: 'end' } : {}),
        ...(s.noSymbol ? { symbol: 'none' } : { symbolSize: 6 }),
        lineStyle: { width: 2, ...(s.dashed ? { type: 'dashed' } : {}), ...(color ? { color } : {}) },
        ...(color ? { itemStyle: { color } } : {}),
        ...(s.area ? { areaStyle: { opacity: s.stack ? 0.85 : 0.2, ...(color ? { color } : {}) } } : {}),
        data: s.values.map((v, i) => ({ value: v, key: input.categories[i]?.key })),
        ...(input.markLine && si === 0
          ? {
              markLine: {
                symbol: 'none',
                silent: true,
                data: [{ yAxis: input.markLine.value }],
                label: { formatter: () => input.markLine?.label ?? format(input.markLine?.value ?? 0) },
              },
            }
          : {}),
      };
    }),
  } as ChartOption;
  return { option, table: tableOf(input.categoryHeader ?? '', input.categories, all, format) };
}

export interface BubblePoint {
  key: string;
  label: string;
  x: number;
  y: number;
  /** Bubble size measure (e.g. budget); null draws the smallest bubble. */
  size: number | null;
}

export interface BubbleInput {
  groups: readonly { name: string; color?: string; points: readonly BubblePoint[] }[];
  x: { name: string; min?: number; max?: number; format?: Format };
  y: { name: string; min?: number; max?: number; interval?: number; format?: Format };
  size: { name: string; format?: Format; hidden?: string };
  /** Smallest and largest bubble diameter in px. */
  sizeRange?: [number, number];
  /** Header of the item column in the table; group column header. */
  itemHeader?: string;
  groupHeader?: string;
}

export function bubbleOption(input: BubbleInput): ChartSpec {
  const [minSize, maxSize] = input.sizeRange ?? [12, 50];
  const fx = input.x.format ?? plain;
  const fy = input.y.format ?? plain;
  const fs = input.size.format ?? plain;
  const largest = Math.max(1, ...input.groups.flatMap((g) => g.points.map((p) => p.size ?? 0)));
  const sizeOf = (size: number | null) =>
    size === null ? minSize : minSize + Math.sqrt(Math.max(0, size) / largest) * (maxSize - minSize);
  const option = {
    grid: { top: 40, bottom: 40, left: 16, right: 24, containLabel: true },
    legend: { ...legendTop },
    tooltip: {
      trigger: 'item',
      formatter: (params: unknown) => {
        const p = params as { name: string; seriesName: string; data: { point: BubblePoint } };
        const point = p.data.point;
        const esc = (s: string) => s.replace(/[&<>"]/g, (c) => `&#${c.charCodeAt(0)};`);
        return [
          esc(p.name),
          `${esc(input.x.name)}: ${fx(point.x)}`,
          `${esc(input.y.name)}: ${fy(point.y)}`,
          `${esc(input.size.name)}: ${point.size === null ? esc(input.size.hidden ?? '—') : fs(point.size)}`,
          esc(p.seriesName),
        ].join('<br/>');
      },
    },
    xAxis: {
      type: 'value',
      name: input.x.name,
      nameLocation: 'middle',
      nameGap: 28,
      ...(input.x.min !== undefined ? { min: input.x.min } : {}),
      ...(input.x.max !== undefined ? { max: input.x.max } : {}),
      axisLabel: { formatter: (v: number) => fx(v) },
    },
    yAxis: {
      type: 'value',
      name: input.y.name,
      ...(input.y.min !== undefined ? { min: input.y.min } : {}),
      ...(input.y.max !== undefined ? { max: input.y.max } : {}),
      ...(input.y.interval !== undefined ? { interval: input.y.interval } : {}),
      axisLabel: { formatter: (v: number) => fy(v) },
    },
    series: input.groups.map((g) => ({
      name: g.name,
      type: 'scatter',
      itemStyle: { opacity: 0.8, ...(g.color ? { color: g.color } : {}) },
      data: g.points.map((p) => ({
        name: p.label,
        key: p.key,
        point: p,
        value: [p.x, p.y, p.size ?? 0],
        symbolSize: sizeOf(p.size),
      })),
    })),
  } as ChartOption;
  const table: ChartTable = {
    columns: [input.itemHeader ?? '', input.x.name, input.y.name, input.size.name, input.groupHeader ?? ''],
    rows: input.groups.flatMap((g) =>
      g.points.map((p) => [
        p.label,
        fx(p.x),
        fy(p.y),
        p.size === null ? (input.size.hidden ?? null) : fs(p.size),
        g.name,
      ]),
    ),
    rowKeys: input.groups.flatMap((g) => g.points.map((p) => p.key)),
  };
  return { option, table };
}

export interface GaugeInput {
  name: string;
  value: number | null;
  min?: number;
  max?: number;
  format?: Format;
  /** Colour bands as [upper bound, colour] (ascending), e.g. RAG thresholds. */
  bands?: readonly (readonly [number, string])[];
  color?: string;
}

export function gaugeOption(input: GaugeInput): ChartSpec {
  const min = input.min ?? 0;
  const max = input.max ?? 1;
  const format = input.format ?? plain;
  const span = max - min || 1;
  const bands = input.bands?.map(([to, color]) => [Math.min(1, Math.max(0, (to - min) / span)), color]);
  const option = {
    series: [
      {
        type: 'gauge',
        min,
        max,
        startAngle: 210,
        endAngle: -30,
        radius: '95%',
        center: ['50%', '60%'],
        progress: { show: !bands, width: 14, ...(input.color ? { itemStyle: { color: input.color } } : {}) },
        pointer: { show: Boolean(bands), length: '60%', width: 5 },
        axisLine: { lineStyle: { width: 14, ...(bands ? { color: bands } : {}) } },
        axisTick: { show: false },
        splitLine: { show: false },
        axisLabel: { show: false },
        anchor: { show: false },
        title: { show: false },
        detail: {
          valueAnimation: false,
          offsetCenter: [0, '10%'],
          fontSize: 24,
          fontWeight: 600,
          formatter: (v: number) => (input.value === null ? '—' : format(v)),
        },
        data: [{ value: input.value ?? min, name: input.name }],
      },
    ],
  } as ChartOption;
  return {
    option,
    table: { columns: [input.name], rows: [[input.value === null ? null : format(input.value)]] },
  };
}

export interface FunnelInput {
  steps: readonly { key: string; label: string; value: number; color?: string }[];
  format?: Format;
  categoryHeader?: string;
  valueHeader?: string;
}

export function funnelOption(input: FunnelInput): ChartSpec {
  const format = input.format ?? plain;
  const max = Math.max(1, ...input.steps.map((s) => s.value));
  const option = {
    tooltip: {
      trigger: 'item',
      formatter: (params: unknown) => {
        const p = params as { name: string; value: number };
        return `${p.name.replace(/[&<>"]/g, (c) => `&#${c.charCodeAt(0)};`)}: ${format(p.value)}`;
      },
    },
    series: [
      {
        type: 'funnel',
        sort: 'none',
        min: 0,
        max,
        top: 8,
        bottom: 8,
        left: '10%',
        width: '80%',
        gap: 2,
        minSize: '8%',
        label: {
          show: true,
          position: 'inside',
          formatter: (p: { name: string; value: number }) => `${p.name}: ${format(p.value)}`,
        },
        data: input.steps.map((s) => ({
          name: s.label,
          key: s.key,
          value: s.value,
          ...(s.color ? { itemStyle: { color: s.color } } : {}),
        })),
      },
    ],
  } as ChartOption;
  return {
    option,
    table: {
      columns: [input.categoryHeader ?? '', input.valueHeader ?? ''],
      rows: input.steps.map((s) => [s.label, format(s.value)]),
      rowKeys: input.steps.map((s) => s.key),
    },
  };
}

export interface PieInput {
  slices: readonly { key: string; label: string; value: number; color?: string }[];
  /** A ring (donut) instead of a full pie. */
  donut?: boolean;
  format?: Format;
  categoryHeader?: string;
  valueHeader?: string;
}

/** Pie / donut (report builder, ADR 0182): legend on the start side, share in the tooltip, the same data as a table. */
export function pieOption(input: PieInput): ChartSpec {
  const format = input.format ?? plain;
  const total = input.slices.reduce((sum, s) => sum + Math.max(0, s.value), 0);
  const option = {
    tooltip: {
      trigger: 'item',
      formatter: (params: unknown) => {
        const p = params as { name: string; value: number; percent: number };
        return `${p.name.replace(/[&<>"]/g, (c) => `&#${c.charCodeAt(0)};`)}: ${format(p.value)} (${p.percent}%)`;
      },
    },
    legend: { type: 'scroll', orient: 'vertical', left: 0, top: 'middle' },
    series: [
      {
        type: 'pie',
        radius: input.donut ? ['45%', '72%'] : '72%',
        center: ['62%', '50%'],
        avoidLabelOverlap: true,
        label: { show: false },
        data: input.slices.map((s) => ({
          name: s.label,
          key: s.key,
          value: s.value,
          ...(s.color ? { itemStyle: { color: s.color } } : {}),
        })),
      },
    ],
  } as ChartOption;
  return {
    option,
    table: {
      columns: [input.categoryHeader ?? '', input.valueHeader ?? '', '%'],
      rows: input.slices.map((s) => [
        s.label,
        format(s.value),
        total > 0 ? Math.round((Math.max(0, s.value) / total) * 1000) / 10 : 0,
      ]),
      rowKeys: input.slices.map((s) => s.key),
    },
  };
}
