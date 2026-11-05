/**
 * Dashboard periods (docs/design/reporting.md §3, ADR 0148): whole months only. A period is a month, a quarter, a year or a custom
 * range of months, plus "compare with the previous period" (the window of the same length just before). Pure helpers; the URL keeps
 * the period so a dashboard link reopens the same view.
 */
export type PeriodKind = 'month' | 'quarter' | 'year' | 'custom';

export const periodKinds: readonly PeriodKind[] = ['month', 'quarter', 'year', 'custom'];

export interface PeriodValue {
  kind: PeriodKind;
  /** First month, 'YYYY-MM'. */
  from: string;
  /** Last month (inclusive), 'YYYY-MM'. */
  to: string;
  compare: boolean;
}

const monthPattern = /^(\d{4})-(0[1-9]|1[0-2])$/;

export function isMonth(value: unknown): value is string {
  return typeof value === 'string' && monthPattern.test(value);
}

function parts(month: string): [number, number] {
  const match = monthPattern.exec(month);
  if (!match) throw new Error(`Invalid month '${month}' (expected YYYY-MM).`);
  return [Number(match[1]), Number(match[2])];
}

function format(year: number, month: number): string {
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}`;
}

/** 'YYYY-MM' of a date (local calendar). */
export function monthOf(date: Date): string {
  return format(date.getFullYear(), date.getMonth() + 1);
}

/** Adds (or subtracts) whole months. */
export function addMonths(month: string, count: number): string {
  const [year, m] = parts(month);
  const index = year * 12 + (m - 1) + count;
  return format(Math.floor(index / 12), (((index % 12) + 12) % 12) + 1);
}

/** Number of months from `from` to `to`, both included (1 for the same month). */
export function monthCount(from: string, to: string): number {
  const [fy, fm] = parts(from);
  const [ty, tm] = parts(to);
  return ty * 12 + tm - (fy * 12 + fm) + 1;
}

/** Every month of a period, in order. */
export function monthsOf(period: Pick<PeriodValue, 'from' | 'to'>): string[] {
  const count = Math.max(0, monthCount(period.from, period.to));
  return Array.from({ length: count }, (_, i) => addMonths(period.from, i));
}

/** The first and last month of the period of `kind` containing `anchor` (custom: the anchor month alone). */
export function periodRange(kind: PeriodKind, anchor: string): { from: string; to: string } {
  const [year, month] = parts(anchor);
  switch (kind) {
    case 'month':
    case 'custom':
      return { from: anchor, to: anchor };
    case 'quarter': {
      const first = Math.floor((month - 1) / 3) * 3 + 1;
      return { from: format(year, first), to: format(year, first + 2) };
    }
    case 'year':
      return { from: format(year, 1), to: format(year, 12) };
  }
}

/** The period of `kind` containing today. */
export function currentPeriod(
  kind: PeriodKind = 'month',
  today: Date = new Date(),
  compare = false,
): PeriodValue {
  return { kind, ...periodRange(kind, monthOf(today)), compare };
}

/** The window of the same length just before the period (previous month / quarter / year / range). */
export function previousPeriod(period: PeriodValue): PeriodValue {
  const length = Math.max(1, monthCount(period.from, period.to));
  return { ...period, from: addMonths(period.from, -length), to: addMonths(period.from, -1) };
}

/** Moves a month / quarter / year period one step back (-1) or forward (+1); a custom range moves by its own length. */
export function shiftPeriod(period: PeriodValue, step: -1 | 1): PeriodValue {
  const length = Math.max(1, monthCount(period.from, period.to));
  return {
    ...period,
    from: addMonths(period.from, step * length),
    to: addMonths(period.to, step * length),
  };
}

/** Changes the kind, keeping the period that contains the current start month. */
export function changeKind(period: PeriodValue, kind: PeriodKind): PeriodValue {
  if (kind === 'custom') return { ...period, kind };
  return { ...period, kind, ...periodRange(kind, period.from) };
}

/** Puts `from` before `to` and snaps month / quarter / year periods to their calendar boundaries. */
export function normalizePeriod(period: PeriodValue): PeriodValue {
  const [from, to] =
    monthCount(period.from, period.to) >= 1 ? [period.from, period.to] : [period.to, period.from];
  if (period.kind === 'custom') return { ...period, from, to };
  return { ...period, ...periodRange(period.kind, from) };
}

/** URL search parameters of a period (`period`, `from`, `to`, `compare`). */
export function toSearch(period: PeriodValue): Record<string, string> {
  return {
    period: period.kind,
    from: period.from,
    to: period.to,
    ...(period.compare ? { compare: '1' } : {}),
  };
}

/** Reads a period back from URL search parameters; anything missing or invalid falls back (default: this month). */
export function fromSearch(
  search: Readonly<Record<string, unknown>>,
  fallback: PeriodValue = currentPeriod(),
): PeriodValue {
  const kind = periodKinds.find((k) => k === search.period) ?? fallback.kind;
  const compare = search.compare === '1' || search.compare === 1 || search.compare === true;
  const from = isMonth(search.from) ? search.from : undefined;
  const to = isMonth(search.to) ? search.to : undefined;
  if (!from)
    return {
      ...fallback,
      kind,
      ...(kind === fallback.kind ? {} : periodRange(kind, fallback.from)),
      compare,
    };
  return normalizePeriod({ kind, from, to: kind === 'custom' ? (to ?? from) : from, compare });
}
