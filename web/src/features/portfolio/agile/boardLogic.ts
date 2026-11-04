// Pure helpers of the agile screens (unit-tested in boardLogic.test.ts). The server enforces every rule again.

interface ColumnLike {
  code: string;
  wipLimit?: number | null;
}

interface StoryLike {
  id: string;
  code: string;
  epicId: string;
  sprintId: string | null;
  columnCode: string;
  rank: number;
  status: string;
  assignee: { id: string; nameAr: string | null; nameEn: string | null } | null;
}

interface EpicLike {
  id: string;
  sort: number;
  code: string;
}

/** Sprint filter of the board: one sprint's id, or the backlog (stories in no sprint). */
export type SprintFilter = string;
export const BACKLOG = 'backlog';

export type WipState = 'none' | 'below' | 'at' | 'over';

/** Where a column stands against its WIP limit (no limit → none). */
export function wipState(column: ColumnLike, count: number): WipState {
  const limit = column.wipLimit;
  if (limit === null || limit === undefined || limit <= 0) return 'none';
  if (count > limit) return 'over';
  return count === limit ? 'at' : 'below';
}

/** True when one more story may not enter the column (the server's rule: stories already there ≥ limit). */
export function wipReached(column: ColumnLike, count: number): boolean {
  const state = wipState(column, count);
  return state === 'at' || state === 'over';
}

/** The sprint the board opens on: the active sprint, otherwise the backlog. */
export function defaultSprintFilter(sprints: readonly { id: string; status: string }[]): SprintFilter {
  return sprints.find((s) => s.status === 'active')?.id ?? BACKLOG;
}

/** Active stories of the chosen sprint (or of the backlog). */
export function storiesInScope<T extends StoryLike>(stories: readonly T[], filter: SprintFilter): T[] {
  return stories.filter(
    (s) => s.status !== 'cancelled' && (filter === BACKLOG ? s.sprintId === null : s.sprintId === filter),
  );
}

const byRank = (a: StoryLike, b: StoryLike) =>
  a.rank - b.rank || a.code.localeCompare(b.code, undefined, { numeric: true });

/** Stories of one column in board order (rank, then code). Stories in an unknown column fall into the first column. */
export function columnStories<T extends StoryLike>(
  stories: readonly T[],
  columns: readonly ColumnLike[],
  code: string,
): T[] {
  const known = new Set(columns.map((c) => c.code));
  const first = columns[0]?.code;
  return stories
    .filter((s) => s.columnCode === code || (!known.has(s.columnCode) && code === first))
    .sort(byRank);
}

/** The rank that puts a story at the end of a column. */
export function rankAtEnd(
  storiesInColumn: readonly { id: string; rank: number }[],
  movingId: string,
): number {
  const others = storiesInColumn.filter((s) => s.id !== movingId);
  return others.length === 0 ? 1 : Math.max(...others.map((s) => s.rank)) + 1;
}

export interface Lane<T> {
  /** Epic id, employee id, or '' for "no epic / unassigned" / the single lane. */
  key: string;
  stories: T[];
}

/** Swimlanes: one lane (none), per epic in epic order, or per assignee by name; the empty lane comes last. */
export function groupBySwimlane<T extends StoryLike>(
  stories: readonly T[],
  swimlane: string,
  epics: readonly EpicLike[],
  language: string,
): Lane<T>[] {
  if (swimlane === 'epic') {
    const ordered = [...epics].sort(
      (a, b) => a.sort - b.sort || a.code.localeCompare(b.code, undefined, { numeric: true }),
    );
    const lanes: Lane<T>[] = ordered
      .map((e) => ({ key: e.id, stories: stories.filter((s) => s.epicId === e.id) }))
      .filter((l) => l.stories.length > 0);
    const known = new Set(ordered.map((e) => e.id));
    const rest = stories.filter((s) => !known.has(s.epicId));
    return rest.length ? [...lanes, { key: '', stories: rest }] : lanes;
  }
  if (swimlane === 'assignee') {
    const people = new Map<string, string>();
    for (const s of stories) {
      if (s.assignee) {
        const name =
          (language === 'ar'
            ? s.assignee.nameAr || s.assignee.nameEn
            : s.assignee.nameEn || s.assignee.nameAr) ?? '';
        people.set(s.assignee.id, name);
      }
    }
    const lanes: Lane<T>[] = [...people.entries()]
      .sort((a, b) => a[1].localeCompare(b[1], language))
      .map(([id]) => ({ key: id, stories: stories.filter((s) => s.assignee?.id === id) }));
    const rest = stories.filter((s) => !s.assignee);
    return rest.length ? [...lanes, { key: '', stories: rest }] : lanes;
  }
  return [{ key: '', stories: [...stories] }];
}

/** Moves one entry of a list (board settings: reorder columns). Out-of-range moves return the list unchanged. */
export function moveItem<T>(items: readonly T[], from: number, to: number): T[] {
  if (from < 0 || from >= items.length || to < 0 || to >= items.length || from === to) return [...items];
  const copy = [...items];
  const [item] = copy.splice(from, 1) as [T];
  copy.splice(to, 0, item);
  return copy;
}

/** A column code made from a label: lower-case ASCII letters, digits and dashes, unique among the existing codes. */
export function columnCode(label: string, existing: readonly string[]): string {
  const base =
    label
      .toLowerCase()
      .replace(/[^a-z0-9]+/gu, '-')
      .replace(/^-+|-+$/gu, '')
      .slice(0, 30) || 'column';
  let code = base;
  for (let n = 2; existing.includes(code); n++) code = `${base}-${n}`;
  return code;
}

/** Burndown points up to today: the server marks future days with remaining = -1. */
export function burndownSeries(points: readonly { date: string; remaining: number; ideal: number }[]) {
  return {
    dates: points.map((p) => p.date),
    ideal: points.map((p) => p.ideal),
    remaining: points.map((p) => (p.remaining < 0 ? null : p.remaining)),
  };
}

/** Sum of ratios without floating noise (4 decimals, as stored). */
export function sumRatios(values: readonly number[]): number {
  return values.reduce((sum, v) => sum + Math.round(v * 10000), 0) / 10000;
}

/** Weights must total exactly 100 %. */
export function isFullWeight(total: number): boolean {
  return Math.abs(total - 1) < 0.00005;
}
