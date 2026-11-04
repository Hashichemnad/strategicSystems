import { describe, expect, it } from 'vitest';
import {
  fromPercent,
  isDelayed,
  milestoneTree,
  offersRecall,
  toPercent,
  visibleTabs,
  weightTotals,
} from './projectApi';
import { periodLabel } from './project/StageTabs';

describe('percent conversion (API ratios, 1.0 = 100 %)', () => {
  it('shows ratios as percentages with two decimals', () => {
    expect(toPercent(0.25)).toBe(25);
    expect(toPercent(0.12345)).toBe(12.35);
    expect(toPercent(1)).toBe(100);
    expect(toPercent(null)).toBeNull();
  });

  it('reads typed percentages back as ratios', () => {
    expect(fromPercent('25')).toBe(0.25);
    expect(fromPercent(' 33,33 ')).toBe(0.3333);
    expect(fromPercent('')).toBeNull();
    expect(fromPercent('abc')).toBeNull();
  });
});

describe('weight totals', () => {
  const m = (
    weight: number,
    extra: Partial<{ isHeader: boolean; highLevel: boolean; status: string }> = {},
  ) => ({
    weight,
    isHeader: false,
    highLevel: false,
    status: 'not-started',
    ...extra,
  });

  it('sums leaf weights of each plan without floating noise, ignoring phases and cancelled rows', () => {
    const totals = weightTotals([
      m(0.1),
      m(0.2),
      m(0.7),
      m(0.3, { isHeader: true }),
      m(0.5, { status: 'cancelled' }),
      m(0.6, { highLevel: true }),
      m(0.4, { highLevel: true }),
    ]);
    expect(totals.detailed).toBe(1);
    expect(totals.highLevel).toBe(1);
  });
});

describe('tabs per stage template', () => {
  it('shows every tab for a project', () => {
    const tabs = visibleTabs({ charter: true, lpo: true, teamFormation: true, closure: true, impact: true });
    expect(tabs).toContain('closure');
    expect(tabs).toContain('impact');
    expect(tabs).toContain('contracts');
  });

  it('gives initiatives the simplified set (no closure, impact or contracts)', () => {
    const tabs = visibleTabs({
      charter: false,
      lpo: false,
      teamFormation: false,
      closure: false,
      impact: false,
    });
    expect(tabs).not.toContain('closure');
    expect(tabs).not.toContain('impact');
    expect(tabs).not.toContain('contracts');
    expect(tabs).toContain('milestones');
  });

  it('hides Impact when it is switched off (Q-P3)', () => {
    expect(
      visibleTabs({ charter: true, lpo: true, teamFormation: true, closure: true, impact: false }),
    ).not.toContain('impact');
  });
});

describe('milestone helpers', () => {
  it('flags a milestone past its planned end below its weight as delayed', () => {
    expect(isDelayed(0.1, 0.2, '2027-03-01', '2027-03-02')).toBe(true);
    expect(isDelayed(0.2, 0.2, '2027-03-01', '2027-03-02')).toBe(false);
    expect(isDelayed(0.1, 0.2, '2027-03-02', '2027-03-02')).toBe(false);
    expect(isDelayed(0.1, 0.2, null, '2027-03-02')).toBe(false);
  });

  it('orders phases with their children nested', () => {
    const rows = [
      { id: 'b', parentId: 'p', code: 'P-1.2', sort: 0 },
      { id: 'p', parentId: null, code: 'P-1', sort: 0 },
      { id: 'a', parentId: 'p', code: 'P-1.1', sort: 0 },
      { id: 'x', parentId: null, code: 'P-2', sort: 0 },
    ];
    expect(milestoneTree(rows).map((r) => `${r.row.code}:${r.depth}`)).toEqual([
      'P-1:0',
      'P-1.1:1',
      'P-1.2:1',
      'P-2:0',
    ]);
  });

  it('labels a second window of the same month with its sequence', () => {
    expect(periodLabel({ period: '2027-03', sequence: 1 })).toBe('2027-03');
    expect(periodLabel({ period: '2027-03', sequence: 2 })).toBe('2027-03 (2)');
  });
});

describe('offersRecall', () => {
  it('is true only for the hold refusal that names recall', () => {
    expect(offersRecall({ problem: { params: { action: 'recall', process: 'portfolio.planning' } } })).toBe(
      true,
    );
    expect(offersRecall({ problem: { params: { status: 'active' } } })).toBe(false);
    expect(offersRecall(new Error('network'))).toBe(false);
    expect(offersRecall(null)).toBe(false);
  });
});
