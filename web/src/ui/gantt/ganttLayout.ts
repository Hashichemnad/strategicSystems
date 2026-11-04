/**
 * Pure layout of the in-house read-only Gantt (docs/design/portfolio.md §36, ADR 0096): scale, quarter bands over month
 * ticks, per-row bars, today line and finish-to-start dependency arrows. No DOM, no React — unit-tested in both directions.
 *
 * Dates are calendar dates (`YYYY-MM-DD`) handled as whole UTC days. A bar covers its end day (inclusive), so a
 * one-day item is one day wide. In RTL the time axis is mirrored (x = width − x) and the label column sits on the right.
 */

/** Structural row shape (the portfolio API's `GanttRowDto`). */
export interface GanttRow {
  id: string;
  kind: string;
  parentId: string | null;
  code: string;
  titleAr: string | null;
  titleEn: string | null;
  plannedStart: string | null;
  plannedEnd: string | null;
  actualStart: string | null;
  actualEnd: string | null;
  baselineStart: string | null;
  baselineEnd: string | null;
  weight: number;
  /** Ratio 0–1. */
  achieved: number;
  status: string;
  delayed: boolean;
  highLevel: boolean;
  rag?: string | null;
}

export interface GanttDependency {
  from: string;
  to: string;
}

export type GanttZoom = 'month' | 'quarter';

export interface GanttLayoutInput {
  rows: readonly GanttRow[];
  dependencies?: readonly GanttDependency[];
  start?: string | null;
  end?: string | null;
  today: string;
  /** Total width in px (label column + time axis). */
  width: number;
  rtl: boolean;
  collapsed?: ReadonlySet<string>;
  labelWidth?: number;
  rowHeight?: number;
  headerHeight?: number;
}

export interface Span {
  x: number;
  width: number;
}

export interface BandLayout extends Span {
  /** First day of the band (`YYYY-MM-DD`). */
  start: string;
  year: number;
  /** 1–4 for quarters, 1–12 for months. */
  index: number;
}

export interface RowLayout {
  row: GanttRow;
  index: number;
  depth: number;
  /** Top of the row. */
  y: number;
  /** Vertical middle of the row. */
  cy: number;
  hasChildren: boolean;
  collapsed: boolean;
  /** Where the label text starts (text-anchor "start" in the row's direction). */
  labelX: number;
  planned: Span | null;
  achievedFill: Span | null;
  actual: Span | null;
  baseline: Span | null;
  diamond: { cx: number; cy: number; size: number } | null;
  /** End-side x of the planned bar / diamond (where a dependency leaves). */
  finishX: number | null;
  /** Start-side x of the planned bar / diamond (where a dependency arrives). */
  startX: number | null;
}

export interface DependencyLayout {
  from: string;
  to: string;
  d: string;
  x1: number;
  y1: number;
  x2: number;
  y2: number;
  /** Arrow head polygon points (tip at x2, y2). */
  head: string;
}

export interface GanttLayout {
  width: number;
  height: number;
  rtl: boolean;
  labelWidth: number;
  rowHeight: number;
  headerHeight: number;
  /** The time axis area [x, x + width]. */
  axis: Span;
  /** The label column area. */
  labels: Span;
  start: string;
  end: string;
  /** Pixels per day. */
  dayWidth: number;
  quarters: BandLayout[];
  months: BandLayout[];
  todayX: number | null;
  rows: RowLayout[];
  dependencies: DependencyLayout[];
  /** Maps a date to x (start of that day). */
  x: (date: string) => number;
}

const DAY_MS = 86_400_000;

/** Whole UTC day number of a `YYYY-MM-DD` (or ISO date-time) string; NaN when invalid. */
export function dayOf(date: string): number {
  const [y, m, d] = date.slice(0, 10).split('-').map(Number);
  if (!y || !m || !d) return Number.NaN;
  return Math.floor(Date.UTC(y, m - 1, d) / DAY_MS);
}

export function dateOf(day: number): string {
  return new Date(day * DAY_MS).toISOString().slice(0, 10);
}

function monthStart(day: number): number {
  const date = new Date(day * DAY_MS);
  return Math.floor(Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), 1) / DAY_MS);
}

function addMonths(day: number, months: number): number {
  const date = new Date(day * DAY_MS);
  return Math.floor(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + months, 1) / DAY_MS);
}

function valid(value: string | null | undefined): value is string {
  return typeof value === 'string' && !Number.isNaN(dayOf(value));
}

