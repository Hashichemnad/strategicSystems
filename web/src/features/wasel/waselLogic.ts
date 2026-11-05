import type { DiffEntry, ProposalDiffEntry, ProposalItem } from './waselApi';

/** Achievement and weights travel as ratios of the whole project (1 = 100 %); the UI shows percent points. */
export function ratioToPercent(ratio: number | null | undefined): string {
  if (ratio === null || ratio === undefined) return '';
  return String(Math.round(ratio * 10000) / 100);
}

/** Percent typed by the user → ratio rounded to 4 decimals (what the server accepts). */
export function percentToRatio(percent: string): number {
  const value = Number(percent.replace(',', '.'));
  if (!Number.isFinite(value)) return 0;
  return Math.round(value * 100) / 10000;
}

export function formatPercent(ratio: number | null | undefined, language: string): string {
  if (ratio === null || ratio === undefined) return '—';
  return new Intl.NumberFormat(language === 'ar' ? 'ar-AE' : 'en-GB', {
    style: 'percent',
    maximumFractionDigits: 2,
  }).format(ratio);
}

/** A diff value as the vendor reads it: the achievement field is a ratio, shown as percent; empty is a dash. */
export function diffValue(field: string, value: string | null, language: string): string {
  if (value === null || value === '') return '—';
  if (field === 'achieve' || field === 'weight') {
    const number = Number(value);
    return Number.isFinite(number) ? formatPercent(number, language) : value;
  }
  return value;
}

export type DiffKind = 'changed' | 'added' | 'removed';

export function proposalDiffKind(entry: ProposalDiffEntry): DiffKind {
  if (entry.field === 'added') return 'added';
  if (entry.field === 'removed') return 'removed';
  return 'changed';
}

export function hasPmChanges(diff: readonly DiffEntry[] | readonly ProposalDiffEntry[]): boolean {
  return diff.length > 0;
}

/** Next free key for a new proposal row ("m3", "p2", …). */
export function nextKey(items: readonly ProposalItem[], prefix: string): string {
  let n = 1;
  const keys = new Set(items.map((i) => i.key));
  while (keys.has(`${prefix}${n}`)) n += 1;
  return `${prefix}${n}`;
}

/** Sum of the weighted rows (milestones in waterfall, epics in agile) as a ratio. */
export function proposedWeight(items: readonly ProposalItem[], kind: string): number {
  const weighted = kind === 'agile' ? 'epic' : 'milestone';
  return items.filter((i) => i.type === weighted).reduce((sum, i) => sum + (i.weight ?? 0), 0);
}

export function parseTasks(text: string): string[] {
  return text
    .split(',')
    .map((t) => t.trim())
    .filter(Boolean);
}

export const submissionTones: Record<string, 'neutral' | 'info' | 'success' | 'warning' | 'danger'> = {
  draft: 'neutral',
  submitted: 'info',
  returned: 'warning',
  accepted: 'success',
  included: 'success',
  'not-started': 'neutral',
  pending: 'info',
  edited: 'success',
  withdrawn: 'neutral',
  proposed: 'info',
  rejected: 'danger',
};
