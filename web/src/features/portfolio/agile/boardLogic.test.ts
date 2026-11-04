import { describe, expect, it } from 'vitest';
import {
  BACKLOG,
  burndownSeries,
  columnCode,
  columnStories,
  defaultSprintFilter,
  groupBySwimlane,
  isFullWeight,
  moveItem,
  rankAtEnd,
  storiesInScope,
  sumRatios,
  wipReached,
  wipState,
} from './boardLogic';

const story = (
  id: string,
  over: Partial<{
    code: string;
    epicId: string;
    sprintId: string | null;
    columnCode: string;
    rank: number;
    status: string;
    assignee: { id: string; nameAr: string | null; nameEn: string | null } | null;
  }> = {},
) => ({
  id,
  code: `P-US${id}`,
  epicId: 'e1',
  sprintId: null,
  columnCode: 'to-do',
  rank: 1,
  status: 'active',
  assignee: null,
  ...over,
});

const columns = [{ code: 'to-do', wipLimit: null }, { code: 'in-progress', wipLimit: 2 }, { code: 'done' }];

describe('WIP limits', () => {
  it('has no state without a limit', () => {
    expect(wipState({ code: 'a', wipLimit: null }, 50)).toBe('none');
    expect(wipState({ code: 'a' }, 0)).toBe('none');
    expect(wipReached({ code: 'a' }, 99)).toBe(false);
  });
  it('is below, at and over the limit', () => {
    expect(wipState({ code: 'a', wipLimit: 3 }, 2)).toBe('below');
    expect(wipState({ code: 'a', wipLimit: 3 }, 3)).toBe('at');
    expect(wipState({ code: 'a', wipLimit: 3 }, 4)).toBe('over');
  });
  it('refuses one more story once the limit is reached (server rule: count ≥ limit)', () => {
    expect(wipReached({ code: 'a', wipLimit: 2 }, 1)).toBe(false);
    expect(wipReached({ code: 'a', wipLimit: 2 }, 2)).toBe(true);
    expect(wipReached({ code: 'a', wipLimit: 2 }, 3)).toBe(true);
  });
});

describe('sprint scope', () => {
  it('opens on the active sprint, otherwise the backlog', () => {
    expect(
      defaultSprintFilter([
        { id: 's1', status: 'closed' },
        { id: 's2', status: 'active' },
      ]),
    ).toBe('s2');
    expect(defaultSprintFilter([{ id: 's1', status: 'planned' }])).toBe(BACKLOG);
  });
  it('keeps the active stories of the sprint or of the backlog', () => {
    const rows = [
      story('1', { sprintId: 's1' }),
      story('2'),
      story('3', { sprintId: 's1', status: 'cancelled' }),
      story('4', { sprintId: 's2' }),
    ];
    expect(storiesInScope(rows, 's1').map((s) => s.id)).toEqual(['1']);
    expect(storiesInScope(rows, BACKLOG).map((s) => s.id)).toEqual(['2']);
  });
});

describe('column ordering', () => {
  it('orders stories of a column by rank, then by code', () => {
    const rows = [
      story('10', { rank: 2 }),
      story('2', { rank: 1, code: 'P-US10' }),
      story('3', { rank: 1, code: 'P-US9' }),
      story('4', { columnCode: 'done' }),
    ];
    expect(columnStories(rows, columns, 'to-do').map((s) => s.id)).toEqual(['3', '2', '10']);
    expect(columnStories(rows, columns, 'done').map((s) => s.id)).toEqual(['4']);
  });
  it('shows stories of a removed column in the first column', () => {
    const rows = [story('1', { columnCode: 'gone' })];
    expect(columnStories(rows, columns, 'to-do').map((s) => s.id)).toEqual(['1']);
    expect(columnStories(rows, columns, 'in-progress')).toEqual([]);
  });
  it('ranks a moved story at the end of the target column', () => {
    expect(rankAtEnd([], 'x')).toBe(1);
    expect(
      rankAtEnd(
        [
          { id: 'a', rank: 3 },
          { id: 'x', rank: 9 },
        ],
        'x',
      ),
    ).toBe(4);
  });
  it('reorders columns', () => {
    expect(moveItem(['a', 'b', 'c'], 0, 2)).toEqual(['b', 'c', 'a']);
    expect(moveItem(['a', 'b', 'c'], 2, 1)).toEqual(['a', 'c', 'b']);
    expect(moveItem(['a', 'b'], 0, 5)).toEqual(['a', 'b']);
  });
  it('derives unique column codes', () => {
    expect(columnCode('QA Review', [])).toBe('qa-review');
    expect(columnCode('QA Review', ['qa-review'])).toBe('qa-review-2');
    expect(columnCode('مراجعة', [])).toBe('column');
  });
});

describe('swimlanes', () => {
  const epics = [
    { id: 'e2', sort: 2, code: 'P-E2' },
    { id: 'e1', sort: 1, code: 'P-E1' },
  ];
  const sara = { id: 'p1', nameAr: 'سارة', nameEn: 'Sara' };
  const adam = { id: 'p2', nameAr: 'آدم', nameEn: 'Adam' };
  const rows = [
    story('1', { epicId: 'e2', assignee: sara }),
    story('2', { epicId: 'e1', assignee: adam }),
    story('3', { epicId: 'e1' }),
    story('4', { epicId: 'unknown', assignee: sara }),
  ];
  it('puts every story in one lane when there are no swimlanes', () => {
    const lanes = groupBySwimlane(rows, 'none', epics, 'en');
    expect(lanes).toHaveLength(1);
    expect(lanes[0]?.stories).toHaveLength(4);
  });
  it('groups by epic in epic order, unknown epics last', () => {
    const lanes = groupBySwimlane(rows, 'epic', epics, 'en');
    expect(lanes.map((l) => [l.key, l.stories.map((s) => s.id)])).toEqual([
      ['e1', ['2', '3']],
      ['e2', ['1']],
      ['', ['4']],
    ]);
  });
  it('groups by assignee by name, unassigned last', () => {
    const lanes = groupBySwimlane(rows, 'assignee', epics, 'en');
    expect(lanes.map((l) => [l.key, l.stories.map((s) => s.id)])).toEqual([
      ['p2', ['2']],
      ['p1', ['1', '4']],
      ['', ['3']],
    ]);
  });
  it('omits empty lanes', () => {
    const lanes = groupBySwimlane([story('1', { epicId: 'e1' })], 'epic', epics, 'en');
    expect(lanes.map((l) => l.key)).toEqual(['e1']);
  });
});

describe('charts and weights', () => {
  it('skips future burndown days', () => {
    const series = burndownSeries([
      { date: '2026-11-01', remaining: 20, ideal: 20 },
      { date: '2026-11-02', remaining: 15, ideal: 10 },
      { date: '2026-11-03', remaining: -1, ideal: 0 },
    ]);
    expect(series.remaining).toEqual([20, 15, null]);
    expect(series.ideal).toEqual([20, 10, 0]);
  });
  it('sums weights exactly', () => {
    expect(sumRatios([0.1, 0.2, 0.3, 0.4])).toBe(1);
    expect(isFullWeight(sumRatios([0.3333, 0.3333, 0.3334]))).toBe(true);
    expect(isFullWeight(0.99)).toBe(false);
  });
});