/** Rows in display order (each parent followed by its children, input order kept) with their depth. */
export function orderRows(
  rows: readonly GanttRow[],
): { row: GanttRow; depth: number; hasChildren: boolean }[] {
  const ids = new Set(rows.map((r) => r.id));
  const children = new Map<string, GanttRow[]>();
  const roots: GanttRow[] = [];
  for (const row of rows) {
    if (row.parentId && ids.has(row.parentId) && row.parentId !== row.id) {
      const list = children.get(row.parentId) ?? [];
      list.push(row);
      children.set(row.parentId, list);
    } else roots.push(row);
  }
  const out: { row: GanttRow; depth: number; hasChildren: boolean }[] = [];
  const seen = new Set<string>();
  const walk = (row: GanttRow, depth: number) => {
    if (seen.has(row.id)) return;
    seen.add(row.id);
    const kids = children.get(row.id) ?? [];
    out.push({ row, depth, hasChildren: kids.length > 0 });
    for (const kid of kids) walk(kid, depth + 1);
  };
  for (const root of roots) walk(root, 0);
  // Rows caught in a parent cycle are still shown, at the top level.
  for (const row of rows) walk(row, 0);
  return out;
}

/** The date range shown: the given start / end, else the rows' extent (with today), snapped to whole months. */
export function dateRange(
  rows: readonly GanttRow[],
  start: string | null | undefined,
  end: string | null | undefined,
  today: string,
): { start: number; end: number } {
  const days: number[] = [];
  for (const r of rows)
    for (const v of [
      r.plannedStart,
      r.plannedEnd,
      r.actualStart,
      r.actualEnd,
      r.baselineStart,
      r.baselineEnd,
    ])
      if (valid(v)) days.push(dayOf(v));
  const todayDay = dayOf(today);
  let first = valid(start) ? dayOf(start) : days.length ? Math.min(...days) : todayDay;
  let last = valid(end) ? dayOf(end) : days.length ? Math.max(...days) : todayDay;
  if (!valid(start) && !Number.isNaN(todayDay)) first = Math.min(first, todayDay);
  if (!valid(end) && !Number.isNaN(todayDay)) last = Math.max(last, todayDay);
  if (Number.isNaN(first)) first = Math.floor(Date.now() / DAY_MS);
  if (Number.isNaN(last) || last < first) last = first;
  // Snap to whole months; the end is exclusive (first day of the following month).
  return { start: monthStart(first), end: addMonths(last, 1) };
}

