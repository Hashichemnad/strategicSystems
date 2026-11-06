import type { ChartOption } from './types';

type Obj = Record<string, unknown>;

function isObj(value: unknown): value is Obj {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/** Applies `fn` to a component given as one object or an array of objects (ECharts accepts both). */
function mapComponent(value: unknown, fn: (item: Obj, index: number) => Obj): unknown {
  if (Array.isArray(value))
    return (value as unknown[]).map((item, index) => (isObj(item) ? fn(item, index) : item));
  return isObj(value) ? fn(value, 0) : value;
}

/** Swaps `left` and `right` (positions in px, % or keywords) so a box placed on the start side stays on the start side. */
export function swapSides(item: Obj): Obj {
  const next: Obj = { ...item };
  delete next.left;
  delete next.right;
  const flip = (v: unknown) => (v === 'left' ? 'right' : v === 'right' ? 'left' : v);
  if (item.left !== undefined) next.right = flip(item.left);
  if (item.right !== undefined) next.left = flip(item.right);
  return next;
}

const mirroredPosition: Record<string, string> = {
  left: 'right',
  right: 'left',
  insideLeft: 'insideRight',
  insideRight: 'insideLeft',
  insideTopLeft: 'insideTopRight',
  insideTopRight: 'insideTopLeft',
  insideBottomLeft: 'insideBottomRight',
  insideBottomRight: 'insideBottomLeft',
};

function mirrorLabel(label: unknown): unknown {
  if (!isObj(label) || typeof label.position !== 'string') return label;
  const position = mirroredPosition[label.position];
  return position ? { ...label, position } : label;
}

/**
 * Mirrors a left-to-right option for right-to-left pages (CLAUDE.md §10, ADR 0148), so callers never write direction logic:
 * - every x axis is inverted (time and categories run from the right; horizontal bars grow from the right),
 * - y axes move to the other side (the first one defaults to the left in ECharts, so it goes to the right),
 * - grid, legend, visualMap and title margins swap sides, a legend / visualMap without a side is put on the start (right) side,
 * - series labels placed left / right swap.
 * Pure: the input is not modified.
 */
export function applyDirection(option: ChartOption, rtl: boolean): ChartOption {
  if (!rtl) return option;
  const o = option as Obj;
  const next: Obj = { ...o };
  if (o.xAxis !== undefined)
    next.xAxis = mapComponent(o.xAxis, (axis) => ({ ...axis, inverse: !(axis.inverse === true) }));
  if (o.yAxis !== undefined)
    next.yAxis = mapComponent(o.yAxis, (axis, index) => {
      const position = typeof axis.position === 'string' ? axis.position : index === 0 ? 'left' : 'right';
      return { ...axis, position: position === 'left' ? 'right' : position === 'right' ? 'left' : position };
    });
  if (o.grid !== undefined) next.grid = mapComponent(o.grid, swapSides);
  if (o.title !== undefined) next.title = mapComponent(o.title, swapSides);
  const startSide = (item: Obj): Obj => {
    const swapped = swapSides(item);
    if (item.left === undefined && item.right === undefined) swapped.right = 0;
    return { ...swapped, align: item.align ?? 'right' };
  };
  if (o.legend !== undefined) next.legend = mapComponent(o.legend, startSide);
  if (o.visualMap !== undefined) next.visualMap = mapComponent(o.visualMap, startSide);
  if (o.series !== undefined)
    next.series = mapComponent(o.series, (series) =>
      series.label === undefined ? series : { ...series, label: mirrorLabel(series.label) },
    );
  return next;
}

/** Adds the ECharts `aria` description (read by assistive technology inside the chart) to an option. */
export function withAria(option: ChartOption, description: string): ChartOption {
  return { ...option, aria: { enabled: true, label: { enabled: true, description } } };
}

/** What {@link Chart} renders: aria description + direction. */
export function prepareOption(
  option: ChartOption,
  { rtl, description }: { rtl: boolean; description: string },
) {
  return applyDirection(withAria(option, description), rtl);
}