/** Computes everything the SVG draws. */
export function layoutGantt(input: GanttLayoutInput): GanttLayout {
  const labelWidth = input.labelWidth ?? 240;
  const rowHeight = input.rowHeight ?? 32;
  const headerHeight = input.headerHeight ?? 44;
  const width = Math.max(input.width, labelWidth + 120);
  const rtl = input.rtl;
  const collapsed = input.collapsed ?? new Set<string>();
  const range = dateRange(input.rows, input.start, input.end, input.today);
  const days = Math.max(range.end - range.start, 1);
  const axisWidth = width - labelWidth;
  const dayWidth = axisWidth / days;

  // LTR: labels [0, labelWidth], axis [labelWidth, width]. RTL mirrors: axis [0, width − labelWidth], labels on the right.
  const ltrX = (day: number) => labelWidth + (day - range.start) * dayWidth;
  const xOfDay = (day: number) => (rtl ? width - ltrX(day) : ltrX(day));
  const x = (date: string) => xOfDay(dayOf(date));
  /** Span between two day boundaries, as a left-anchored rect in either direction. */
  const span = (fromDay: number, toDay: number): Span => {
    const a = xOfDay(fromDay);
    const b = xOfDay(toDay);
    return { x: Math.min(a, b), width: Math.abs(b - a) };
  };
  const clampDay = (day: number) => Math.min(Math.max(day, range.start), range.end);

  const quarters: BandLayout[] = [];
  const months: BandLayout[] = [];
  for (let m = range.start; m < range.end; m = addMonths(m, 1)) {
    const next = addMonths(m, 1);
    const date = new Date(m * DAY_MS);
    const month = date.getUTCMonth() + 1;
    months.push({
      ...span(m, Math.min(next, range.end)),
      start: dateOf(m),
      year: date.getUTCFullYear(),
      index: month,
    });
    const quarterStartMonth = month - ((month - 1) % 3);
    const quarterStart = Math.floor(Date.UTC(date.getUTCFullYear(), quarterStartMonth - 1, 1) / DAY_MS);
    const quarterEnd = addMonths(quarterStart, 3);
    if (m === range.start || m === quarterStart) {
      quarters.push({
        ...span(Math.max(quarterStart, range.start), Math.min(quarterEnd, range.end)),
        start: dateOf(Math.max(quarterStart, range.start)),
        year: date.getUTCFullYear(),
        index: (quarterStartMonth - 1) / 3 + 1,
      });
    }
  }

  const todayDay = dayOf(input.today);
  const todayX =
    !Number.isNaN(todayDay) && todayDay >= range.start && todayDay <= range.end ? xOfDay(todayDay) : null;

  // Visible rows: descendants of a collapsed row are hidden.
  const ordered = orderRows(input.rows);
  const parentOf = new Map(input.rows.map((r) => [r.id, r.parentId]));
  const hidden = (row: GanttRow) => {
    let parent = row.parentId;
    const guard = new Set<string>();
    while (parent && parentOf.has(parent) && !guard.has(parent)) {
      if (collapsed.has(parent)) return true;
      guard.add(parent);
      parent = parentOf.get(parent) ?? null;
    }
    return false;
  };

  const rows: RowLayout[] = [];
  for (const { row, depth, hasChildren } of ordered) {
    if (hidden(row)) continue;
    const index = rows.length;
    const y = headerHeight + index * rowHeight;
    const cy = y + rowHeight / 2;
    const indent = 12 + depth * 16 + (hasChildren ? 0 : 16);
    const labelX = rtl ? width - indent : indent;

    let planned: Span | null = null;
    let achievedFill: Span | null = null;
    let diamond: RowLayout['diamond'] = null;
    let finishX: number | null = null;
    let startX: number | null = null;
    const ps = valid(row.plannedStart) ? dayOf(row.plannedStart) : null;
    const pe = valid(row.plannedEnd) ? dayOf(row.plannedEnd) : null;
    if (pe !== null && (ps === null || ps === pe)) {
      // Zero-length item: a diamond centred on its day.
      const cx = xOfDay(clampDay(pe)) + (rtl ? -dayWidth / 2 : dayWidth / 2);
      const size = Math.min(rowHeight * 0.32, 10);
      diamond = { cx, cy, size };
      finishX = cx + (rtl ? -size : size);
      startX = cx - (rtl ? -size : size);
    } else if (ps !== null && pe !== null) {
      const from = clampDay(Math.min(ps, pe));
      const to = clampDay(Math.max(ps, pe) + 1);
      planned = span(from, to);
      const ratio = Math.min(Math.max(row.achieved || 0, 0), 1);
      const fillWidth = planned.width * ratio;
      achievedFill = {
        x: rtl ? planned.x + planned.width - fillWidth : planned.x,
        width: fillWidth,
      };
      startX = xOfDay(from);
      finishX = xOfDay(to);
    }

    let actual: Span | null = null;
    if (valid(row.actualStart)) {
      const as = dayOf(row.actualStart);
      // Not finished yet: the actual bar runs to today.
      const ae = valid(row.actualEnd) ? dayOf(row.actualEnd) + 1 : todayDay;
      if (!Number.isNaN(ae)) {
        const from = clampDay(as);
        const to = clampDay(Math.max(ae, as));
        actual = span(from, to);
      }
    }

    let baseline: Span | null = null;
    if (valid(row.baselineStart) && valid(row.baselineEnd)) {
      const bs = dayOf(row.baselineStart);
      const be = dayOf(row.baselineEnd);
      baseline = span(clampDay(Math.min(bs, be)), clampDay(Math.max(bs, be) + 1));
    }

    rows.push({
      row,
      index,
      depth,
      y,
      cy,
      hasChildren,
      collapsed: collapsed.has(row.id),
      labelX,
      planned,
      achievedFill,
      actual,
      baseline,
      diamond,
      finishX,
      startX,
    });
  }

  const byId = new Map(rows.map((r) => [r.row.id, r]));
  const dir = rtl ? -1 : 1;
  const dependencies: DependencyLayout[] = [];
  for (const dep of input.dependencies ?? []) {
    const from = byId.get(dep.from);
    const to = byId.get(dep.to);
    if (!from || !to || from.finishX === null || to.startX === null) continue;
    const x1 = from.finishX;
    const y1 = from.cy;
    const x2 = to.startX;
    const y2 = to.cy;
    const elbow = x1 + dir * 8;
    const d = `M ${r2(x1)} ${r2(y1)} H ${r2(elbow)} V ${r2(y2)} H ${r2(x2)}`;
    const pointing = Math.sign(x2 - elbow) || dir;
    const head = `${r2(x2)},${r2(y2)} ${r2(x2 - pointing * 6)},${r2(y2 - 4)} ${r2(x2 - pointing * 6)},${r2(y2 + 4)}`;
    dependencies.push({ from: dep.from, to: dep.to, d, x1, y1, x2, y2, head });
  }

  return {
    width,
    height: headerHeight + rows.length * rowHeight + 8,
    rtl,
    labelWidth,
    rowHeight,
    headerHeight,
    axis: { x: rtl ? 0 : labelWidth, width: axisWidth },
    labels: { x: rtl ? width - labelWidth : 0, width: labelWidth },
    start: dateOf(range.start),
    end: dateOf(range.end),
    dayWidth,
    quarters,
    months,
    todayX,
    rows,
    dependencies,
    x,
  };
}

function r2(value: number): number {
  return Math.round(value * 100) / 100;
}

/** Width needed so months stay readable when zoomed to months (the container scrolls). */
export function preferredWidth(
  rows: readonly GanttRow[],
  start: string | null | undefined,
  end: string | null | undefined,
  today: string,
  zoom: GanttZoom,
  available: number,
  labelWidth = 240,
): number {
  if (zoom === 'quarter') return available;
  const range = dateRange(rows, start, end, today);
  const monthsCount = Math.round((range.end - range.start) / 30.44);
  return Math.max(available, labelWidth + monthsCount * 72);
}
